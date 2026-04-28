using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Upsanctionscreener.Classess.Interfaces;
using Upsanctionscreener.Classess.Parsers;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Models;


namespace Upsanctionscreener.Classess
{
    public class SanctionDownloader : ISanctionDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly SanctionExcelExporter _exporter;
       
        private readonly UpSanctionSettingsService _settingsService;

        private readonly List<ISanctionParser> _parsers = new()
        {
            new UnSanctionParser(),
            new EuSanctionParser(),
            new OfacSanctionParser(),
            new UkSanctionParser(),
        };

        private readonly Dictionary<string, string> _sourceUrls = new()
        {
            ["UN"] = "https://scsanctions.un.org/resources/xml/en/consolidated.xml",
            ["EU"] = "https://webgate.ec.europa.eu/fsd/fsf/public/files/xmlFullSanctionsList_1_1/content?token=dG9rZW4tMjAxNw",
            ["OFAC"] = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/SDN.XML",
        };

        public SanctionDownloader()
        {
            _httpClient = new HttpClient();
            _exporter = new SanctionExcelExporter();
            
        }

        public async Task<List<SanctionEntry>> DownloadAndParseAsync()
        {
            var allEntries = new List<SanctionEntry>();

            // Download UN, EU, OFAC in parallel
            var parallelTasks = _sourceUrls.Select(async kvp =>
            {
                var xml = await DownloadStringAsync(kvp.Value);

                var parser = _parsers.FirstOrDefault(p => p.Source == kvp.Key);
                if (parser == null)
                    throw new Exception($"No parser found for source: {kvp.Key}");

                return parser.Parse(xml);
            });

            var results = await Task.WhenAll(parallelTasks);

            foreach (var list in results)
                allEntries.AddRange(list);

            // UK requires dynamic URL extraction
            var ukXml = await DownloadUkXmlAsync();
            var ukParser = _parsers.FirstOrDefault(p => p.Source == "UK");

            if (ukParser == null)
                throw new Exception("No parser found for UK source.");

            allEntries.AddRange(ukParser.Parse(ukXml));

            return allEntries;
        }

        public async Task ExportToExcelAsync(List<SanctionEntry> entries, string outputPath)
        {
            await Task.Run(() => _exporter.Export(entries, outputPath));
        }

        // Convenience method: download, parse, and export in one call
        public async Task DownloadParseAndExportAsync(UpSanctionSettingsService settingsService)
        {
            var outputDir = Path.Combine(GlobalVariables.root_folder, "SanctionDatabase");
            Directory.CreateDirectory(outputDir);

            List<SanctionEntry> entries = await DownloadAndParseAsync();
            List<SanctionEntry> nigerianEntries = await NigerianSanctionListReader.LoadFromFileAsync(Path.Combine(GlobalVariables.nigerian_sanction_list_path, "NIGERIANSANCTIONLIST.json"));
            entries.AddRange(nigerianEntries);

            var excelPath = Path.Combine(outputDir,
               $"UPSanctionDB-{DateTime.UtcNow:dd-MM-yyyy}.xlsx");


            await ExportToExcelAsync(entries, excelPath);

            Console.WriteLine($"Exported {entries.Count} entries to {excelPath}");

            // ── 3. Overwrite base source file ────────────────────────────────────
            var baseSourcePath = Path.Combine(
                GlobalVariables.root_folder,
                "SanctionDatabase", "basesource",
                "UPSanctionDB.xlsx");

            await ExportToExcelAsync(entries, baseSourcePath);
            Console.WriteLine($"Base source file updated: {baseSourcePath}");

            // ── 4. Reload BK-Tree directly via singleton ─────────────────────────
            var settingsResult = await settingsService.GetScanSettingsAsync();
            if (!settingsResult.Success)
            {
                Console.WriteLine($"Tree not reloaded — failed to get settings: {settingsResult.Error}");
                return;
            }

            //double threshold = settingsResult.Data.ScanThreshold / 100.0;

            //SanctionBKTree.Instance.Reset();
            //SanctionBKTree.Instance.Configure(threshold, caseSensitive: false);
            //SanctionBKTree.Instance.Load(entries);

            //Console.WriteLine($"BK-Tree reloaded with {SanctionBKTree.Instance.NodeCount} nodes.");

        }

        private async Task<string> DownloadUkXmlAsync()
        {
            var html = await _httpClient.GetStringAsync(
                "https://www.gov.uk/government/publications/the-uk-sanctions-list"
            );

            // Find first XML link in the HTML page
            var match = Regex.Match(html, "href=\"(https?://[^\"]+\\.xml)\"", RegexOptions.IgnoreCase);

            if (!match.Success)
                throw new Exception("UK XML link not found on page.");

            var xmlUrl = match.Groups[1].Value;

            return await DownloadStringAsync(xmlUrl);
        }

        private async Task<string> DownloadStringAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36"
            );

            request.Headers.Accept.ParseAdd("application/xml");
            request.Headers.Accept.ParseAdd("text/xml");
            request.Headers.Accept.ParseAdd("*/*");

            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
    }
}