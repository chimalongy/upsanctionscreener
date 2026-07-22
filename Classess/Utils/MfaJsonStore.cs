using System.Text.Json;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Utils
{
    public class MfaJsonStore
    {
        private readonly string _filePath;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public MfaJsonStore(IConfiguration config)
        {
            string relative = GlobalVariables.MFASecretsFilePath;
            _filePath = Path.Combine(Directory.GetCurrentDirectory(), relative);

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public async Task<Dictionary<int, MfaRecord>> LoadAllAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath))
                    return new Dictionary<int, MfaRecord>();

                var json = await File.ReadAllTextAsync(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new Dictionary<int, MfaRecord>();

                return JsonSerializer.Deserialize<Dictionary<int, MfaRecord>>(json) ?? new();
            }
            finally { _lock.Release(); }
        }

        public async Task SaveAllAsync(Dictionary<int, MfaRecord> data)
        {
            await _lock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_filePath, json);
            }
            finally { _lock.Release(); }
        }
    }
}