using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quartz.Logging;
using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Upsanctionscreener.Classess;
using Upsanctionscreener.Classess.Search;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;
using Upsanctionscreener.Models.ViewModels;
using Upsanctionscreener.Services;

using static Upsanctionscreener.Classess.Search.PEPBKTree;

namespace Upsanctionscreener.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
       
        private readonly UpSanctionSettingsService _settingsService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SanctionDownloader _downloader;
        private readonly TargetSchedulerService _targetScheduler;
        public DashboardController(AppDbContext db, IConfiguration config,  UpSanctionSettingsService settingsService, IServiceScopeFactory scopeFactory, SanctionDownloader downloader, TargetSchedulerService targetScheduler)  
        {
            _db = db;
            _config = config;
        
            _settingsService = settingsService;
            _scopeFactory = scopeFactory;
            _downloader = downloader;
            _targetScheduler = targetScheduler;
        }

        // ══════════════════════════════════════════════════════════════════════
        // VIEWS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<IActionResult> Index()
        {
            var svc = new UpSanctionSettingsService(_db);
            SettingsResult<ScanSettings> result = await svc.GetScanSettingsAsync();

            int default_threshold = result.Success && result.Data is not null
                ? result.Data.ScanThreshold
                : 90;

            ViewData["DefaultThreshold"] = default_threshold;

            return View("~/Views/Dashboard/Index.cshtml");
        }

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

        [Route("Dashboard/Reports/TargetScanReports")]
        public IActionResult TargetScanReports() =>
        View("~/Views/Dashboard/Reports/TargetScanReports.cshtml");

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

        [Route("Dashboard/MultiScan/Tasks")]
        public IActionResult MultiScanTasks() =>
           View("~/Views/Dashboard/MultiScanScreen.cshtml");

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
            {
return BadRequest(new { message = error });
            }
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - CREATED USER WITH EMAIL - {req.Email}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(result);
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/Delete/{id:int}")]
        public async Task<IActionResult> UsersDelete(int id)
        {
            var error = await GlobalFunctions.DeleteUserAsync(_db, id);

            if (error == "NOT_FOUND")
                return NotFound(new { message = "User not found." });


            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - DELETED USER WITH USER ID: ${id}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );
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


            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - UPDATED USER WITH USER ID: {id} - STATUS TO {req.ProfileStatus}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(result);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TARGET SETTINGS API
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// GET all target configurations.
        /// </summary>
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
            // ── Guard: target_type ────────────────────────────────────────────────
            if (target.TargetType != "database" && target.TargetType != "document")
                return BadRequest(new { success = false, message = "target_type must be 'database' or 'document'." });

            // ── Guard: document upload path ───────────────────────────────────────
            if (target.TargetType == "document" && target.DocumentSettings is not null)
            {
                var uploadRoot = Path.GetFullPath(
                    Path.Combine(GlobalVariables.root_folder, "Targets", "TargetUploads"));

                if (!string.IsNullOrWhiteSpace(target.DocumentSettings.UploadPath))
                {
                    var resolvedPath = Path.GetFullPath(target.DocumentSettings.UploadPath);
                    if (!resolvedPath.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase))
                        return BadRequest(new { success = false, message = "Invalid document upload path." });
                }
            }

            var svc = new UpSanctionSettingsService(_db);

            // ── Resolve the ID that the service will assign ───────────────────────
            // UpsertTargetAsync returns bool, not the saved entity, so we compute
            // the new ID the same way the service does — before the save.
            int resolvedId = target.Id;
            if (target.Id == 0)
            {
                var existing = await svc.GetTargetSettingsAsync();
                if (existing.Success && existing.Data is not null)
                    resolvedId = existing.Data.Count > 0
                        ? existing.Data.Max(t => t.Id) + 1
                        : 1;
            }

            // ── Save to database ──────────────────────────────────────────────────
            var result = await svc.UpsertTargetAsync(target);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });

            // ── Schedule / reschedule the Quartz job ──────────────────────────────
            if (target.AutomationSettings is not null)
            {
                var automation = new AutomationSettings
                {
                    Automate = target.AutomationSettings.Automate,
                    Frequency = target.AutomationSettings.Frequency,
                    StartTime = target.AutomationSettings.StartTime,
                    Weekday = target.AutomationSettings.Weekday,
                    DayOfMonth = target.AutomationSettings.DayOfMonth,
                    IntervalMinutes = target.AutomationSettings.IntervalMinutes,
                    IntervalHours = target.AutomationSettings.IntervalHours
                };

                await _targetScheduler.ScheduleOrUpdateTargetAsync(
                    resolvedId,
                    target.TargetName,
                    target.TargetType,
                    target.AutomationSettings.Frequency,
                    automation);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - UPSERTED TARGET SETTINGS {target.TargetName}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(new { success = true });
        }







        [HttpPost]
        [Route("Dashboard/Settings/TargetSettings/Delete/{id:int}")]
        public async Task<IActionResult> TargetSettingsDelete(int id)
        {
            var svc = new UpSanctionSettingsService(_db);

            // ── Clean up uploaded document file if applicable ─────────────────────
            var getAllResult = await svc.GetTargetSettingsAsync();
            if (getAllResult.Success && getAllResult.Data is not null)
            {
                var target = getAllResult.Data.FirstOrDefault(t => t.Id == id);
                if (target?.TargetType == "document")
                {
                    var uploadPath = target.DocumentSettings?.UploadPath;
                    if (!string.IsNullOrWhiteSpace(uploadPath) && System.IO.File.Exists(uploadPath))
                    {
                        var uploadRoot = Path.GetFullPath(
                            Path.Combine(GlobalVariables.root_folder, "Targets", "TargetUploads"));
                        var resolvedPath = Path.GetFullPath(uploadPath);

                        if (resolvedPath.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            try { System.IO.File.Delete(uploadPath); }
                            catch (Exception ex)
                            {
                                // Non-fatal — log and continue
                             Console.WriteLine($"{ex}- \\n[TargetDelete] Could not delete uploaded file for target .");
                            }
                        }
                    }
                }
            }

            // ── Delete from database ──────────────────────────────────────────────
            var result = await svc.DeleteTargetAsync(id);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });

            // ── Remove the Quartz job ─────────────────────────────────────────────
            await _targetScheduler.RemoveTargetScheduleAsync(id);

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - DELETED TARGET {id}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(new { success = true });
        }





















        /// <summary>
        /// POST test a database connection (database targets only).
        /// Supports PostgreSQL and Oracle.
        /// Returns { success, message }.
        /// </summary>
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

        /// <summary>
        /// POST upload a document (Excel) for a document-type target.
        /// Accepts .xlsx and .xls files only.
        /// Saves the file to: {RootFolder}/Targets/TargetUploads/
        /// Returns { success, fileName, fileExtension, uploadPath }.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 104857600)] // 100 MB
        [Route("Dashboard/Settings/TargetSettings/UploadDocument")]
        public async Task<IActionResult> TargetSettingsUploadDocument(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file provided." });

            var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
            if (ext != "xlsx" && ext != "xls")
                return BadRequest(new { success = false, message = "Only .xlsx and .xls files are supported." });

            try
            {
                var uploadDir = Path.Combine(GlobalVariables.root_folder, "Targets", "TargetUploads");
                Directory.CreateDirectory(uploadDir);

                // Build a safe, unique file name: timestamp_originalname.ext
                var originalNameWithoutExt = Path.GetFileNameWithoutExtension(file.FileName);
                var safeOriginalName = string.Concat(
                    originalNameWithoutExt
                        .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                        .Take(60));

                var safeFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safeOriginalName}.{ext}";
                var savePath = Path.Combine(uploadDir, safeFileName);

                await using (var stream = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                {
                    await file.CopyToAsync(stream);
                }

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - UPLOADED DOCUMENT {safeFileName}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );
                return Json(new
                {
                    success = true,
                    fileName = safeOriginalName,   // human-readable name (no ext, no timestamp)
                    fileExtension = ext,
                    uploadPath = savePath,
                    savedFileName = safeFileName        // full file name on disk
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Upload failed: {ex.Message}" });
            }
        }



        // ══════════════════════════════════════════════════════════════════════
        // TARGET SCAN REPORTS — controller methods for DashboardController.cs
        // ══════════════════════════════════════════════════════════════════════

        // ── Private helper ────────────────────────────────────────────────────────────
        // Matches a filename against the list of known target names by checking whether
        // the filename's base-name STARTS WITH a target name (case-insensitive),
        // followed immediately by '_', '-', ' ', or end-of-string.
        //
        // Targets are sorted longest-name-first so "UBO List Extended" is tested before
        // "UBO List", preventing partial matches.
        //
        // Examples
        //   "MerchantScan_2026-04-30_11-58.xlsx"  → ("MerchantScan", 1)
        //   "UBO List_2026-04-30_11-58.xlsx"       → ("UBO List",     2)
        //   "unknown_file.xlsx"                    → (null, null)
        private static (string? targetName, string? targetId) MatchTargetByFileName(
            string fileName,
            List<TargetSetting> targets)
        {
            var bare = Path.GetFileNameWithoutExtension(fileName); // strips .xlsx etc.

            foreach (var t in targets.OrderByDescending(x => x.TargetName.Length))
            {
                if (string.IsNullOrWhiteSpace(t.TargetName)) continue;

                if (!bare.StartsWith(t.TargetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Require a word-boundary character right after the target name,
                // or the filename is exactly the target name (no suffix).
                int afterIdx = t.TargetName.Length;
                if (afterIdx == bare.Length
                    || bare[afterIdx] == '_'
                    || bare[afterIdx] == '-'
                    || bare[afterIdx] == ' ')
                {
                    return (t.TargetName, t.Id.ToString());
                }
            }

            return (null, null);
        }

        // ── GET /Dashboard/Reports/TargetScanReports/GetAll ───────────────────────────
        // Returns every .xlsx / .xls / .csv in Targets/TargetReports with the
        // resolved target name attached.
        // Response shape: { reports: [ { fileName, targetName, targetId, fileSizeKb, createdAt } ] }
        [HttpGet]
        [Route("Dashboard/Reports/TargetScanReports/GetAll")]
        public async Task<IActionResult> TargetScanReportsGetAll()
        {
            try
            {
                var reportsDir = Path.Combine(GlobalVariables.root_folder, "Targets", "TargetReports");

                if (!Directory.Exists(reportsDir))
                    return Json(new { reports = Array.Empty<object>() });

                // Fetch targets from DB — TargetName is mapped from JSON "target_name"
                var svc = new UpSanctionSettingsService(_db);
                var targetsResult = await svc.GetTargetSettingsAsync();
                var targets = (targetsResult.Success && targetsResult.Data is not null)
                    ? targetsResult.Data
                    : new List<TargetSetting>();

                var files = Directory
                    .EnumerateFiles(reportsDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext == ".xlsx" || ext == ".xls" || ext == ".csv";
                    })
                    .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                    .Select(f =>
                    {
                        var info = new FileInfo(f);
                        var (targetName, targetId) = MatchTargetByFileName(info.Name, targets);

                        return new
                        {
                            fileName = info.Name,
                            targetName,
                            targetId,
                            fileSizeKb = (int)Math.Ceiling(info.Length / 1024.0),
                            createdAt = info.LastWriteTimeUtc
                        };
                    })
                    .ToList();

                return Json(new { reports = files });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // ── GET /Dashboard/Reports/TargetScanReports/GetLogs ─────────────────────────
        // Returns every .log file in Logs/TargetScanLogs with the resolved target name.
        // Response shape: { logs: [ { fileName, targetName, targetId, fileSizeKb, createdAt } ] }
        [HttpGet]
        [Route("Dashboard/Reports/TargetScanReports/GetLogs")]
        public async Task<IActionResult> TargetScanReportsGetLogs()
        {
            try
            {
                var logsDir = Path.Combine(GlobalVariables.root_folder, "Logs", "TargetScanLogs");

                if (!Directory.Exists(logsDir))
                    return Json(new { logs = Array.Empty<object>() });

                var svc = new UpSanctionSettingsService(_db);
                var targetsResult = await svc.GetTargetSettingsAsync();
                var targets = (targetsResult.Success && targetsResult.Data is not null)
                    ? targetsResult.Data
                    : new List<TargetSetting>();

                // Search for *.log AND *.txt so nothing is missed
                var logFiles = Directory
                    .EnumerateFiles(logsDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext == ".log" || ext == ".logs";
                    })
                    .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                    .Select(f =>
                    {
                        var info = new FileInfo(f);
                        var (targetName, targetId) = MatchTargetByFileName(info.Name, targets);

                        return new
                        {
                            fileName = info.Name,
                            targetName,
                            targetId,
                            fileSizeKb = (int)Math.Ceiling(info.Length / 1024.0),
                            createdAt = info.LastWriteTimeUtc
                        };
                    })
                    .ToList();

                return Json(new { logs = logFiles });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // ── GET /Dashboard/Reports/TargetScanReports/Download?fileName=... ────────────
        // Streams a file from Targets/TargetReports as a browser download.
        // Only a plain filename is accepted — no path separators allowed.
        [HttpGet]
        [Route("Dashboard/Reports/TargetScanReports/Download")]
        public IActionResult TargetScanReportsDownload([FromQuery] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest(new { success = false, message = "fileName is required." });

            // Reject anything that looks like a path traversal attempt
            if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0 || fileName.Contains(".."))
                return BadRequest(new { success = false, message = "Invalid file name." });

            var reportsDir = Path.GetFullPath(Path.Combine(GlobalVariables.root_folder, "Targets", "TargetReports"));
            var resolvedPath = Path.GetFullPath(Path.Combine(reportsDir, fileName));

            // Belt-and-suspenders path traversal guard
            if (!resolvedPath.StartsWith(reportsDir, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Invalid file path." });

            if (!System.IO.File.Exists(resolvedPath))
                return NotFound(new { success = false, message = $"File not found: {fileName}" });

            var contentType = Path.GetExtension(resolvedPath).ToLowerInvariant() switch
            {
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                ".csv" => "text/csv",
                _ => "application/octet-stream"
            };

            // PhysicalFile streams directly from disk — no memory allocation for the whole file
            return PhysicalFile(resolvedPath, contentType, fileName);
        }

        // ── GET /Dashboard/Reports/TargetScanReports/ReadLog?fileName=... ─────────────
        // Returns the full text of a .log or .txt file from Logs/TargetScanLogs.
        // Response: { content: "..." }
        [HttpGet]
        [Route("Dashboard/Reports/TargetScanReports/ReadLog")]
        public async Task<IActionResult> TargetScanReportsReadLog([FromQuery] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest(new { success = false, message = "fileName is required." });

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext != ".log" && ext != ".logs")
                return BadRequest(new { success = false, message = "Only .log and .logs files may be read." });

            if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0 || fileName.Contains(".."))
                return BadRequest(new { success = false, message = "Invalid file name." });

            var logsDir = Path.GetFullPath(Path.Combine(GlobalVariables.root_folder, "Logs", "TargetScanLogs"));
            var resolvedPath = Path.GetFullPath(Path.Combine(logsDir, fileName));

            if (!resolvedPath.StartsWith(logsDir, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Invalid file path." });

            if (!System.IO.File.Exists(resolvedPath))
                return NotFound(new { success = false, message = $"Log file not found: {fileName}" });

            var content = await System.IO.File.ReadAllTextAsync(resolvedPath);
            return Json(new { content });
        }



























        // ── SCAN SETTINGS API ──────────────────────────────────────────────────

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

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - UPDATED SCAN SETTINGS ",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );
            return Json(new { success = true });
        }



        // ── API KEYS — Create ──────────────────────────────────────────────────────
        // POST /Dashboard/Settings/ScanSettings/ApiKeys/Create
        // Body: { "client_id": "my_app" }
        // Returns: { success, settings: <full ScanSettings> }
        [HttpPost]
        [Route("Dashboard/Settings/ScanSettings/ApiKeys/Create")]
        public async Task<IActionResult> ScanSettingsApiKeysCreate([FromBody] CreateApiKeyRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.ClientId))
                return BadRequest(new { success = false, message = "client_id is required." });

            if (!System.Text.RegularExpressions.Regex.IsMatch(req.ClientId, @"^[a-zA-Z0-9_-]+$"))
                return BadRequest(new { success = false, message = "client_id may only contain letters, numbers, hyphens and underscores." });

            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.CreateApiKeyAsync(req.ClientId);

            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - CREATED API KEY FOR CLIENT: {req.ClientId}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(new { success = true, settings = result.Data });
        }

        // ── API KEYS — Delete ──────────────────────────────────────────────────────
        // POST /Dashboard/Settings/ScanSettings/ApiKeys/Delete/{id}
        // Returns: { success, settings: <full ScanSettings> }
        [HttpPost]
        [Route("Dashboard/Settings/ScanSettings/ApiKeys/Delete/{id:int}")]
        public async Task<IActionResult> ScanSettingsApiKeysDelete(int id)
        {
            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.DeleteApiKeyAsync(id);

            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - DELETED API KEY ID: {id}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(new { success = true, settings = result.Data });
        }

        // ── API KEYS — Update Status ───────────────────────────────────────────────
        // POST /Dashboard/Settings/ScanSettings/ApiKeys/UpdateStatus/{id}
        // Body: { "status": "active" | "inactive" }
        // Returns: { success, settings: <full ScanSettings> }
        [HttpPost]
        [Route("Dashboard/Settings/ScanSettings/ApiKeys/UpdateStatus/{id:int}")]
        public async Task<IActionResult> ScanSettingsApiKeysUpdateStatus(int id, [FromBody] UpdateApiKeyStatusRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Status))
                return BadRequest(new { success = false, message = "status is required." });

            var svc = new UpSanctionSettingsService(_db);
            var result = await svc.UpdateApiKeyStatusAsync(id, req.Status);

            if (!result.Success)
                return BadRequest(new { success = false, message = result.Error });

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - UPDATED API KEY ID: {id} STATUS TO: {req.Status}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(new { success = true, settings = result.Data });
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

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who updated the adverse media filter
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - UPDATED ADVERSE MEDIA FILTER",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );
            return Json(new { success = true });
        }

        [HttpPost]
        [Route("Dashboard/Settings/SourceSettings/RefetchDatabase")]
        public async Task<IActionResult> SourceSettingsRefetch()
        {
           
            await _downloader.DownloadParseAndExportAsync(_settingsService);
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

                List<SanctionEntry> entries;
                try { entries = NigerianSanctionListReader.LoadFromFile(filePath); }
                catch { entries = new List<SanctionEntry>(); }

                if (req.IsEdit && req.OriginalId is not null)
                    entries = entries.Where(e => e.ID != req.OriginalId).ToList();

                var newId = string.IsNullOrWhiteSpace(req.Id)
                    ? $"NSL-{Guid.NewGuid().ToString("N")[..8].ToUpper()}"
                    : req.Id.Trim();

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

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var json = System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await System.IO.File.WriteAllTextAsync(filePath, json);

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - UPDATED NIGERIAN SANCTION LIST",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );

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
                var entry = entries.Where(e => e.ID == id).ToList();
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
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - DELETED NIGERIAN SANCTION LIST ENTRY {id} - {System.Text.Json.JsonSerializer.Serialize(entry)}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PEPs API
        // ══════════════════════════════════════════════════════════════════════

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
                    var deserializeOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    entries = JsonSerializer.Deserialize<List<PepEntry>>(json, deserializeOpts) ?? new();
                }
                else
                {
                    entries = new List<PepEntry>();
                }

                if (req.Id.HasValue && req.Id.Value > 0)
                    entries = entries.Where(e => e.Id != req.Id.Value).ToList();

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
                var serializeOpts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(entries, serializeOpts));
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - UPSERTED PEP ENTRY {newId} - {System.Text.Json.JsonSerializer.Serialize(entry)}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );

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
                var entries = JsonSerializer.Deserialize<List<PepEntry>>(json) ?? new();
                var before = entries.Count;
                var entry = entries.Where(e => e.Id == id).ToList();
                entries = entries.Where(e => e.Id != id).ToList();
              

                if (entries.Count == before)
                    return NotFound(new { success = false, message = "PEP not found." });

                await System.IO.File.WriteAllTextAsync(filePath,
                    JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - DELETED PEP ENTRY {id} -{System.Text.Json.JsonSerializer.Serialize(entry)} ",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // UBOs API
        // ══════════════════════════════════════════════════════════════════════

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
                    var deserializeOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    entries = JsonSerializer.Deserialize<List<UboEntry>>(json, deserializeOpts) ?? new();
                }
                else
                {
                    entries = new List<UboEntry>();
                }

                if (req.Id.HasValue && req.Id.Value > 0)
                    entries = entries.Where(e => e.Id != req.Id.Value).ToList();

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
                var serializeOpts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(entries, serializeOpts));

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - UPSERTED UBO ENTRY - {System.Text.Json.JsonSerializer.Serialize(entry)}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );

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
                var deserializeOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var entries = JsonSerializer.Deserialize<List<UboEntry>>(json, deserializeOpts) ?? new();
                var entry = entries.Where(e => e.Id == id).ToList();
                var before = entries.Count;
                entries = entries.Where(e => e.Id != id).ToList();

                if (entries.Count == before)
                    return NotFound(new { success = false, message = "UBO not found." });

                var serializeOpts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(entries, serializeOpts));

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - DELETED UBO WITH ID {id} - {System.Text.Json.JsonSerializer.Serialize(entry)}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );

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

            var filePath = Path.Combine(GlobalVariables.root_folder, "SanctionDatabase", "basesource", "UPSanctionDB.xlsx");
            var sanctionList = SanctionExcelReader.LoadFromExcel(filePath);
            var sanctionMatches = new List<(string EntryId, string Matched, double Similarity, int EditDistance)>();

            var field = (req.SearchField ?? "name").ToLowerInvariant();

            var result = await Scanner.SingleScanScreener(req.Threshold??0.9,req.SearchTerm, req.SearchField, _scopeFactory);

            dynamic response = result;
            sanctionMatches = response.data;
            var completeSanctionList = sanctionMatches
                .Select(c => new { entry = sanctionList.FirstOrDefault(x => x.ID == c.EntryId), c.Similarity })
                .Where(x => x.entry is not null)
                .Select(x => new SingleSearchSanctionMatchRow
                {
                    similarity = (x.Similarity * 100).ToString("F2") + "%",
                    sanction_item = x.entry!
                }).ToList();

            var pepEntries = GlobalFunctions.FetchAllPeps();
            var pepTree = new PEPBKTree.PEPSanctionBKTree();
            pepTree.Load(pepEntries);

            var completePepList = pepTree.Search(req.SearchTerm, req.Threshold)
                .Select(c => new { entry = pepEntries.FirstOrDefault(x => x.Id == int.Parse(c.EntryId)), c.Similarity })
                .Where(x => x.entry is not null)
                .Select(x => new PepSearchSanctionMatchRow
                {
                    similarity = (x.Similarity * 100).ToString("F2") + "%",
                    pep_item = x.entry!
                }).ToList();

            var adverseMediaResults = await GoogleNewsRssService.SearchAsync(req.SearchTerm);
            var adverseMediaSettings = await _settingsService.GetAdverseMediaFilterAsync();
            List<string> adverseMediaFilters = adverseMediaSettings.Data;

            var filteredAdverseMedia = adverseMediaResults.Where(item =>
                !adverseMediaFilters.Any(filter =>
                    item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToList();




            return Json(new
            {
                success = true,
                searchTerm = req.SearchTerm,
                searchField = req.SearchField,
                sanctions = completeSanctionList,
                peps = completePepList,
                adverseMedia = filteredAdverseMedia
            });
        }








        // ══════════════════════════════════════════════════════════════════════
        // MULTI-SCAN API
        // ══════════════════════════════════════════════════════════════════════
       

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

            var uploadDir = Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanUploads");
            Directory.CreateDirectory(uploadDir);

            var safeFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_paste.txt";
            var savedFilePath = Path.Combine(uploadDir, safeFileName);
            await System.IO.File.WriteAllLinesAsync(savedFilePath, names);

            return Json(new
            {
                success = true,
                scanType = "non-document",
                rowCount = names.Count,
                savedFilePath,
                fileName = safeFileName
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
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

            string? savedFilePath = null;
            string? safeFileName = null;

            if (data_to_scan.Rows.Count > 0)
            {
                if (ext != ".csv" && ext != ".xlsx" && ext != ".xls")
                    return BadRequest(new { success = false, message = "Unsupported file type." });

                var uploadDir = Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanUploads");
                Directory.CreateDirectory(uploadDir);

                safeFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(file.FileName)}";
                savedFilePath = Path.Combine(uploadDir, safeFileName);

                await using var stream = new FileStream(savedFilePath, FileMode.Create, FileAccess.Write);
                await file.CopyToAsync(stream);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = User.FindFirstValue(ClaimTypes.Email);

            // e.g. log who fetched the audit logs
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{email} - UPLOADED FILE {file.FileName} to {savedFilePath}",
                userId: userId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return Json(new
            {
                success = true,
                scanType = "document",
                rowCount = data_to_scan.Rows.Count,
                savedFilePath,
                fileName = safeFileName
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Dashboard/MultiScan/DeleteUpload")]
        public IActionResult MultiScanDeleteUpload([FromBody] MultiScanDeleteRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.FilePath))
                return BadRequest(new { success = false, message = "filePath is required." });

            var uploadDir = Path.GetFullPath(Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanUploads"));
            var targetPath = Path.GetFullPath(req.FilePath);

            if (!targetPath.StartsWith(uploadDir, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Invalid file path." });

            if (!System.IO.File.Exists(targetPath))
                return NotFound(new { success = false, message = "File not found on server." });

            try
            {
                System.IO.File.Delete(targetPath);

              

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Could not delete file: {ex.Message}" });
            }
        }

        [HttpGet]
        [Route("Dashboard/MultiScan/Screen")]
        public IActionResult MultiScanScreen(
            string filePath,
            string? fileName,
            int rowCount,
            string scanType,
            string? scanColumn,
            string? idColumn,
            bool autoGenerateId)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return RedirectToAction(nameof(MultiScan));

            var uploadDir = Path.GetFullPath(Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanUploads"));
            var targetPath = Path.GetFullPath(filePath);

            if (!targetPath.StartsWith(uploadDir, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(MultiScan));

            var newTask = new MultiScanTask
            {
                FileName = fileName ?? Path.GetFileName(filePath),
                FilePath = filePath,
                ScanType = scanType,
                RowCount = rowCount.ToString(),
                AutoGenerateId = autoGenerateId.ToString(),
                IdColumn = idColumn ?? string.Empty,
                ScanColumn = scanColumn ?? string.Empty,
                Status = "Pending",
                StartTime = DateTime.UtcNow.ToString("o"),
                CompletionTIme = string.Empty,
                ErrorMessage = string.Empty
            };

            int newId = Scanner.AddMultiScanTask(newTask);
            Task.Run(() => Scanner.MultiScanScreener(newTask, _scopeFactory));

            ViewData["TaskId"] = newId;
            ViewData["FilePath"] = filePath;
            ViewData["FileName"] = newTask.FileName;
            ViewData["RowCount"] = rowCount;
            ViewData["ScanType"] = scanType;
            ViewData["ScanColumn"] = scanColumn;
            ViewData["IdColumn"] = idColumn;
            ViewData["AutoGenerateId"] = autoGenerateId;

            return View("~/Views/Dashboard/MultiScanScreen.cshtml");
        }

        [HttpGet]
        [Route("Dashboard/MultiScan/GetTasks")]
        public IActionResult MultiScanGetTasks()
        {
            try
            {
                var tasks = Scanner.GetAllTasks();
                return Json(new { success = true, tasks });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("Dashboard/MultiScan/DownloadResult/{id:int}")]
        public IActionResult MultiScanDownloadResult(int id)
        {
            var tasks = Scanner.GetAllTasks();
            var task = tasks.FirstOrDefault(t => t.Id == id);

            if (task is null)
                return NotFound(new { success = false, message = "Task not found." });

            if (!task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Task has not completed successfully." });

            if (string.IsNullOrWhiteSpace(task.ResultPath) || !System.IO.File.Exists(task.ResultPath))
                return NotFound(new { success = false, message = "Result file not found." });

            var bytes = System.IO.File.ReadAllBytes(task.ResultPath);
            var fileName = task.ResultFileName; // already includes .xlsx

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Dashboard/MultiScan/CancelTask/{id:int}")]
        public async Task<IActionResult> MultiScanCancelTask(int id)
        {
            try
            {
                var tasks = Scanner.GetAllTasks();
                var task = tasks.FirstOrDefault(t => t.Id == id);

                if (task is null)
                    return NotFound(new { success = false, message = "Task not found." });

                if (!task.Status.Equals("Scanning", StringComparison.OrdinalIgnoreCase) &&
                    !task.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { success = false, message = "Only Pending or Scanning tasks can be cancelled." });

                Scanner.UpdateMultiScanTask(id, "Cancelled", "Cancelled by user.");
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - CANCELED MULTISCAN TASK {System.Text.Json.JsonSerializer.Serialize(task)}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Dashboard/MultiScan/DeleteTask/{id:int}")]
        public async Task<IActionResult> MultiScanDeleteTask(int id)
        {
            try
            {
                var tasks = Scanner.GetAllTasks();
                var task = tasks.FirstOrDefault(t => t.Id == id);

                if (task is null)
                    return NotFound(new { success = false, message = "Task not found." });

                // Only allow deletion of terminal states
                var terminalStatuses = new[] { "Completed", "Failed", "Cancelled" };
                if (!terminalStatuses.Contains(task.Status, StringComparer.OrdinalIgnoreCase))
                    return BadRequest(new { success = false, message = "Only completed, failed, or cancelled tasks can be deleted." });

                // Delete the upload file if it exists and is in the safe upload dir
                if (!string.IsNullOrWhiteSpace(task.FilePath))
                {
                    var uploadDir = Path.GetFullPath(Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanUploads"));
                    var targetPath = Path.GetFullPath(task.FilePath);
                    if (targetPath.StartsWith(uploadDir, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(targetPath))
                        System.IO.File.Delete(targetPath);
                }

                // Delete the result file if it exists
                if (!string.IsNullOrWhiteSpace(task.ResultPath) && System.IO.File.Exists(task.ResultPath))
                {
                    var resultDir = Path.GetFullPath(Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanResult"));
                    var resultPath = Path.GetFullPath(task.ResultPath);
                    if (resultPath.StartsWith(resultDir, StringComparison.OrdinalIgnoreCase))
                        System.IO.File.Delete(resultPath);
                }

                // Remove from tasks list
                lock (Scanner._multiscantaskFileLock)
                {
                    // Re-use internal method via a public helper — or inline the logic:
                }
                Scanner.DeleteMultiScanTask(id);

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var email = User.FindFirstValue(ClaimTypes.Email);

                // e.g. log who fetched the audit logs
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - DELETED MULTISCAN TASK {System.Text.Json.JsonSerializer.Serialize(task)}",
                    userId: userId,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }


    }

  


}