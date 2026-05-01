// Controllers/ScanApiController.cs
using Microsoft.AspNetCore.Mvc;
using Upsanctionscreener.Classess;
using Upsanctionscreener.Classess.Search;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models.Api;
using Upsanctionscreener.Services;

using static Upsanctionscreener.Classess.Search.PEPBKTree;

namespace Upsanctionscreener.Controllers
{
    /// <summary>
    /// Public REST API for sanction screening.
    /// All routes are protected by ApiKeyAuthMiddleware — every request
    /// must include X-Api-Key and X-Client-Id headers.
    /// </summary>
    [ApiController]
    [Route("api/v1")]
    public class ScanApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly UpSanctionSettingsService _settingsService;
        private readonly IServiceScopeFactory _scopeFactory;

        public ScanApiController(
            AppDbContext db,
            UpSanctionSettingsService settingsService,
            IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _settingsService = settingsService;
            _scopeFactory = scopeFactory;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly string[] ValidFields = { "name", "address", "email", "phone" };

        /// <summary>
        /// Resolve the threshold to use: caller-supplied value takes priority,
        /// otherwise fall back to the saved scan setting.
        /// </summary>
        private async Task<double> ResolveThresholdAsync(double? requested)
        {
            if (requested.HasValue)
                return Math.Clamp(requested.Value, 0.01, 1.0);

            var settingsResult = await _settingsService.GetScanSettingsAsync();
            var saved = settingsResult.Success && settingsResult.Data is not null
                ? settingsResult.Data.ScanThreshold
                : 90;

            // ScanThreshold is stored as 1-100; convert to 0.0-1.0 for the scanner
            return saved / 100.0;
        }

        /// <summary>
        /// Run a single term through sanctions + PEP trees.
        /// Returns (sanctions, peps).
        /// </summary>
        private async Task<(List<SanctionHit> sanctions, List<PepHit> peps)>
            RunScreenAsync(string searchTerm, string searchField, double threshold)
        {
            // ── Sanctions ─────────────────────────────────────────────────────
            var filePath = Path.Combine(GlobalVariables.root_folder, "SanctionDatabase", "basesource", "UPSanctionDB.xlsx");
            var sanctionList = SanctionExcelReader.LoadFromExcel(filePath);

            var scanResult = await Scanner.SingleScanScreener(threshold, searchTerm, searchField, _scopeFactory);
            dynamic response = scanResult;
            var rawMatches = (List<(string EntryId, string Matched, double Similarity, int EditDistance)>)response.data;

            var sanctions = rawMatches
                .Select(c => new
                {
                    entry = sanctionList.FirstOrDefault(x => x.ID == c.EntryId),
                    c.Similarity
                })
                .Where(x => x.entry is not null)
                .Select(x => new SanctionHit
                {
                    Similarity = (x.Similarity * 100).ToString("F2") + "%",
                    SanctionItem = x.entry
                })
                .ToList();

            // ── PEPs ──────────────────────────────────────────────────────────
            var pepEntries = GlobalFunctions.FetchAllPeps();
            var pepTree = new PEPSanctionBKTree();
            pepTree.Load(pepEntries);

            var peps = pepTree.Search(searchTerm, threshold)
                .Select(c => new
                {
                    entry = pepEntries.FirstOrDefault(x => x.Id == int.Parse(c.EntryId)),
                    c.Similarity
                })
                .Where(x => x.entry is not null)
                .Select(x => new PepHit
                {
                    Similarity = (x.Similarity * 100).ToString("F2") + "%",
                    PepItem = x.entry
                })
                .ToList();

            return (sanctions, peps);
        }

        // ══════════════════════════════════════════════════════════════════════
        // GET  /api/v1/ping
        // Quick health-check — confirms auth is working.
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            var clientId = HttpContext.Items["ApiClientId"]?.ToString() ?? "unknown";
            return Ok(new
            {
                success = true,
                message = "Authenticated successfully.",
                client_id = clientId,
                timestamp = DateTime.UtcNow
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        // POST /api/v1/scan/single
        //
        // Screen a single name / address / email / phone.
        //
        // Headers:
        //   X-Api-Key:    <your encrypted key>
        //   X-Client-Id:  <your client id>
        //
        // Body:
        // {
        //   "search_term":  "John Doe",
        //   "search_field": "name",        // name | address | email | phone
        //   "threshold":    0.85           // optional, 0.0-1.0
        // }
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost("scan/single")]
        public async Task<IActionResult> SingleScan([FromBody] SingleScanApiRequest req)
        {
            // ── Validate ──────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(req.SearchTerm))
                return BadRequest(new { success = false, message = "search_term is required." });

            if (!ValidFields.Contains(req.SearchField?.ToLowerInvariant()))
                return BadRequest(new
                {
                    success = false,
                    message = $"search_field must be one of: {string.Join(", ", ValidFields)}."
                });

            // ── Resolve threshold ─────────────────────────────────────────────
            var threshold = await ResolveThresholdAsync(req.Threshold);

            // ── Screen ────────────────────────────────────────────────────────
            try
            {
                var (sanctions, peps) = await RunScreenAsync(
                    req.SearchTerm,
                    req.SearchField.ToLowerInvariant(),
                    threshold);

                return Ok(new
                {
                    success = true,
                    search_term = req.SearchTerm,
                    search_field = req.SearchField,
                    threshold = threshold,
                    has_hits = sanctions.Count > 0 || peps.Count > 0,
                    sanctions,
                    peps
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // POST /api/v1/scan/bulk
        //
        // Screen up to 500 records in one call.  Each item is screened
        // independently; results are returned in the same order as the input.
        //
        // Body:
        // {
        //   "search_field": "name",
        //   "threshold":    0.85,
        //   "items": [
        //     { "id": "REF-001", "search_term": "John Doe" },
        //     { "id": "REF-002", "search_term": "Jane Smith" }
        //   ]
        // }
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost("scan/bulk")]
        public async Task<IActionResult> BulkScan([FromBody] BulkScanApiRequest req)
        {
            // ── Validate ──────────────────────────────────────────────────────
            if (req.Items is null || req.Items.Count == 0)
                return BadRequest(new { success = false, message = "items array is required and must not be empty." });

            if (req.Items.Count > 500)
                return BadRequest(new { success = false, message = "Maximum 500 items per bulk request." });

            if (!ValidFields.Contains(req.SearchField?.ToLowerInvariant()))
                return BadRequest(new
                {
                    success = false,
                    message = $"search_field must be one of: {string.Join(", ", ValidFields)}."
                });

            // ── Resolve threshold once for the whole batch ────────────────────
            var threshold = await ResolveThresholdAsync(req.Threshold);
            var searchField = req.SearchField.ToLowerInvariant();

            // ── Process each item ─────────────────────────────────────────────
            var results = new List<BulkScanResultItem>(req.Items.Count);

            foreach (var item in req.Items)
            {
                if (string.IsNullOrWhiteSpace(item.SearchTerm))
                {
                    results.Add(new BulkScanResultItem
                    {
                        Id = item.Id,
                        SearchTerm = item.SearchTerm,
                        Error = "search_term is empty — skipped."
                    });
                    continue;
                }

                try
                {
                    var (sanctions, peps) = await RunScreenAsync(item.SearchTerm, searchField, threshold);

                    results.Add(new BulkScanResultItem
                    {
                        Id = item.Id,
                        SearchTerm = item.SearchTerm,
                        Sanctions = sanctions,
                        Peps = peps,
                        HasHits = sanctions.Count > 0 || peps.Count > 0
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new BulkScanResultItem
                    {
                        Id = item.Id,
                        SearchTerm = item.SearchTerm,
                        Error = ex.Message
                    });
                }
            }

            return Ok(new
            {
                success = true,
                search_field = searchField,
                threshold,
                total = results.Count,
                hit_count = results.Count(r => r.HasHits),
                results
            });
        }
    }
}