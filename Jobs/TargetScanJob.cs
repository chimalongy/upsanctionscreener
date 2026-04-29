using Quartz;
using Upsanctionscreener.Classess.Search;
using Upsanctionscreener.Classess.Utils;

namespace Upsanctionscreener.Jobs
{
    [DisallowConcurrentExecution]
    public class TargetScanJob : IJob
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TargetScanJob> _logger;

        public TargetScanJob(IServiceScopeFactory scopeFactory, ILogger<TargetScanJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var dataMap = context.JobDetail.JobDataMap;
            var targetId = dataMap.GetInt("targetId");
            var targetName = dataMap.GetString("targetName");
            var targetType = dataMap.GetString("targetType");
            object targetFreqency = dataMap.Get("targetfrequency");

            _logger.LogInformation(
                "[TargetScanJob] Starting scan — Target [{Id}] '{Name}' (type: {Type})",
                targetId, targetName, targetType);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await Scanner.TargetScanScreener(targetId, targetName, targetFreqency, _scopeFactory);
                // ── Plug your actual scan logic here ──────────────────────
                // var scanService = scope.ServiceProvider.GetRequiredService<IYourScanService>();
                // await scanService.ScanTargetAsync(targetId, targetType);
                // ─────────────────────────────────────────────────────────

                _logger.LogInformation(
                    "[TargetScanJob] Completed scan — Target [{Id}] '{Name}'",
                    targetId, targetName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[TargetScanJob] Scan failed — Target [{Id}] '{Name}'",
                    targetId, targetName);

                throw new JobExecutionException(ex, refireImmediately: false);
            }
        }
    }
}