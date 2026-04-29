using CsvHelper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Dynamic;
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

        public SanctionDownloader(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _exporter = new SanctionExcelExporter();
        }








        public async Task<object> DownloadAndParseAsync()
        {
            var allEntries = new List<SanctionEntry>();
            bool hasFailure = false;

            string folderName = Path.Combine(GlobalVariables.root_folder, "Logs", "UPDatabaseDownloadLogs");
            string today = DateTime.Now.ToString("dd-MM-yyyy");
            string fileName = "SanctionListDownload" + "_" + today;

            Logger.LogToFile(folderName, fileName,
                $"START: Download and parse process initiated at {DateTime.UtcNow:O}");

            try
            {
                // ───────────────────────────────
                // SEQUENTIAL DOWNLOADS
                // ───────────────────────────────

                foreach (var kvp in _sourceUrls)
                {
                    try
                    {
                        var xml = await DownloadStringAsync(kvp.Value);

                        var parser = _parsers.FirstOrDefault(p => p.Source == kvp.Key);
                        if (parser == null)
                            throw new Exception($"No parser found for source: {kvp.Key}");

                        var parsed = parser.Parse(xml);

                        Logger.LogToFile(folderName, fileName,
                            $"SUCCESS: {kvp.Key} downloaded and parsed successfully. Entries: {parsed.Count}");

                        allEntries.AddRange(parsed);
                    }
                    catch (Exception ex)
                    {
                        hasFailure = true;

                        Logger.LogToFile(folderName, fileName,
                            $"FAILED: {kvp.Key} download/parse failed. Error: {ex.Message}");
                    }
                }

                // ───────────────────────────────
                // UK SOURCE (SEPARATE STEP)
                // ───────────────────────────────

                try
                {
                    var ukXml = await DownloadUkXmlAsync();

                    var ukParser = _parsers.FirstOrDefault(p => p.Source == "UK");
                    if (ukParser == null)
                        throw new Exception("No parser found for UK source.");

                    var ukParsed = ukParser.Parse(ukXml);

                    Logger.LogToFile(folderName, fileName,
                        $"SUCCESS: UK downloaded and parsed successfully. Entries: {ukParsed.Count}");

                    allEntries.AddRange(ukParsed);
                }
                catch (Exception ex)
                {
                    hasFailure = true;

                    Logger.LogToFile(folderName, fileName,
                        $"FAILED: UK download/parse failed. Error: {ex.Message}");
                }

                Logger.LogToFile(folderName, fileName,
                    $"COMPLETED: Total entries collected: {allEntries.Count}");

                dynamic response = new ExpandoObject();
                response.data = allEntries;
                response.status = hasFailure ? "some" : "all";

                return response;
            }
            catch (Exception ex)
            {
                Logger.LogToFile(folderName, fileName,
                    $"FATAL ERROR in DownloadAndParseAsync: {ex.Message}");

                dynamic response = new ExpandoObject();
                response.data = new List<SanctionEntry>();
                response.status = "some";

                return response;
            }
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

            var result = await DownloadAndParseAsync();
            dynamic response = result;
            List<SanctionEntry> entries = response.data;
            var status = response.status;


            List<SanctionEntry> nigerianEntries = await NigerianSanctionListReader.LoadFromFileAsync(Path.Combine(GlobalVariables.nigerian_sanction_list_path, "NIGERIANSANCTIONLIST.json"));
            entries.AddRange(nigerianEntries);

            var excelPath = Path.Combine(outputDir,
               $"UPSanctionDB-{DateTime.UtcNow:dd-MM-yyyy}.xlsx");


            await ExportToExcelAsync(entries, excelPath);

            Console.WriteLine($"Exported {entries.Count} entries to {excelPath}");

            // ── 3. Overwrite base source file ────────────────────────────────────
            if (status == "all")
            {
                var baseSourcePath = Path.Combine(
               GlobalVariables.root_folder,
               "SanctionDatabase", "basesource",
               "UPSanctionDB.xlsx");

                await ExportToExcelAsync(entries, baseSourcePath);
                Console.WriteLine($"Base source file updated: {baseSourcePath}");
            }
           


           

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