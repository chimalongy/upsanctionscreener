using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;

namespace Upsanctionscreener.Services
{
    public class SchedulerStartupService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SchedulerStartupService> _logger;

        public SchedulerStartupService(
            IServiceScopeFactory scopeFactory,
            ILogger<SchedulerStartupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[SchedulerStartup] Restoring target schedules…");
            Console.WriteLine("[SchedulerStartup] Restoring target schedules…");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var schedulerSvc = scope.ServiceProvider.GetRequiredService<TargetSchedulerService>();
            var settingsSvc = new UpSanctionSettingsService(db);

            var result = await settingsSvc.GetTargetSettingsAsync();

            if (!result.Success || result.Data is null)
            {
                _logger.LogWarning(
                    "[SchedulerStartup] Could not load targets: {Error}", result.Error);
                Console.WriteLine($"[SchedulerStartup] Could not load targets: {result.Error}");
                return;
            }

            int restored = 0;
            int skipped = 0;

            foreach (var target in result.Data)
            {
                if (!target.AutomationSettings.Automate)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    await schedulerSvc.ScheduleOrUpdateTargetAsync(
                        target.Id,
                        target.TargetName,
                        target.TargetType,
                        target.AutomationSettings.Frequency,
                        target.AutomationSettings);

                    restored++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[SchedulerStartup] Failed to restore schedule for target [{Id}] '{Name}'.",
                        target.Id, target.TargetName);
                    Console.WriteLine($"[SchedulerStartup] Failed to restore schedule for target [{target.Id}] '{target.TargetName}'.");
                }
            }

            _logger.LogInformation(
                "[SchedulerStartup] Done — {Restored} Quartz schedule(s) restored, {Skipped} manual target(s) skipped.",
                restored, skipped);
            Console.WriteLine($"[SchedulerStartup] Done — {restored} Quartz schedule(s) restored, {skipped} manual target(s) skipped.");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}