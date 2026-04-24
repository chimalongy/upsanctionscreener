using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Upsanctionscreener.Classess;
using Upsanctionscreener.Classess.Search;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;
using Upsanctionscreener.Models.ViewModels;
using Upsanctionscreener.Services;
using static Upsanctionscreener.Classess.Search.BKTree;
using static Upsanctionscreener.Classess.Search.PEPBKTree;
namespace Upsanctionscreener.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly SanctionBKTree _sanction_tree;
        private readonly UpSanctionSettingsService _settingsService;

        public DashboardController(AppDbContext db, IConfiguration config, SanctionBKTree tree, UpSanctionSettingsService settingsService)
        {
            _db = db;
            _config = config;
            _sanction_tree = tree;
            _settingsService = settingsService;
        }

        // ══════════════════════════════════════════════════════════════════════
        // VIEWS
        // ══════════════════════════════════════════════════════════════════════

        public IActionResult Index() => View();

        [Route("Dashboard/MultiScan")]
        public IActionResult MultiScan() =>
            View("~/Views/Dashboard/MultiScan.cshtml");

        [Route("Dashboard/List/UBOs")]
        public IActionResult UBOs() =>
            View("~/Views/Dashboard/List/UBOs.cshtml");

        [Route("Dashboard/List/PEPs")]
        public IActionResult PEPs() =>
            View("~/Views/Dashboard/List/PEPs.cshtml");

        [Route("Dashboard/List/NigerianSanctionList")]
        public IActionResult NigerianSanctionList() =>
            View("~/Views/Dashboard/List/NigerianSanctionList.cshtml");

        [Route("Dashboard/Reports/MerchantScanReports")]
        public IActionResult MerchantScanReports() =>
            View("~/Views/Dashboard/Reports/MerchantScanReports.cshtml");

        [Route("Dashboard/Reports/CustomerReports")]
        public IActionResult CustomerReports() =>
            View("~/Views/Dashboard/Reports/CustomerReports.cshtml");

        [Route("Dashboard/Settings/ScanSettings")]
        public IActionResult ScanSettings() =>
            View("~/Views/Dashboard/Settings/ScanSettings.cshtml");

        [Route("Dashboard/Settings/Users")]
        public IActionResult Users() =>
            View("~/Views/Dashboard/Settings/Users.cshtml");

        [Route("Dashboard/Settings/TargetSettings")]
        public IActionResult TargetSettings() =>
            View("~/Views/Dashboard/Settings/TargetSettings.cshtml");

        [Route("Dashboard/Settings/SourceSettings")]
        public IActionResult SourceSettings() =>
            View("~/Views/Dashboard/Settings/SourceSettings.cshtml");

        [Route("Dashboard/Settings/AuditLogs")]
        public IActionResult AuditLogs() =>
            View("~/Views/Dashboard/Settings/AuditLogs.cshtml");

        // ══════════════════════════════════════════════════════════════════════
        // AUDIT LOGS API
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/Settings/AuditLogs/GetAll")]
        public async Task<IActionResult> AuditLogsGetAll()
        {
            var logs = await GlobalFunctions.GetAllAuditLogsAsync(_db);
            return Json(logs);
        }

        // ══════════════════════════════════════════════════════════════════════
        // USERS API
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/Settings/Users/GetAll")]
        public async Task<IActionResult> UsersGetAll()
        {
            var users = await GlobalFunctions.GetAllUsersAsync(_db);
            return Json(users);
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/Create")]
        public async Task<IActionResult> UsersCreate([FromBody] CreateUserRequest req)
        {
            var (result, error) = await GlobalFunctions.CreateUserAsync(
                _db, _config,
                req.FirstName, req.LastName, req.Email,
                req.Department, req.Role);

            if (error == "CONFLICT")
                return Conflict(new { message = "A user with this email already exists." });

            if (error is not null)
                return BadRequest(new { message = error });

            return Json(result);
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/Delete/{id:int}")]
        public async Task<IActionResult> UsersDelete(int id)
        {
            var error = await GlobalFunctions.DeleteUserAsync(_db, id);

            if (error == "NOT_FOUND")
                return NotFound(new { message = "User not found." });

            return Json(new { message = "User deleted." });
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/UpdateStatus/{id:int}")]
        public async Task<IActionResult> UsersUpdateStatus(int id, [FromBody] UpdateStatusRequest req)
        {
            var (result, error) = await GlobalFunctions.UpdateUserStatusAsync(_db, id, req.ProfileStatus);

            if (error == "NOT_FOUND")
                return NotFound(new { message = "User not found." });

            if (error is not null)
                return BadRequest(new { message = error });

            return Json(result);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TARGET SETTINGS API
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/Settings/TargetSettings/GetAll")]
        public async Task<IActionResult> TargetSettingsGetAll()
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.GetTargetSettingsAsync();
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true, data = result.Data });
        }

        [HttpPost]
        [Route("Dashboard/Settings/TargetSettings/Upsert")]
        public async Task<IActionResult> TargetSettingsUpsert([FromBody] UpsertTargetRequest target)
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.UpsertTargetAsync(target);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true });
        }

        [HttpPost]
        [Route("Dashboard/Settings/TargetSettings/Delete/{id:int}")]
        public async Task<IActionResult> TargetSettingsDelete(int id)
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.DeleteTargetAsync(id);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true });
        }

        [HttpPost]
        [Route("Dashboard/Settings/TargetSettings/TestConnection")]
        public async Task<IActionResult> TargetSettingsTestConnection([FromBody] TestConnectionRequest req)
        {
            try
            {
                string connStr = req.DbType switch
                {
                    "PostgreSQL" =>
                        $"Host={req.Host};Port={req.Port};Database={req.DbName};Username={req.Username};Password={req.Password};Timeout=5;CommandTimeout=5",
                    "Oracle" =>
                        $"Data Source={req.Host}:{req.Port}/{req.DbName};User Id={req.Username};Password={req.Password};Connection Timeout=5",
                    _ => throw new Exception("Unsupported database type.")
                };

                if (req.DbType == "PostgreSQL")
                {
                    await using var conn = new Npgsql.NpgsqlConnection(connStr);
                    await conn.OpenAsync();
                }
                else if (req.DbType == "Oracle")
                {
                    await using var conn = new Oracle.ManagedDataAccess.Client.OracleConnection(connStr);
                    await conn.OpenAsync();
                }

                return Json(new { success = true, message = "Connection successful." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Connection failed: {ex.Message}" });
            }
        }



        // ── SCAN SETTINGS API ─────────────────────────────────────────────────────

        [HttpGet]
        [Route("Dashboard/Settings/ScanSettings/Get")]
        public async Task<IActionResult> ScanSettingsGet()
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.GetScanSettingsAsync();
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true, data = result.Data });
        }

        [HttpPost]
        [Route("Dashboard/Settings/ScanSettings/Update")]
        public async Task<IActionResult> ScanSettingsUpdate([FromBody] ScanSettings scan)
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.UpdateScanSettingsAsync(scan);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true });
        }



        // ══════════════════════════════════════════════════════════════════════
        // SOURCE SETTINGS API
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/Settings/SourceSettings/GetAdverseMedia")]
        public async Task<IActionResult> SourceSettingsGetAdverseMedia()
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.GetAdverseMediaFilterAsync();
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true, data = result.Data });
        }

        [HttpPost]
        [Route("Dashboard/Settings/SourceSettings/UpdateAdverseMedia")]
        public async Task<IActionResult> SourceSettingsUpdateAdverseMedia([FromBody] List<string> keywords)
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.UpdateAdverseMediaFilterAsync(keywords);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });
            return Json(new { success = true });
        }

        [HttpPost]
        [Route("Dashboard/Settings/SourceSettings/RefetchDatabase")]
        public async Task<IActionResult> SourceSettingsRefetch()
        {
            // TODO: plug in your actual refetch logic here
            // e.g. await _sanctionSyncService.SyncAsync();

            var downloader = new SanctionDownloader();
            await downloader.DownloadParseAndExportAsync(_settingsService);
            GlobalVariables.refetching_sanction_database = true;
            return Json(new { success = true, message = "Sanction database updated successfully." });
        }

        [HttpGet]
        [Route("Dashboard/Settings/SourceSettings/DownloadDatabase")]
        public async Task<IActionResult> SourceSettingsDownload()
        {
            var path = Path.Combine(GlobalVariables.root_folder, "SanctionDatabase", $"UPSanctionDB-{DateTime.UtcNow:dd-MM-yyyy}.xlsx");

            if (!System.IO.File.Exists(path))
                return NotFound(new { message = "Database file not found." });

            var bytes = await System.IO.File.ReadAllBytesAsync(path);

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Path.GetFileName(path));
        }


        // ══════════════════════════════════════════════════════════════════════
        // NIGERIAN SANCTION LIST API
        // Add these action methods inside DashboardController.cs
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/List/NigerianSanctionList/GetAll")]
        public IActionResult NigerianSanctionListGetAll()
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "NIGERIANSANCTIONLIST.json");
                var entries = NigerianSanctionListReader.LoadFromFile(filePath);
                return Json(new { success = true, data = entries });
            }
            catch (FileNotFoundException)
            {
                // Return empty list if file doesn't exist yet
                return Json(new { success = true, data = new List<SanctionEntry>() });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("Dashboard/List/NigerianSanctionList/Upsert")]
        public async Task<IActionResult> NigerianSanctionListUpsert([FromBody] SanctionEntryUpsertRequest req)
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "SanctionDatabase", "NigerianSanctionList.json");

                // Load existing entries (or start fresh)
                List<SanctionEntry> entries;
                try { entries = NigerianSanctionListReader.LoadFromFile(filePath); }
                catch { entries = new List<SanctionEntry>(); }

                if (req.IsEdit && req.OriginalId is not null)
                {
                    // Remove the old entry
                    entries = entries.Where(e => e.ID != req.OriginalId).ToList();
                }

                // Auto-generate an ID if none supplied
                var newId = string.IsNullOrWhiteSpace(req.Id)
                    ? $"NSL-{Guid.NewGuid().ToString("N")[..8].ToUpper()}"
                    : req.Id.Trim();

                // Ensure ID uniqueness for new entries
                if (!req.IsEdit && entries.Any(e => e.ID == newId))
                    return BadRequest(new { success = false, message = $"An entry with ID '{newId}' already exists." });

                var entry = new SanctionEntry
                {
                    ID = newId,
                    SubjectType = req.SubjectType ?? string.Empty,
                    Source = req.Source ?? string.Empty,
                    ReferenceNumber = req.ReferenceNumber ?? string.Empty,
                    DateDesignated = req.DateDesignated ?? string.Empty,
                    SanctionImposed = req.SanctionImposed ?? string.Empty,
                    Comments = req.Comments ?? string.Empty,
                    Names = req.Names ?? new(),
                    Addresses = req.Addresses ?? new(),
                    PhoneNumbers = req.PhoneNumbers ?? new(),
                    EmailAddresses = req.EmailAddresses ?? new(),
                    Positions = req.Positions ?? new(),
                    IdList = req.IdList ?? new(),
                    CallSign = req.CallSign,
                    VesselType = req.VesselType,
                    VesselFlag = req.VesselFlag,
                    VesselOwner = req.VesselOwner,
                    GrossRegisteredTonnage = req.GrossRegisteredTonnage
                };

                entries.Add(entry);

                // Persist back to JSON
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var json = System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await System.IO.File.WriteAllTextAsync(filePath, json);

                return Json(new { success = true, id = newId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("Dashboard/List/NigerianSanctionList/Delete/{id}")]
        public async Task<IActionResult> NigerianSanctionListDelete(string id)
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "SanctionDatabase", "NigerianSanctionList.json");
                var entries = NigerianSanctionListReader.LoadFromFile(filePath);
                var before = entries.Count;
                entries = entries.Where(e => e.ID != id).ToList();

                if (entries.Count == before)
                    return NotFound(new { success = false, message = "Entry not found." });

                var json = System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await System.IO.File.WriteAllTextAsync(filePath, json);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }



        //═══════════════════════════════════════════════════════════════════════════
        //CONTROLLER METHODS TO ADD TO DashboardController.cs
        //═══════════════════════════════════════════════════════════════════════════

        // Add these using statements at the top:
        // using System.Text.Json;
        // using System.Text.Json.Serialization;

        // Add these action methods inside DashboardController:

        [HttpGet]
        [Route("Dashboard/List/PEPs/GetAll")]
        public IActionResult PEPsGetAll()
        {
            try
            {
                List<PepEntry> entries = GlobalFunctions.FetchAllPeps();
                return Json(new { success = true, data = entries });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("Dashboard/List/PEPs/Upsert")]
        public async Task<IActionResult> PEPsUpsert([FromBody] PepUpsertRequest req)
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "peps.json");
                List<PepEntry> entries;

                if (System.IO.File.Exists(filePath))
                {
                    var json = await System.IO.File.ReadAllTextAsync(filePath);
                    var deserializeOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    entries = JsonSerializer.Deserialize<List<PepEntry>>(json, deserializeOptions) ?? new List<PepEntry>();
                }
                else
                {
                    entries = new List<PepEntry>();
                }

                if (req.Id.HasValue && req.Id.Value > 0)
                {
                    entries = entries.Where(e => e.Id != req.Id.Value).ToList();
                }

                int newId = req.Id ?? (entries.Any() ? entries.Max(e => e.Id) + 1 : 1);

                var entry = new PepEntry
                {
                    Id = newId,
                    FullName = req.Fullname ?? string.Empty,
                    MerchantName = req.MerchantName ?? string.Empty,
                    DOO = req.DOO,
                    TransactionMonitoringFrequency = req.TransactionMonitoringFrequency ?? "DAILY",
                    MonitoringCategory = req.MonitoringCategory ?? "HIGH RISK MERCHANT",
                    MerchantIds = req.MerchantIds ?? new List<string>(),
                    MerchantLocation = req.MerchantLocation
                };

                entries.Add(entry);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var outputJson = JsonSerializer.Serialize(entries, serializeOptions);
                await System.IO.File.WriteAllTextAsync(filePath, outputJson);

                return Json(new { success = true, id = newId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }



        [HttpPost]
   [Route("Dashboard/List/PEPs/Delete/{id:int}")]
   public async Task<IActionResult> PEPsDelete(int id)
   {
       try
       {
           var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "peps.json");
           if (!System.IO.File.Exists(filePath))
               return NotFound(new { success = false, message = "File not found." });

           var json = await System.IO.File.ReadAllTextAsync(filePath);
           var entries = JsonSerializer.Deserialize<List<PepEntry>>(json) ?? new List<PepEntry>();
           var before = entries.Count;
           entries = entries.Where(e => e.Id != id).ToList();

           if (entries.Count == before)
               return NotFound(new { success = false, message = "PEP not found." });

           var options = new JsonSerializerOptions { WriteIndented = true };
           var outputJson = JsonSerializer.Serialize(entries, options);
           await System.IO.File.WriteAllTextAsync(filePath, outputJson);

           return Json(new { success = true });
       }
       catch (Exception ex)
       {
           return BadRequest(new { success = false, message = ex.Message });
       }
   }

        // ═══════════════════════════════════════════════════════════════════════════
        // UBOS API
        // ═══════════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/List/UBOs/GetAll")]
        public IActionResult UBOsGetAll()
        {

            List<UboEntry> Ubos = GlobalFunctions.FetchAllUBOs();

            return Json(new { success = true, data = Ubos });

          
        }

        [HttpPost]
        [Route("Dashboard/List/UBOs/Upsert")]
        public async Task<IActionResult> UBOsUpsert([FromBody] UboUpsertRequest req)
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "UBOs.json");
                List<UboEntry> entries;

                if (System.IO.File.Exists(filePath))
                {
                    var json = await System.IO.File.ReadAllTextAsync(filePath);
                    var deserializeOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    entries = JsonSerializer.Deserialize<List<UboEntry>>(json, deserializeOptions) ?? new List<UboEntry>();
                }
                else
                {
                    entries = new List<UboEntry>();
                }

                if (req.Id.HasValue && req.Id.Value > 0)
                {
                    // Edit mode: remove existing
                    entries = entries.Where(e => e.Id != req.Id.Value).ToList();
                }

                // Generate new ID if needed
                int newId = req.Id ?? (entries.Any() ? entries.Max(e => e.Id) + 1 : 1);

                var entry = new UboEntry
                {
                    Id = newId,
                    FullName = req.FullName ?? string.Empty,
                    Dob = req.Dob,
                    Nationality = req.Nationality,
                    Gender = req.Gender,
                    Address = req.Address,
                    PercentageOfOwnership = req.PercentageOfOwnership,
                    NatureOfControl = req.NatureOfControl,
                    IdNumber = req.IdNumber,
                    Remark = req.Remark,
                    Company = req.Company,
                    MerchantIds = req.MerchantIds ?? new List<string>()
                };

                entries.Add(entry);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var outputJson = JsonSerializer.Serialize(entries, serializeOptions);
                await System.IO.File.WriteAllTextAsync(filePath, outputJson);

                return Json(new { success = true, id = newId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("Dashboard/List/UBOs/Delete/{id:int}")]
        public async Task<IActionResult> UBOsDelete(int id)
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "UBOs.json");
                if (!System.IO.File.Exists(filePath))
                    return NotFound(new { success = false, message = "File not found." });

                var json = await System.IO.File.ReadAllTextAsync(filePath);
                var deserializeOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var entries = JsonSerializer.Deserialize<List<UboEntry>>(json, deserializeOptions) ?? new List<UboEntry>();
                var before = entries.Count;
                entries = entries.Where(e => e.Id != id).ToList();

                if (entries.Count == before)
                    return NotFound(new { success = false, message = "UBO not found." });

                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var outputJson = JsonSerializer.Serialize(entries, serializeOptions);
                await System.IO.File.WriteAllTextAsync(filePath, outputJson);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }


        // ══════════════════════════════════════════════════════════════════════
        // SINGLE SCREEN API
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        [Route("Dashboard/SingleScreen")]
        public async Task<IActionResult> SingleScreen([FromBody] SingleScreenRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.SearchTerm))
                return BadRequest(new { success = false, message = "Search term is required." });

            var validFields = new[] { "name", "address", "email", "phone" };
            if (!validFields.Contains(req.SearchField?.ToLower()))
                return BadRequest(new { success = false, message = "Invalid search field." });

            // ── Sanction Search ──────────────────────────────────────────────────
            var filePath = Path.Combine(
                GlobalVariables.root_folder,
                "SanctionDatabase", "basesource",
                "UPSanctionDB.xlsx"
            );

            List<SanctionEntry> sanctionList = SanctionExcelReader.LoadFromExcel(filePath);

            // Use the appropriate BK-tree depending on requested search field
            var sanctionMatches = new List<(string EntryId, string Matched, double Similarity, int EditDistance)>();

            var field = (req.SearchField ?? "name").ToLowerInvariant();
            if (field == "name")
            {
                var sanction_candidates = _sanction_tree.Search(req.SearchTerm, req.Threshold);
                foreach (var candidate in sanction_candidates)
                    sanctionMatches.Add((candidate.EntryId, candidate.MatchedName, candidate.Similarity, candidate.EditDistance));
            }
            else if (field == "email")
            {
                var emailTree = new SanctionEmailsBKTree();
                emailTree.Load(sanctionList);
                var results = emailTree.Search(req.SearchTerm, req.Threshold);
                foreach (var r in results)
                    sanctionMatches.Add((r.EntryId, r.Matched, r.Similarity, r.EditDistance));
            }
            else if (field == "phone")
            {
                var phoneTree = new SanctionPhoneNumberBKTree();
                phoneTree.Load(sanctionList);
                var results = phoneTree.Search(req.SearchTerm, req.Threshold);
                foreach (var r in results)
                    sanctionMatches.Add((r.EntryId, r.Matched, r.Similarity, r.EditDistance));
            }
            else if (field == "address")
            {
                var addrTree = new SanctionAddressesBKTree();
                addrTree.Load(sanctionList);
                var results = addrTree.Search(req.SearchTerm, req.Threshold);
                foreach (var r in results)
                    sanctionMatches.Add((r.EntryId, r.Matched, r.Similarity, r.EditDistance));
            }

            List<SingleSearchSanctionMatchRow> completeSanctionList = new();
            foreach (var candidate in sanctionMatches)
            {
                SanctionEntry item = sanctionList.FirstOrDefault(x => x.ID == candidate.EntryId);
                if (item is null) continue;
                completeSanctionList.Add(new SingleSearchSanctionMatchRow
                {
                    similarity = (candidate.Similarity * 100).ToString("F2") + "%",
                    sanction_item = item
                });
            }

            // ── PEP Search ───────────────────────────────────────────────────────
            List<PepEntry> pepEntries = GlobalFunctions.FetchAllPeps();
            var pepTree = new PEPBKTree.PEPSanctionBKTree();
            pepTree.Load(pepEntries);

            List<PEPSearchResult> pepResults = pepTree.Search(req.SearchTerm, req.Threshold);
            List<PepSearchSanctionMatchRow> completePepList = new();
            foreach (PEPSearchResult candidate in pepResults)
            {
                PepEntry item = pepEntries.FirstOrDefault(x => x.Id == int.Parse(candidate.EntryId));
                if (item is null) continue;
                completePepList.Add(new PepSearchSanctionMatchRow
                {
                    similarity = (candidate.Similarity * 100).ToString("F2") + "%",
                    pep_item = item
                });
            }

            // ── Adverse Media ────────────────────────────────────────────────────
            List<RssNewsItem> adverseMediaResults = await GoogleNewsRssService.SearchAsync(req.SearchTerm);

            return Json(new
            {
                success = true,
                searchTerm = req.SearchTerm,
                searchField = req.SearchField,
                sanctions = completeSanctionList,
                peps = completePepList,
                adverseMedia = adverseMediaResults
            });
        }


        // ══════════════════════════════════════════════════════════════════════
        // REPLACE the existing MultiScanUpload action in DashboardController.cs
        // with this updated version.
        // ══════════════════════════════════════════════════════════════════════

        // ─────────────────────────────────────────────────────────────────────────────
        // Replace the existing MultiScanUpload action in DashboardController.cs
        //
        // WHY TWO ACTIONS?
        // ─────────────────
        // ASP.NET Core cannot bind [FromForm] and [FromBody] on the same action —
        // they use different body readers and the framework picks one.
        //
        // • Document uploads  must use multipart/form-data (FormData) because a
        //   real File object is in the payload.  →  [FromForm]
        //
        // • Paste-list uploads send a JSON body (matching how Index.cshtml calls
        //   SingleScreen) and have no file.        →  [FromBody]
        //
        // Both share the same route prefix; the `scanType` field in the body
        // disambiguates, but because the content-type differs we split them into
        // two actions on different sub-routes. The front-end already calls
        // Url.Action("MultiScanUpload", "Dashboard") for both — just update the
        // JS Url.Action values to the two routes below if you prefer named routes,
        // OR keep one route and let the JS decide which URL to hit.
        // ─────────────────────────────────────────────────────────────────────────────

        // ── REQUEST MODEL for the JSON (paste) path ───────────────────────────────────
        public class MultiScanPasteRequest
        {
            public string ScanType { get; set; } = "non-document";
            public string? NameList { get; set; }   // newline-delimited names
        }

        // ── Inside DashboardController ────────────────────────────────────────────────

        // ─── PATH A: non-document  (JSON body, no file) ───────────────────────────────
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Dashboard/MultiScan/Upload/Paste")]
        public async Task<IActionResult> MultiScanUploadPaste([FromBody] MultiScanPasteRequest req)
        {
            if (req.ScanType != "non-document")
                return BadRequest(new { success = false, message = "This endpoint only accepts non-document scans." });

            if (string.IsNullOrWhiteSpace(req.NameList))
                return BadRequest(new { success = false, message = "nameList is required." });

            var names = req.NameList
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            if (!names.Any())
                return BadRequest(new { success = false, message = "nameList contains no valid entries." });

            // Save to disk
            var uploadDir = Path.Combine(GlobalVariables.root_folder, "MultiScanUploads");
            Directory.CreateDirectory(uploadDir);

            var safeFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_paste.txt";
            var savedFilePath = Path.Combine(uploadDir, safeFileName);
            await System.IO.File.WriteAllLinesAsync(savedFilePath, names);

            return Json(new
            {
                success = true,
                scanType = "non-document",
                rowCount = names.Count,
                savedFilePath = savedFilePath,
                fileName = safeFileName
            });
        }

        // ─── PATH B: document  (multipart/form-data, has a file) ─────────────────────
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Dashboard/MultiScan/Upload/Document")]
        public async Task<IActionResult> MultiScanUploadDocument(
            IFormFile? file,
            [FromForm] string? scanColumn,
            [FromForm] string? idColumn,
            [FromForm] bool autoGenerateId)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file provided." });

            if (string.IsNullOrWhiteSpace(scanColumn))
                return BadRequest(new { success = false, message = "scanColumn is required." });

            if (!autoGenerateId && string.IsNullOrWhiteSpace(idColumn))
                return BadRequest(new { success = false, message = "idColumn is required when autoGenerateId is false." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".csv" && ext != ".xlsx" && ext != ".xls")
                return BadRequest(new { success = false, message = "Unsupported file type. Use .csv, .xlsx, or .xls." });

            // Save raw file to disk first (before parsing, so we keep the original)
            var uploadDir = Path.Combine(GlobalVariables.root_folder, "MultiScanUploads");
            Directory.CreateDirectory(uploadDir);

            var safeFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(file.FileName)}";
            var savedFilePath = Path.Combine(uploadDir, safeFileName);

            await using (var stream = new FileStream(savedFilePath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(stream);
            }

            // Parse to get row count
            DataTable data_to_scan = new DataTable();

            if (ext == ".csv")
            {
                var csvReader = new CsvFileReader();
                var csvResult = csvReader.ReadCsvFile(file, idColumn!, scanColumn);
                if (!csvResult.Success)
                    return BadRequest(new { success = false, message = csvResult.Error });
                data_to_scan = csvResult.Data!;
            }
            else
            {
                var excelReader = new ExcelMultiSheetReader();
                var excelResult = excelReader.ReadExcelFile(file, idColumn!, scanColumn);
                if (!excelResult.Success)
                    return BadRequest(new { success = false, message = excelResult.Error });
                data_to_scan = excelResult.Data!;
            }

            return Json(new
            {
                success = true,
                scanType = "document",
                rowCount = data_to_scan.Rows.Count,
                savedFilePath = savedFilePath,
                fileName = safeFileName
            });
        }




    }


}