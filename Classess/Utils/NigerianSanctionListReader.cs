using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Utils
{
    public static class NigerianSanctionListReader
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static List<SanctionEntry> LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Sanction list file not found: {filePath}", filePath);

            using (var stream = File.OpenRead(filePath))
            {
                var entries = JsonSerializer.Deserialize<List<SanctionEntry>>(stream, _options);
                return entries ?? new List<SanctionEntry>();
            }
        }

        public static async Task<List<SanctionEntry>> LoadFromFileAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Sanction list file not found: {filePath}", filePath);

            using (var stream = File.OpenRead(filePath))  // ← plain 'using', no 'await'
            {
                var entries = await JsonSerializer.DeserializeAsync<List<SanctionEntry>>(
                    stream, _options, cancellationToken);

                return entries ?? new List<SanctionEntry>();
            }
        }
    }

}
