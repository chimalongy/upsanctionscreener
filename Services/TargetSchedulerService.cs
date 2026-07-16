using Quartz;
using System.Net.Http.Json;
using Upsanctionscreener.Classess.Utils;

namespace Upsanctionscreener.Services
{
    public class TargetSchedulerService
    {
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger<TargetSchedulerService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        // Named HttpClient registered in Program.cs, pointed at the
        // transaction screener app (http://localhost:3001).
        private const string TxnScreenerClientName = "TransactionScreenerApi";

        public TargetSchedulerService(
            ISchedulerFactory schedulerFactory,
            ILogger<TargetSchedulerService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _schedulerFactory = schedulerFactory;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        // ── Schedule or reschedule a target ───────────────────────────────────
        // transactionScan = true  → delegate to the transaction screener app
        //                           (POST /api/start-job) instead of Quartz.
        // transactionScan = false → existing Quartz behaviour.
        public async Task ScheduleOrUpdateTargetAsync(
            int targetId,
            string targetName,
            string targetType,
            string frequency,
            AutomationSettings automation,
            bool transactionScan = false)
        {
            if (transactionScan)
            {
                await StartTransactionJobAsync(targetId, targetName);
                return;
            }

            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey($"target-scan-{targetId}", "target-scans");

            // Always delete the old job first — handles both add and edit cleanly
            await scheduler.DeleteJob(jobKey);

            if (!automation.Automate)
            {
                _logger.LogInformation(
                    "[Scheduler] Target [{Id}] '{Name}' is manual only — no job scheduled.",
                    targetId, targetName);
                return;
            }

            var job = JobBuilder.Create<Jobs.TargetScanJob>()
                .WithIdentity(jobKey)
                .WithDescription($"Sanction scan — {targetName}")
                .UsingJobData("targetId", targetId)
                .UsingJobData("targetName", targetName)
                .UsingJobData("targetType", targetType)
                .UsingJobData("targetfrequency", frequency)
                .StoreDurably()
                .Build();

            ITrigger trigger;
            try
            {
                trigger = BuildTrigger(targetId, automation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Scheduler] Could not build trigger for target [{Id}] — schedule not created.",
                    targetId);
                return;
            }

            await scheduler.ScheduleJob(job, trigger);

            _logger.LogInformation(
                "[Scheduler] Scheduled target [{Id}] '{Name}' — frequency: {Freq}",
                targetId, targetName, automation.Frequency);
        }

        // ── Remove a target's schedule entirely ────────────────────────────────
        public async Task RemoveTargetScheduleAsync(int targetId, bool transactionScan = false)
        {
            if (transactionScan)
            {
                await StopTransactionJobAsync(targetId);
                return;
            }

            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey($"target-scan-{targetId}", "target-scans");
            var deleted = await scheduler.DeleteJob(jobKey);

            if (deleted)
                _logger.LogInformation("[Scheduler] Removed schedule for target [{Id}].", targetId);
            else
                _logger.LogDebug("[Scheduler] No schedule found for target [{Id}] — nothing to remove.", targetId);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TRANSACTION SCREENER APP — HTTP calls to localhost:3001
        // ══════════════════════════════════════════════════════════════════════

        public async Task StartTransactionJobAsync(int targetId, string targetName)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(TxnScreenerClientName);
                var response = await client.PostAsJsonAsync("/api/start-job", new { target_id = targetId });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response);
                    _logger.LogError(
                        "[TxnScreener] start-job failed for target [{Id}] '{Name}'. Status: {Status}. Body: {Body}",
                        targetId, targetName, (int)response.StatusCode, body);
                    return;
                }

