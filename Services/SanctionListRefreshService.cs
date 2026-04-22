using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Classess;
using Upsanctionscreener.Classess.Utils;

namespace Upsanctionscreener.Services
{
    public class SanctionListRefreshService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SanctionListRefreshService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("Sanction List Refresh Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = GetDelayUntilNextRun();

                Console.WriteLine(
                    $"Next sanction list refresh scheduled in {(int)delay.TotalHours}h {delay.Minutes}m (at 23:30 UTC / 00:30 WAT).");

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                await RunRefreshAsync(stoppingToken);
            }

            Console.WriteLine("Sanction List Refresh Service stopped.");
        }

        private async Task RunRefreshAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("Starting scheduled sanction list refresh...");

            try
            {
                using var scope = _scopeFactory.CreateScope();

                var settingsService = scope.ServiceProvider
                    .GetRequiredService<UpSanctionSettingsService>();

                var downloader = new SanctionDownloader();
                await downloader.DownloadParseAndExportAsync(settingsService);

                Console.WriteLine("Sanction list refresh completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sanction list refresh failed: {ex.Message}");
            }
        }

        private static TimeSpan GetDelayUntilNextRun()
        {
            var now = DateTime.UtcNow;
            var nextRun = DateTime.UtcNow.Date.AddHours(23).AddMinutes(30); // 23:30 UTC = 00:30 WAT

            if (now >= nextRun)
                nextRun = nextRun.AddDays(1);

            return nextRun - now;
        }
    }
}