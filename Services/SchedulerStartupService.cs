using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;

namespace Upsanctionscreener.Services
{
    /// <summary>
    /// Runs on application startup and restores all automated target schedules
    /// from the database. Required because Quartz uses an in-memory store,
    /// so all jobs are lost when the application restarts.
    /// </summary>
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

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var schedulerSvc = scope.ServiceProvider.GetRequiredService<TargetSchedulerService>();
            var settingsSvc = new UpSanctionSettingsService(db);

            // GetTargetSettingsAsync returns List<TargetSetting> which already has
            // AutomationSettings fully typed — no re-deserialization needed
            var result = await settingsSvc.GetTargetSettingsAsync();

            if (!result.Success || result.Data is null)
            {
                _logger.LogWarning(
                    "[SchedulerStartup] Could not load targets: {Error}", result.Error);
                return;
            }

            int restored = 0;
            int skipped = 0;

            foreach (var target in result.Data)
            {
                // AutomationSettings is never null on TargetSetting (initialised with new())
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
                }
            }

            _logger.LogInformation(
                "[SchedulerStartup] Done — {Restored} schedule(s) restored, {Skipped} manual target(s) skipped.",
                restored, skipped);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}