                _logger.LogInformation(
                    "[TxnScreener] start-job succeeded for target [{Id}] '{Name}'.", targetId, targetName);
            }
            catch (Exception ex)
            {
                // Non-fatal — target settings are already saved; log and continue
                // so a transaction screener outage doesn't block Upsert.
                _logger.LogError(ex,
                    "[TxnScreener] Could not reach transaction screener app to start job for target [{Id}] '{Name}'.",
                    targetId, targetName);
            }
        }

        public async Task StopTransactionJobAsync(int targetId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(TxnScreenerClientName);
                var response = await client.PostAsJsonAsync("/api/stop-job", new { target_id = targetId });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response);
                    _logger.LogError(
                        "[TxnScreener] stop-job failed for target [{Id}]. Status: {Status}. Body: {Body}",
                        targetId, (int)response.StatusCode, body);
                    return;
                }

                _logger.LogInformation("[TxnScreener] stop-job succeeded for target [{Id}].", targetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[TxnScreener] Could not reach transaction screener app to stop job for target [{Id}].",
                    targetId);
            }
        }

        public async Task<(bool Success, string Message)> RunTransactionJobNowAsync(int targetId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(TxnScreenerClientName);
                var response = await client.PostAsJsonAsync("/api/run-job", new { target_id = targetId });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response);
                    _logger.LogError(
                        "[TxnScreener] run-job failed for target [{Id}]. Status: {Status}. Body: {Body}",
                        targetId, (int)response.StatusCode, body);
                    return (false, $"Transaction screener returned {(int)response.StatusCode}: {body}");
                }

                _logger.LogInformation("[TxnScreener] run-job succeeded for target [{Id}].", targetId);
                return (true, "Transaction scan job triggered.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[TxnScreener] Could not reach transaction screener app to run job for target [{Id}].",
                    targetId);
                return (false, "Could not reach the transaction screener app.");
            }
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
        {
            try { return await response.Content.ReadAsStringAsync(); }
            catch { return "<unreadable body>"; }
        }

        // ── Build the correct Quartz trigger from AutomationSettings ──────────
        private static ITrigger BuildTrigger(int targetId, AutomationSettings auto)
        {
            var triggerKey = new TriggerKey($"trigger-{targetId}", "target-scans");

            var builder = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow();

            switch (auto.Frequency?.ToLowerInvariant())
            {
                case "minutely":
                    {
                        int mins = auto.IntervalMinutes > 0 ? auto.IntervalMinutes : 30;
                        return builder
                            .WithSimpleSchedule(x => x
                                .WithIntervalInMinutes(mins)
                                .RepeatForever())
                            .Build();
                    }

                case "hourly":
                    {
                        int hours = auto.IntervalHours > 0 ? auto.IntervalHours : 1;
                        return builder
                            .WithSimpleSchedule(x => x
                                .WithIntervalInHours(hours)
                                .RepeatForever())
                            .Build();
                    }

                case "daily":
                    {
                        var (h, m) = ParseTime(auto.StartTime);
                        return builder
                            .WithCronSchedule($"0 {m} {h} * * ?")
                            .Build();
                    }

                case "weekly":
                    {
                        var (h, m) = ParseTime(auto.StartTime);
                        int quartzDay = auto.Weekday + 1;
                        return builder
                            .WithCronSchedule($"0 {m} {h} ? * {quartzDay}")
                            .Build();
                    }

                case "monthly":
                    {
                        var (h, m) = ParseTime(auto.StartTime);
                        int day = Math.Clamp(auto.DayOfMonth, 1, 28);
                        return builder
                            .WithCronSchedule($"0 {m} {h} {day} * ?")
                            .Build();
                    }

                default:
                    throw new ArgumentException(
                        $"Unknown frequency '{auto.Frequency}' for target {targetId}.");
            }
        }

        // ── Parse "HH:mm" string into (hour, minute) ──────────────────────────
        private static (int hour, int minute) ParseTime(string? time)
        {
            if (string.IsNullOrWhiteSpace(time))
                return (2, 0); // default: 02:00

            var parts = time.Split(':');
            int h = parts.Length > 0 && int.TryParse(parts[0], out var ph) ? ph : 2;
            int m = parts.Length > 1 && int.TryParse(parts[1], out var pm) ? pm : 0;
            return (h, m);
        }
    }
}