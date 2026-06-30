using Microsoft.Extensions.Configuration;

namespace Upsanctionscreener.Classess.Utils
{
    public static class AppConfigFetcher
    {
        private static IConfiguration? _configuration;

        public static void Initialize(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public static string? GetValue(string key)
        {
            if (_configuration == null)
                throw new InvalidOperationException(
                    "AppConfigFetcher has not been initialized.");

            return _configuration[key];
        }

        public static T GetValue<T>(string key)
        {
            if (_configuration == null)
                throw new InvalidOperationException(
                    "AppConfigFetcher has not been initialized.");

            return _configuration.GetValue<T>(key)!;
        }
    }
}