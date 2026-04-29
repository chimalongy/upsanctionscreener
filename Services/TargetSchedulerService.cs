using Quartz;
using Upsanctionscreener.Classess.Utils;

namespace Upsanctionscreener.Services
{
    public class TargetSchedulerService
    {
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger<TargetSchedulerService> _logger;

        public TargetSchedulerService(
            ISchedulerFactory schedulerFactory,
            ILogger<TargetSchedulerService> logger)
        {
            _schedulerFactory = schedulerFactory;
            _logger = logger;
        }

        // ── Schedule or reschedule a target ───────────────────────────────────
        public async Task ScheduleOrUpdateTargetAsync(
            int targetId,
            string targetName,
            string targetType,
            string frequency,
            AutomationSettings automation)
        {
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

        // ── Remove a target's job entirely ────────────────────────────────────
        public async Task RemoveTargetScheduleAsync(int targetId)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey($"target-scan-{targetId}", "target-scans");
            var deleted = await scheduler.DeleteJob(jobKey);

            if (deleted)
                _logger.LogInformation("[Scheduler] Removed schedule for target [{Id}].", targetId);
            else
                _logger.LogDebug("[Scheduler] No schedule found for target [{Id}] — nothing to remove.", targetId);
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
                // ── Every N minutes ───────────────────────────────────────────
                // e.g. IntervalMinutes = 30  →  runs every 30 minutes
                case "minutely":
                    {
                        int mins = auto.IntervalMinutes > 0 ? auto.IntervalMinutes : 30;
                        return builder
                            .WithSimpleSchedule(x => x
                                .WithIntervalInMinutes(mins)
                                .RepeatForever())
                            .Build();
                    }

                // ── Every N hours ─────────────────────────────────────────────
                // e.g. IntervalHours = 6  →  runs every 6 hours
                case "hourly":
                    {
                        int hours = auto.IntervalHours > 0 ? auto.IntervalHours : 1;
                        return builder
                            .WithSimpleSchedule(x => x
                                .WithIntervalInHours(hours)
                                .RepeatForever())
                            .Build();
                    }

                // ── Every day at a fixed time ─────────────────────────────────
                // e.g. StartTime = "02:30"  →  cron "0 30 2 * * ?"
                case "daily":
                    {
                        var (h, m) = ParseTime(auto.StartTime);
                        return builder
                            .WithCronSchedule($"0 {m} {h} * * ?")
                            .Build();
                    }

                // ── A specific weekday at a fixed time ────────────────────────
                // e.g. Weekday = 1 (Mon), StartTime = "09:00"  →  cron "0 0 9 ? * 2"
                // auto.Weekday is 0-based (0=Sun…6=Sat)
                // Quartz weekday is 1-based (1=Sun…7=Sat)  →  add 1
                case "weekly":
                    {
                        var (h, m) = ParseTime(auto.StartTime);
                        int quartzDay = auto.Weekday + 1;
                        return builder
                            .WithCronSchedule($"0 {m} {h} ? * {quartzDay}")
                            .Build();
                    }

                // ── A specific day of the month at a fixed time ───────────────
                // e.g. DayOfMonth = 15, StartTime = "08:00"  →  cron "0 0 8 15 * ?"
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