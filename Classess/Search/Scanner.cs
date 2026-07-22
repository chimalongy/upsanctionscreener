using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Office2021.DocumentTasks;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Upsanctionscreener.Classess.Search.ScanExporters;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;
using static Upsanctionscreener.Classess.Search.SanctionNamesBKTree;


namespace Upsanctionscreener.Classess.Search
{



    public class TargetScanResult
    {
        public string RowId { get; set; } = "";

        // Values pulled from the mapped columns (for quick display/export)
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }
        public string? DateOfBirth { get; set; }

        public string MatchedColumn { get; set; } = string.Empty; // which name column produced the winning hit

        public double NameSimilarity { get; set; }
        public double AddressSimilarity { get; set; }
        public double EmailSimilarity { get; set; }
        public double PhoneSimilarity { get; set; }
        public double GenderSimilarity { get; set; }
        public double DobSimilarity { get; set; }
        public double AverageSimilarity { get; set; }

        // Total number of raw BK-tree name hits considered for this row
        // (across all mapped name columns) before average-similarity filtering.
        public int HitsCount { get; set; }

        public SanctionEntry? MatchedSanctionEntry { get; set; }

        // Every column from data_to_scan for this row, in original column order —
        // lets the exporter dump the full source record regardless of what's mapped.
        public List<KeyValuePair<string, string>> RawRowData { get; set; } = new();
    }




    public class TargetScanTimeTracker
    {
        public int Id { get; set; }

        public int TargetId { get; set; }

        public string TargetName { get; set; } = string.Empty;

        public string StartTime { get; set; } = string.Empty;

        public string StopTime { get; set; } = string.Empty;
    }

    public class TargetStreamResult
    {
        public int TotalScansToday { get; set; }
        public int TotalMatchedToday { get; set; }
        public string LastUpdated { get; set; } = string.Empty;
    }


    public static class Scanner
    {
        internal static readonly object _multiscantaskFileLock = new object();
        internal static readonly object _targetScanTimeTrackerLock = new object();
        internal static readonly object _targetStreamFileLock = new object();

        private static string TasksFilePath =>
          System.IO.Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanDB", "multiscantasks.json");

        private static string TargetScanTimeTrackerFilePath =>
        System.IO.Path.Combine(
            GlobalVariables.root_folder,
            "Targets",
            "TargetTracker",
            "target_scan_time_tracker.json");


        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };




        // ── Private helpers (always called inside lock) ───────────────────────

        private static List<MultiScanTask> ReadTasks()
        {
            var path = TasksFilePath;
            if (!File.Exists(path))
                return new List<MultiScanTask>();

            try
            {
                // FileShare.Read allows other readers but blocks writers
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<List<MultiScanTask>>(json, _jsonOptions)
                       ?? new List<MultiScanTask>();
            }
            catch
            {
                return new List<MultiScanTask>();
            }
        }

        private static void WriteTasks(List<MultiScanTask> tasks)
        {
            var path = TasksFilePath;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            // FileShare.None — exclusive write, no other process can touch it
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(JsonSerializer.Serialize(tasks, _jsonOptions));
        }

        // ── Public: read tasks for the polling endpoint ───────────────────────
        // Controller's MultiScanGetTasks should call this instead of reading the file directly
        public static List<MultiScanTask> GetAllTasks()
        {
            lock (_multiscantaskFileLock)
            {
                return ReadTasks();
            }
        }

        // ── Public: add a new task ────────────────────────────────────────────
        public static int AddMultiScanTask(MultiScanTask newTask)
        {
            lock (_multiscantaskFileLock)
            {
                var tasks = ReadTasks();

                int newId = tasks.Any() ? tasks.Max(t => t.Id) + 1 : 1;
                newTask.Id = newId;
                tasks.Add(newTask);

                WriteTasks(tasks);
                return newId;
            }
        }

        // ── Public: Delete task ────────────────────────────────────────────
        public static void DeleteMultiScanTask(int taskId)
        {
            lock (_multiscantaskFileLock)
            {
                var tasks = ReadTasks();
                tasks = tasks.Where(t => t.Id != taskId).ToList();
                WriteTasks(tasks);
            }
        }
        // ── Public: update task status ────────────────────────────────────────
        public static void UpdateMultiScanTask(int taskId, string newStatus, string? errorMessage = null)
        {
            lock (_multiscantaskFileLock)
            {
                try
                {
                    var tasks = ReadTasks();

                    var task = tasks.FirstOrDefault(t => t.Id == taskId);
                    if (task == null) return;

                    task.Status = newStatus;

                    if (!string.IsNullOrWhiteSpace(errorMessage))
                        task.ErrorMessage = errorMessage;

                    if (newStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                        newStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                    {
                        task.CompletionTIme = DateTime.UtcNow.ToString("o");
                    }

                    WriteTasks(tasks);
                }
                catch
                {
                    // optionally log
                }
            }
        }

        public static void UpdateMultiScanTaskField(int taskId, string fieldName, object? newValue)
        {
            lock (_multiscantaskFileLock)
            {
                try
                {
                    var tasks = ReadTasks();

                    var task = tasks.FirstOrDefault(t => t.Id == taskId);
                    if (task == null) return;

                    // Find property (case-insensitive)
                    var prop = typeof(MultiScanTask).GetProperty(
                        fieldName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
                    );

                    if (prop == null || !prop.CanWrite)
                        return;

                    // Convert value to correct type
                    object? convertedValue = newValue;

                    if (newValue != null && prop.PropertyType != newValue.GetType())
                    {
                        convertedValue = Convert.ChangeType(newValue, prop.PropertyType);
                    }

                    // Set value
                    prop.SetValue(task, convertedValue);

                    WriteTasks(tasks);
                }
                catch
                {
                    // optionally log
                }
            }
        }




        //time tracker methods would go here

        // ── Private helpers (always called inside lock) ───────────────────────

        private static List<TargetScanTimeTracker> ReadTargetScanTimeTrackers()
        {
            var path = TargetScanTimeTrackerFilePath;

            if (!File.Exists(path))
                return new List<TargetScanTimeTracker>();

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                using var reader = new StreamReader(stream);

                var json = reader.ReadToEnd();

                return JsonSerializer.Deserialize<List<TargetScanTimeTracker>>(json, _jsonOptions)
                       ?? new List<TargetScanTimeTracker>();
            }
            catch
            {
                return new List<TargetScanTimeTracker>();
            }
        }

        private static void WriteTargetScanTimeTrackers(
            List<TargetScanTimeTracker> trackers)
        {
            var path = TargetScanTimeTrackerFilePath;

            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(path)!);

            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            using var writer = new StreamWriter(stream);

            writer.Write(
                JsonSerializer.Serialize(trackers, _jsonOptions));
        }



        public static int AddTargetScanTimeTracker(TargetScanTimeTracker tracker)
        {
            lock (_targetScanTimeTrackerLock)
            {
                var trackers = ReadTargetScanTimeTrackers();

                int newId = trackers.Any()
                    ? trackers.Max(t => t.Id) + 1
                    : 1;

                tracker.Id = newId;

                trackers.Add(tracker);

                WriteTargetScanTimeTrackers(trackers);

                return newId;
            }
        }

        public static void DeleteTargetScanTimeTracker(int id)
        {
            lock (_targetScanTimeTrackerLock)
            {
                var trackers = ReadTargetScanTimeTrackers();

                trackers = trackers
                    .Where(t => t.Id != id)
                    .ToList();

                WriteTargetScanTimeTrackers(trackers);
            }
        }


        public static void UpdateTargetScanTimeTrackerField(
    int id,
    string fieldName,
    object? newValue)
        {
            lock (_targetScanTimeTrackerLock)
            {
                try
                {
                    var trackers = ReadTargetScanTimeTrackers();

                    var tracker = trackers.FirstOrDefault(t => t.Id == id);

                    if (tracker == null)
                        return;

                    var prop = typeof(TargetScanTimeTracker).GetProperty(
                        fieldName,
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.IgnoreCase);

                    if (prop == null || !prop.CanWrite)
                        return;

                    object? convertedValue = newValue;

                    if (newValue != null &&
                        prop.PropertyType != newValue.GetType())
                    {
                        convertedValue =
                            Convert.ChangeType(newValue, prop.PropertyType);
                    }

                    prop.SetValue(tracker, convertedValue);

                    WriteTargetScanTimeTrackers(trackers);
                }
                catch
                {
                }
            }
        }

        public static TargetScanTimeTracker? GetTargetScanTimeTracker(
    int targetId)
        {
            lock (_targetScanTimeTrackerLock)
            {
                return ReadTargetScanTimeTrackers()
                    .FirstOrDefault(t => t.TargetId == targetId);
            }
        }



        public static void UpdateTargetScanTimeTracker(TargetScanTimeTracker updatedTracker)
        {
            lock (_targetScanTimeTrackerLock)
            {
                var trackers = ReadTargetScanTimeTrackers();

                var tracker = trackers.FirstOrDefault(t => t.Id == updatedTracker.Id);

                if (tracker == null)
                    return;

                tracker.TargetId = updatedTracker.TargetId;
                tracker.TargetName = updatedTracker.TargetName;
                tracker.StartTime = updatedTracker.StartTime;
                tracker.StopTime = updatedTracker.StopTime;

                WriteTargetScanTimeTrackers(trackers);
            }
        }

       




        public static string StreamFolderPath =>
       System.IO.Path.Combine(GlobalVariables.root_folder, "Targets", "TargetStreams");

        public static string SanitizeTargetName(string targetName) =>
            string.Concat((targetName ?? string.Empty).Split(System.IO.Path.GetInvalidFileNameChars()));

        public static string GetStreamFileName(string targetName, DateTime? date = null)
        {
            var d = date ?? DateTime.Now;
            return $"{SanitizeTargetName(targetName)}_ConsolidatedResult_{d:yyyy-MM-dd}.json";
        }

        private static TargetStreamResult ReadTargetStreamResult(string path)
        {
            if (!File.Exists(path)) return new TargetStreamResult();
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<TargetStreamResult>(json, _jsonOptions) ?? new TargetStreamResult();
            }
            catch { return new TargetStreamResult(); }
        }

        private static void WriteTargetStreamResult(string path, TargetStreamResult result)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(JsonSerializer.Serialize(result, _jsonOptions));
        }

        public static void UpdateTargetStreamCounts(string targetName, int scannedCount, int matchedCount)
        {
            lock (_targetStreamFileLock)
            {
                var fullPath = System.IO.Path.Combine(StreamFolderPath, GetStreamFileName(targetName));
                var result = ReadTargetStreamResult(fullPath);

                result.TotalScansToday += scannedCount;
                result.TotalMatchedToday += matchedCount;
                result.LastUpdated = DateTime.UtcNow.ToString("o");

                WriteTargetStreamResult(fullPath, result);
            }
        }

        // Read-only lookup for the controller — no write, no lock needed
        public static TargetStreamResult? GetTodayStreamResult(string targetName)
        {
            var fullPath = System.IO.Path.Combine(StreamFolderPath, GetStreamFileName(targetName));
            if (!File.Exists(fullPath)) return null;

            lock (_targetStreamFileLock)
            {
                return ReadTargetStreamResult(fullPath);
            }
        }










        //==============================================================================================================================================



        public static async Task<object> SingleScanScreener(
      double Threshold,
      string SearchTerm,
      string field,
      IServiceScopeFactory scopeFactory)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var sanctionMatches = new List<(string EntryId, string Matched, double Similarity, int EditDistance)>();

            try
            {
                if (!File.Exists(GlobalVariables.base_sanction_db_path))
                    throw new FileNotFoundException("Base up sanction DB file not found.", GlobalVariables.base_sanction_db_path);

                var sanction_entries = SanctionExcelReader.LoadFromExcel(GlobalVariables.base_sanction_db_path);
                var normalized_sanction_entries = GlobalFunctions.NormalizeSanctionListNames(sanction_entries);

                if (field == "name")
                {
                    SearchTerm = GlobalFunctions.NormalizeString(SearchTerm);
                    var tree = new SanctionNamesBKTree(threshold: Threshold, caseSensitive: false);
                    tree.Load(normalized_sanction_entries);

                    var results = tree.Search(SearchTerm);
                    foreach (var r in results)
                        sanctionMatches.Add((r.EntryId, r.MatchedName, r.Similarity, r.EditDistance));
                }
                else if (field == "email")
                {
                    var tree = new SanctionEmailsBKTree();
                    tree.Load(normalized_sanction_entries);

                    foreach (var r in tree.Search(SearchTerm, Threshold))
                        sanctionMatches.Add((r.EntryId, r.Matched, r.Similarity, r.EditDistance));
                }
                else if (field == "phone")
                {
                    var tree = new SanctionPhoneNumberBKTree();
                    tree.Load(sanction_entries);

                    foreach (var r in tree.Search(SearchTerm, Threshold))
                        sanctionMatches.Add((r.EntryId, r.Matched, r.Similarity, r.EditDistance));
                }
                else if (field == "address")
                {
                    var tree = new SanctionAddressesBKTree();
                    tree.Load(sanction_entries);

                    foreach (var r in tree.Search(SearchTerm, Threshold))
                        sanctionMatches.Add((r.EntryId, r.Matched, r.Similarity, r.EditDistance));
                }
                else
                {
                    return new
                    {
                        success = false,
                        data = sanctionMatches,
                        message = "Invalid field provided"
                    };
                }

                return new
                {
                    success = true,
                    data = sanctionMatches,
                    message = sanctionMatches.Count > 0 ? "Matches found" : "No matches found"
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    data = sanctionMatches,
                    message = ex.Message
                };
            }
        }







        // ── Background scan ───────────────────────────────────────────────────
        // ── Background scan ───────────────────────────────────────────────────
        public static async System.Threading.Tasks.Task MultiScanScreener(MultiScanTask task, IServiceScopeFactory scopeFactory)
        {
            UpdateMultiScanTask(task.Id, "Scanning", null);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                if (!File.Exists(GlobalVariables.base_sanction_db_path))
                    throw new FileNotFoundException("Base up sanction DB file not found.", GlobalVariables.base_sanction_db_path);

                var sanction_entries = SanctionExcelReader.LoadFromExcel(GlobalVariables.base_sanction_db_path);

                var svc = new UpSanctionSettingsService(db);
                SettingsResult<ScanSettings> scan_settings_result = await svc.GetScanSettingsAsync();
                if (!scan_settings_result.Success)
                    throw new Exception($"Failed to load scan settings: {scan_settings_result.Error}");

                int default_threshold = scan_settings_result.Data.ScanThreshold;
                string file_extension = GlobalFunctions.GetFileFileExtension(task.FileName);

                string result_export_folder = System.IO.Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanResult");
                Directory.CreateDirectory(result_export_folder);
                string nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(task.FileName);
                string result_file_name = nameWithoutExtension + "_result.xlsx";
                var result_export_path = System.IO.Path.Combine(result_export_folder, result_file_name);

                if (file_extension == "txt")
                {
                    // ── Pasted name list — now routed through the same field-mapping pipeline
                    //    as document/target scans, treating "ScanItems" as a single name column ──
                    var normalized_sanction_entries = GlobalFunctions.NormalizeSanctionListNames(sanction_entries);
                    var file_read_result = GlobalFunctions.ReadTaskFile("txt", task.AutoGenerateId, task.FilePath, "", "");
                    if (!file_read_result.Success)
                        throw new Exception($"Failed to read task file: {file_read_result.Error}");

                    DataTable data_to_scan = GlobalFunctions.DeduplicateDatatbaleById(file_read_result.Data, "ID");
                    data_to_scan = GlobalFunctions.NormaLizeNamesinColumn(data_to_scan, "ScanItems");

                    var tree = new SanctionNamesBKTree(default_threshold / 100.00, caseSensitive: false);
                    tree.Load(normalized_sanction_entries);

                    var pasteMappings = new List<FieldMapping>
            {
                new FieldMapping
                {
                    ColumnName = "ScanItems",
                    MatchAs    = "name",
                    IsJson     = false,
                    SubFields  = new List<SubFieldMapping>()
                }
            };

                    string pasteLogFolder = System.IO.Path.Combine(GlobalVariables.root_folder, "Logs", "MultiScanLogs");
                    string pasteLogFile = $"MultiScan_{task.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_paste";

                    List<TargetScanResult> scan_results = ParallelTargetScan(
                         tree,
                         data_to_scan,
                         sanction_entries,
                         pasteLogFolder,
                         pasteLogFile,
                         "ID",
                         pasteMappings,
                         default_threshold / 100.00);   // ← added

                    TargetScanResultExporter.ExportToExcel(
                        scan_results,
                        scanType: "Multi-Scan",
                        outputPath: result_export_path);
                }
                else
                {
                    // ── File-based scan — same field-mapping pipeline as Target Scans ──
                    bool generateId = string.Equals(task.AutoGenerateId, "true", StringComparison.OrdinalIgnoreCase);

                    DataTable data_to_scan;

                    if (file_extension == "csv")
                    {
                        var csvResult = new CsvFileReader().ReadTargetCsvFile(task.FilePath, task.IdColumn, task.FieldMappings, generateId);
                        if (!csvResult.Success)
                            throw new Exception(csvResult.Error);
                        data_to_scan = csvResult.Data!;
                    }
                    else
                    {
                        var excelResult = new ExcelMultiSheetReader().ReadTargetExcelFile(task.FilePath, task.IdColumn, task.FieldMappings, generateId);
                        if (!excelResult.Success)
                            throw new Exception(excelResult.Error);
                        data_to_scan = excelResult.Data!;
                    }

                    string idColumnForDedupe = generateId ? "ID" : task.IdColumn;
                    var unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, idColumnForDedupe);

                    // ── JSON sub-field extraction, identical to Target Scan ──
                    var jsonFieldGroups = task.FieldMappings
                        .Where(f => f.IsJson)
                        .GroupBy(f => f.ColumnName, StringComparer.OrdinalIgnoreCase);

                    foreach (var group in jsonFieldGroups)
                    {
                        var jsonColumnName = group.Key;
                        if (!unique_items.Columns.Contains(jsonColumnName))
                            continue;

                        var subFields = group.SelectMany(f => f.SubFields).ToList();
                        if (subFields.Count == 0)
                            continue;

                        unique_items = SubFieldExtractor.ExtractSubFields(unique_items, jsonColumnName, subFields);
                    }

                    var flattenedMappings = SubFieldExtractor.FlattenFieldMappings(task.FieldMappings);
                    var normalized_data_to_scan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, flattenedMappings, "name");

                    var normalized_sanction_entries = GlobalFunctions.NormalizeSanctionListNames(sanction_entries);
                    var tree = new SanctionNamesBKTree(default_threshold / 100.00, caseSensitive: false);
                    tree.Load(normalized_sanction_entries);

                    string logFolder = System.IO.Path.Combine(GlobalVariables.root_folder, "Logs", "MultiScanLogs");
                    string logFile = $"MultiScan_{task.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

                    List<TargetScanResult> scan_results = ParallelTargetScan(
                      tree,
                      normalized_data_to_scan,
                      sanction_entries,
                      logFolder,
                      logFile,
                      idColumnForDedupe,
                      flattenedMappings,
                      default_threshold / 100.00);   // ← added

                    TargetScanResultExporter.ExportToExcel(
                        scan_results,
                        scanType: "Multi-Scan",
                        outputPath: result_export_path);
                }

                UpdateMultiScanTaskField(task.Id, "ResultFileName", result_file_name);
                UpdateMultiScanTaskField(task.Id, "ResultPath", result_export_path);
                UpdateMultiScanTask(task.Id, "Completed", "");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                UpdateMultiScanTask(task.Id, "Failed", ex.Message);
            }
        }

        public static async System.Threading.Tasks.Task TargetScanScreener(
         int targetID, string targetName, object targetfrequency, IServiceScopeFactory scopeFactory, int attempt = 1)
        {
            string log_folder = string.Empty;
            string log_file = string.Empty;
            const int maxAttempts = 6;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                string folderName = System.IO.Path.Combine(
                    GlobalVariables.root_folder, "Logs", "TargetScanLogs");
                log_folder = folderName;
                string  streamFolderName = System.IO.Path.Combine(
                    GlobalVariables.root_folder, "Targets", "TargetStreams");

                string safeTargetName = string.Concat(targetName.Split(System.IO.Path.GetInvalidFileNameChars()));
                string streamFileName = $"{safeTargetName}_ConsolidatedResult_{DateTime.Now:yyyy-MM-dd}.json";


                string fileName = BuildLogFileName(targetName, targetfrequency.ToString());
                log_file = fileName;

                Directory.CreateDirectory(folderName);
                string fullPath = System.IO.Path.Combine(folderName, fileName + ".log");

                Logger.LogToFile(folderName, fileName, $"[START] Target: {targetName} (ID: {targetID}) | Frequency: {targetfrequency} | {DateTime.Now:O}{Environment.NewLine}");
                Logger.LogToFile(folderName, fileName, $"[STEP 1]: GET SANCTION PORTAL SETTINGS AND TARGET DETAILS");

                var svc = new UpSanctionSettingsService(db);
                var allSanctionSettings = await svc.GetAllAsync();

                if (!allSanctionSettings.Success)
                {
                    throw new Exception($"Error fetching sanction portal settings:\n\n {allSanctionSettings.Error} ");
                }

                var targets = allSanctionSettings.Data.Targets;
                var target = targets.FirstOrDefault(t => t.Id == targetID);
                if (target is null)
                {
                    throw new Exception($"Could not find target ");
                }

                var scansettings = allSanctionSettings.Data.ScanSettings;
                if (scansettings is null)
                {
                    throw new Exception($"Could not find scan settings ");
                }

                Logger.LogToFile(folderName, fileName, $"[STEP 1 - COMPLETED]: PORTAL SETTINGS AND TARGET DETAILS RETRIEVED.");

                Logger.LogToFile(folderName, fileName, $"[STEP 2]: FETCH DATA TO SCAN");

                TaskFileReadResult file_read_result = new TaskFileReadResult();
                DatabaseReadResult database_read_result = new DatabaseReadResult();
                DataTable data_to_scan = new DataTable();
                DataTable unique_items = new DataTable();
                DataTable NormalizedDataToScan = new DataTable();
                List<FieldMapping> FieldMappings = target.DatabaseSettings.DataSettings.OtherFields;

                if (target.TargetType == "document")
                {
                    file_read_result = GlobalFunctions.ReadTargetFile(
                        target.DocumentSettings.UploadPath,
                        target.DocumentSettings.IdColumn,
                        target.DocumentSettings.OtherFields);

                    if (!file_read_result.Success)
                    {
                        throw new Exception(file_read_result.Error);
                    }

                    data_to_scan = file_read_result.Data;
                    unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, target.DocumentSettings.IdColumn);

                    // ── Extract JSON subfields (mirrors database branch) ──────────────
                    var docFieldMappings = target.DocumentSettings.OtherFields;

                    if (docFieldMappings is not null)
                    {
                        var jsonFieldGroups = docFieldMappings
                            .Where(f => f.IsJson)
                            .GroupBy(f => f.ColumnName, StringComparer.OrdinalIgnoreCase);

                        foreach (var group in jsonFieldGroups)
                        {
                            var jsonColumnName = group.Key;

                            if (!unique_items.Columns.Contains(jsonColumnName))
                                continue;

                            var subFields = group.SelectMany(f => f.SubFields).ToList();
                            if (subFields.Count == 0)
                                continue;

                            unique_items = SubFieldExtractor.ExtractSubFields(unique_items, jsonColumnName, subFields);
                        }
                    }

                    // ── Flatten mappings so ParallelTargetScan gets the resolved column list ──
                    FieldMappings = SubFieldExtractor.FlattenFieldMappings(docFieldMappings);

                    NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, FieldMappings, "name");
                }
                else
                {
                    string lasttrackedtime = string.Empty;

                    if (target.AutomationSettings.TrackTime)
                    {
                        TargetScanTimeTracker? targettracker = GetTargetScanTimeTracker(targetID);

                        if (targettracker == null)
                        {
                            /// Create a new tracker if it doesn't exist
                        }
                        else
                        {
                            lasttrackedtime = targettracker.StopTime;
                        }
                    }

                    string Query = DatabaseDataReader.DatabaseQueryBuilder.BuildSelectQuery(target.DatabaseSettings, target.AutomationSettings, lasttrackedtime);
                    Logger.LogToFile(folderName, fileName, $"Original Constructed Query:\n\n {Query}");

                    database_read_result = await DatabaseDataReader.ReadDatabaseRecords(Query, target.DatabaseSettings, folderName, fileName);

                    if (!database_read_result.Successful)
                    {
                        throw new Exception(database_read_result.Message);
                    }

                    data_to_scan = database_read_result.Data;

                    DateTime? startTime = null;
                    DateTime? stopTime = null;

                    if (target.AutomationSettings.TrackTime)
                    {
                        startTime = data_to_scan.AsEnumerable().Min(row => row.Field<DateTime?>(target.AutomationSettings.TimeColumn));

                        stopTime = data_to_scan.AsEnumerable().Max(row => row.Field<DateTime?>(target.AutomationSettings.TimeColumn));

                        //check if the target is already tracked
                        var tracker = GetTargetScanTimeTracker(targetID);

                        if (tracker == null)
                        {
                            var targetTracker = new TargetScanTimeTracker
                            {
                                TargetId = targetID,
                                TargetName = target.TargetName,
                                StartTime = startTime?.ToString("o"),
                                StopTime = stopTime?.ToString("o")
                            };

                            AddTargetScanTimeTracker(targetTracker);
                        }
                        else
                        {
                            tracker.StartTime = startTime?.ToString("o");
                            tracker.StopTime = stopTime?.ToString("o");

                            UpdateTargetScanTimeTracker(tracker);
                        }
                    }

                    unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, target.DatabaseSettings.DataSettings.IdColumn);

                    if (FieldMappings is not null)
                    {
                        var jsonFieldGroups = FieldMappings
                            .Where(f => f.IsJson)
                            .GroupBy(f => f.ColumnName, StringComparer.OrdinalIgnoreCase);

                        foreach (var group in jsonFieldGroups)
                        {
                            var jsonColumnName = group.Key;

                            if (!unique_items.Columns.Contains(jsonColumnName))
                                continue;

                            var subFields = group.SelectMany(f => f.SubFields).ToList();
                            if (subFields.Count == 0)
                                continue;

                            // Reassign — each pass returns a new table built on top of the previous one,
                            // so multiple JSON columns chain correctly.
                            unique_items = SubFieldExtractor.ExtractSubFields(unique_items, jsonColumnName, subFields);
                        }
                    }

                    FieldMappings = SubFieldExtractor.FlattenFieldMappings(target.DatabaseSettings.DataSettings.OtherFields);

                    NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, FieldMappings, "name");
                }

                data_to_scan = NormalizedDataToScan;

                Logger.LogToFile(folderName, fileName, $"[STEP 2 - COMPLETED]: {data_to_scan.Rows.Count} Items Fetched, {unique_items.Rows.Count} Unique Items, normalized by ID");

                Logger.LogToFile(folderName, fileName, $"[STEP 3]: LOAD SANCTION ENTRIES AND SEARCH TREE");
                unique_items = null;
                NormalizedDataToScan = null;

                List<SanctionEntry> sanction_entries = SanctionExcelReader.LoadFromExcel(GlobalVariables.base_sanction_db_path);
                var normalized_sanction_entries = GlobalFunctions.NormalizeSanctionListNames(sanction_entries);
                var tree = new SanctionNamesBKTree(threshold: (scansettings.ScanThreshold / 100.00), caseSensitive: false);
                tree.Load(normalized_sanction_entries);

                Logger.LogToFile(folderName, fileName, $"[STEP 3 - COMPLETED]: SEARCH TREE LOADED");
                unique_items = null;
                NormalizedDataToScan = null;

                Logger.LogToFile(folderName, fileName, $"[STEP 4]: BEGIN SCAN");

                List<TargetScanResult> TargetScreenResults = ParallelTargetScan(
                         tree,
                         data_to_scan,
                         sanction_entries,
                         folderName,
                         fileName,
                         target.TargetType == "document" ? target.DocumentSettings.IdColumn : target.DatabaseSettings.DataSettings.IdColumn,
                         FieldMappings,
                         scansettings.ScanThreshold / 100.00);   // ← added

                Logger.LogToFile(folderName, fileName, $"[STEP 4 - COMPLETED]: SCAN COMPLETED");

                if (target.StreamResults)
                {
                    Logger.LogToFile(folderName, fileName, $"[STEP 4.1]: UPDATING STREAMING COUNTS FOR TODAY");
                    Scanner.UpdateTargetStreamCounts(targetName, data_to_scan.Rows.Count, TargetScreenResults.Count);
                    Logger.LogToFile(folderName, fileName, $"[STEP 4.1 - COMPLETED]: STREAM COUNTS UPDATED");
                }


                Logger.LogToFile(folderName, fileName, $"[STEP 5]: EXPORTING SCAN RESULT");

                string outputDir = System.IO.Path.Combine(GlobalVariables.root_folder, "Targets", "TargetReports");
                Directory.CreateDirectory(outputDir);

                string outputPath = System.IO.Path.Combine(outputDir, $"{fileName}.xlsx");

                TargetScanResultExporter.ExportToExcel(
                    TargetScreenResults,
                    scanType: "Target Scan",
                    outputPath: outputPath);

                Logger.LogToFile(folderName, fileName, $"[STEP 5 - COMPLETED]: SCAN RESULTS EXPORTED TO: {outputPath}");

                if (target.NotificationSettings.Enabled)
                {
                    Logger.LogToFile(folderName, fileName, $"[STEP 6]: SENDING EMAIL NOTIFICATION");
                    // CALL EMAIL SENDING SERVICE HERE...
                    Logger.LogToFile(folderName, fileName, $"[STEP 6 - COMPLETED]: EMAIL NOTIFICATION SENT");
                }

                Logger.LogToFile(folderName, fileName, $"[END] Target: {targetName} (ID: {targetID}) | {DateTime.Now:O}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                // Log all errors (unwrap AggregateException if needed)
                IEnumerable<string> errorMessages = ex is AggregateException agg
                    ? agg.InnerExceptions.Select(e => e.Message)
                    : new[] { ex.Message };

                foreach (var msg in errorMessages)
                {
                    Logger.LogToFile(log_folder, log_file, $"[ERROR] - {msg}");
                }

                // Retry if attempts remain
                if (attempt < maxAttempts)
                {
                    int delaySeconds = (int)Math.Pow(2, attempt); // 2s, 4s, 8s ...
                    Logger.LogToFile(log_folder, log_file,
                        $"[RETRY] Attempt {attempt} of {maxAttempts} failed. Retrying in {delaySeconds}s...");

                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                    await TargetScanScreener(targetID, targetName, targetfrequency, scopeFactory, attempt + 1);
                }
                else
                {
                    Logger.LogToFile(log_folder, log_file,
                        $"[FAILED] All {maxAttempts} attempts exhausted for Target: {targetName} (ID: {targetID}). No further retries.");
                }
            }
        }





        private static string BuildLogFileName(string targetName, string? frequency)
        {
            // Sanitize target name for use in file system
            string safeName = string.Concat(targetName.Split(System.IO.Path.GetInvalidFileNameChars()));
            DateTime now = DateTime.Now;

            return frequency?.ToLowerInvariant() switch
            {
                // Minutely  → unique per minute:  TargetName_2025-07-04_14-03
                "minutely" => $"{safeName}_{now:yyyy-MM-dd_HH-mm}",

                // Hourly    → unique per hour:    TargetName_2025-07-04_14
                "hourly" => $"{safeName}_{now:yyyy-MM-dd_HH}",

                // Daily     → unique per day:     TargetName_2025-07-04
                "daily" => $"{safeName}_{now:yyyy-MM-dd}",

                // Weekly    → unique per week:    TargetName_2025-W27
                "weekly" => $"{safeName}_{now:yyyy}_W{System.Globalization.ISOWeek.GetWeekOfYear(now):D2}",

                // Monthly   → unique per month:   TargetName_2025-07
                "monthly" => $"{safeName}_{now:yyyy-MM}",

                // Fallback  → always unique (timestamp to the second)
                _ => $"{safeName}_{now:yyyy-MM-dd_HH-mm-ss}"
            };
        }



        public static List<TargetScanResult> ParallelTargetScan(
         SanctionNamesBKTree sanctionTree,
         DataTable data_to_scan,
         List<SanctionEntry> sanction_entries,
         string folderName,
         string fileName,
         string idColumn,
         List<FieldMapping> mappings,
         double threshold)
        {
            if (sanctionTree == null) throw new ArgumentNullException(nameof(sanctionTree));
            if (data_to_scan == null) throw new ArgumentNullException(nameof(data_to_scan));
            if (sanction_entries == null) throw new ArgumentNullException(nameof(sanction_entries));

            if (string.IsNullOrWhiteSpace(idColumn))
                throw new ArgumentException("ID column name cannot be empty.", nameof(idColumn));
            if (mappings == null || mappings.Count == 0)
                throw new ArgumentException("Mappings cannot be null or empty.", nameof(mappings));

            if (!data_to_scan.Columns.Contains(idColumn))
            {
                Logger.LogToFile(folderName, fileName, $"DataTable must contain an '{idColumn}' column.");
                throw new ArgumentException($"DataTable must contain an '{idColumn}' column.");
            }

            // Resolve all mapped columns by type
            List<string> ResolveColumns(string matchAs) => mappings
                .Where(m => string.Equals(m.MatchAs, matchAs, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nameColumns = ResolveColumns("name");
            var addressColumns = ResolveColumns("address");
            var emailColumns = ResolveColumns("email");
            var phoneColumns = ResolveColumns("phone");
            var genderColumns = ResolveColumns("gender");
            var dobColumns = ResolveColumns("dob");

            if (nameColumns.Count == 0)
                throw new ArgumentException("At least one column must be mapped to 'name'.", nameof(mappings));

            // Validate all mapped columns exist
            foreach (var col in nameColumns.Concat(addressColumns).Concat(emailColumns)
                         .Concat(phoneColumns).Concat(genderColumns).Concat(dobColumns))
            {
                if (!data_to_scan.Columns.Contains(col))
                    throw new ArgumentException($"Mapped column '{col}' does not exist in the DataTable.");
            }

            var dataColumns = data_to_scan.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();

            var rows = data_to_scan.Rows.Cast<DataRow>().ToArray();
            var results = new TargetScanResult[rows.Length];

            Logger.LogToFile(folderName, fileName, $"           creating sanction lookup");
            var sanctionLookup = sanction_entries
                .Where(e => !string.IsNullOrEmpty(e.ID))
                .ToDictionary(e => e.ID, e => e);

            Logger.LogToFile(folderName, fileName, $"           now scanning in parallel.");
            Parallel.ForEach(
                rows.Select((row, index) => (row, index)),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                item =>
                {
                    var rowId = item.row[idColumn]?.ToString()?.Trim();

                    // Single representative value per exclusive field type (address/email/
                    // phone/gender/dob are enforced as single-column mappings upstream).
                    string GetSingleValue(List<string> cols) =>
                        cols.Select(c => item.row[c]?.ToString()?.Trim())
                            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

                    string scanAddress = GetSingleValue(addressColumns);
                    string scanEmail = GetSingleValue(emailColumns);
                    string scanPhone = GetSingleValue(phoneColumns);
                    string scanGender = GetSingleValue(genderColumns);
                    string scanDob = GetSingleValue(dobColumns);

                    TargetScanResult bestResult = null;
                    int hitsCount = 0;

                    foreach (var nameCol in nameColumns)
                    {
                        var nameValue = item.row[nameCol]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(nameValue))
                            continue;

                        var nameHits = sanctionTree.Search(nameValue);
                        hitsCount += nameHits.Count;

                        foreach (var hit in nameHits)
                        {
                            if (hit.Similarity < threshold)
                                continue;

                            if (!sanctionLookup.TryGetValue(hit.EntryId, out var sanctionEntry))
                                continue;

                            double nameSim = hit.Similarity;
                            double addrSim = AddressMatcher(scanAddress, sanctionEntry.Addresses);
                            double emailSim = EmailMatcher(scanEmail, sanctionEntry.EmailAddresses);
                            double phoneSim = PhoneMatcher(scanPhone, sanctionEntry.PhoneNumbers);
                            double genderSim = GenderMatcher(scanGender, sanctionEntry.Gender);
                            double dobSim = DobMatcher(scanDob, sanctionEntry.DateofBirth);

                            double avgSim = (nameSim + addrSim + emailSim + phoneSim + genderSim + dobSim) / 6.0;

                            if (avgSim < threshold)
                                continue;

                            // Keep only the highest-average candidate for this row
                            if (bestResult == null || avgSim > bestResult.AverageSimilarity)
                            {
                                bestResult = new TargetScanResult
                                {
                                    RowId = rowId,
                                    Name = nameValue,
                                    Address = scanAddress,
                                    Email = scanEmail,
                                    Phone = scanPhone,
                                    Gender = scanGender,
                                    DateOfBirth = scanDob,
                                    MatchedColumn = nameCol,
                                    NameSimilarity = nameSim,
                                    AddressSimilarity = addrSim,
                                    EmailSimilarity = emailSim,
                                    PhoneSimilarity = phoneSim,
                                    GenderSimilarity = genderSim,
                                    DobSimilarity = dobSim,
                                    AverageSimilarity = avgSim,
                                    MatchedSanctionEntry = sanctionEntry
                                };
                            }
                        }
                    }

                    if (bestResult != null)
                    {
                        bestResult.HitsCount = hitsCount;

                        // Only materialize the raw row (all source columns) for rows that matched
                        bestResult.RawRowData = dataColumns
                            .Select(col => new KeyValuePair<string, string>(col, item.row[col]?.ToString()?.Trim() ?? string.Empty))
                            .ToList();

                        results[item.index] = bestResult;
                    }
                });

            return results.Where(r => r != null)
                          .OrderByDescending(r => r.AverageSimilarity)
                          .ToList();
        }




        // ── Field-level similarity matchers ─────────────────────────────────────
        // Each accepts the scanned value plus the sanction entry's known values for
        // that field, and returns a 0.0–1.0 similarity score.

        private static double AddressMatcher(string scanAddress, List<string> sanctionAddresses)
        {
            bool scanHasValue = !string.IsNullOrWhiteSpace(scanAddress);
            bool sanctionHasValue = sanctionAddresses != null && sanctionAddresses.Any(a => !string.IsNullOrWhiteSpace(a));

            // Either side missing (or both missing) -> treat as a pass
            if (!scanHasValue || !sanctionHasValue)
                return 1.0;

            string? scanState = null;
            foreach (var state in GlobalVariables.NigerianStates)
            {
                if (scanAddress.IndexOf(state, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    scanState = state;
                    break;
                }
            }

            double best = 0.0;
            foreach (var addr in sanctionAddresses)
            {
                if (string.IsNullOrWhiteSpace(addr)) continue;

                string? addrState = null;
                foreach (var state in GlobalVariables.NigerianStates)
                {
                    if (addr.IndexOf(state, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        addrState = state;
                        break;
                    }
                }

                if (scanState != null && addrState != null &&
                    string.Equals(scanState, addrState, StringComparison.OrdinalIgnoreCase))
                {
                    best = 1.0;
                    break; // can't do better than a full match
                }
            }

            return best;
        }
        private static double EmailMatcher(string scanEmail, List<string> sanctionEmails)
        {
            bool scanHasValue = !string.IsNullOrWhiteSpace(scanEmail);
            bool sanctionHasValue = sanctionEmails != null && sanctionEmails.Any(e => !string.IsNullOrWhiteSpace(e));

            if (!scanHasValue || !sanctionHasValue)
                return 1.0;

            double best = 0.0;
            foreach (var email in sanctionEmails)
            {
                double sim = ComputeStringSimilarity(scanEmail, email);
                if (sim > best) best = sim;
            }
            return best;
        }
        private static double PhoneMatcher(string scanPhone, List<string> sanctionPhones)
        {
            bool scanHasValue = !string.IsNullOrWhiteSpace(scanPhone);
            bool sanctionHasValue = sanctionPhones != null && sanctionPhones.Any(p => !string.IsNullOrWhiteSpace(p));

            if (!scanHasValue || !sanctionHasValue)
                return 1.0;

            static string NormalizePhone(string p) => new string((p ?? string.Empty).Where(char.IsDigit).ToArray());

            string normalizedScan = NormalizePhone(scanPhone);
            if (string.IsNullOrWhiteSpace(normalizedScan))
                return 1.0; // digits stripped to nothing counts as "no real value" -> pass

            double best = 0.0;
            bool anyComparable = false;

            foreach (var phone in sanctionPhones)
            {
                string normalizedSanction = NormalizePhone(phone);
                if (string.IsNullOrWhiteSpace(normalizedSanction)) continue;

                anyComparable = true;
                double sim = ComputeStringSimilarity(normalizedScan, normalizedSanction);
                if (sim > best) best = sim;
            }

            // If after normalization nothing was actually comparable, treat as a pass too
            return anyComparable ? best : 1.0;
        }

        private static double GenderMatcher(string scanGender, string sanctionGender)
        {
            bool scanHasValue = !string.IsNullOrWhiteSpace(scanGender);
            bool sanctionHasValue = !string.IsNullOrWhiteSpace(sanctionGender);

            if (!scanHasValue || !sanctionHasValue)
                return 1.0;

            return string.Equals(scanGender.Trim(), sanctionGender.Trim(), StringComparison.OrdinalIgnoreCase)
                ? 1.0
                : 0.0;
        }

        private static double DobMatcher(string scanDob, List<string> sanctionDobs)
        {
            bool scanHasValue = !string.IsNullOrWhiteSpace(scanDob);
            bool sanctionHasValue = sanctionDobs != null && sanctionDobs.Any(d => !string.IsNullOrWhiteSpace(d));

            if (!scanHasValue || !sanctionHasValue)
                return 1.0;

            if (!DateTime.TryParse(scanDob, out var scanDate))
                return 0.0; // scan value present but unparseable -> can't confirm a match

            string normalizedScanDob = scanDate.ToString("yyyy-MM-dd");
            string scanYear = scanDate.Year.ToString();

            foreach (var raw in sanctionDobs)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string dob = raw.Trim();

                // Year-only entry (e.g. "1995") -> compare year only
                if (dob.Length == 4 && int.TryParse(dob, out _))
                {
                    if (dob == scanYear)
                        return 1.0;

                    continue;
                }

                // Full date entry (e.g. "1973-03-06") -> compare exact normalized date
                if (string.Equals(normalizedScanDob, dob, StringComparison.Ordinal))
                    return 1.0;
            }

            return 0.0;
        }
        // ── Generic normalized string similarity (Levenshtein-based) ────────────
        private static double ComputeStringSimilarity(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return 0.0;

            a = a.Trim().ToLowerInvariant();
            b = b.Trim().ToLowerInvariant();

            if (a == b) return 1.0;

            int distance = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0;

            return 1.0 - ((double)distance / maxLen);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length, m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }






    }

}































