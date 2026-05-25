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

    public class NameScanResult
    {
        public string RowId { get; }
        public string ScannedValue { get; }
        public List<SanctionNamesBKTree.BKSearchResult> Hits { get; }
        public bool IsMatch => Hits.Count > 0;

        public NameScanResult(string rowId, string scannedValue, List<SanctionNamesBKTree.BKSearchResult> hits)
        {
            RowId = rowId;
            ScannedValue = scannedValue;
            Hits = hits;
        }
    }

    public class TargetScanResult
    {
        public string RowId { get; set; } = "";
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string MatchedColumn { get; set; }
        public List<SanctionNamesBKTree.BKSearchResult> Hits { get; set; } = new();
        public List<SanctionEntry> ResolvedSanctionEntries { get; set; } = new();  // ← changed
    }








    public static class Scanner
    {
        internal static readonly object _multiscantaskFileLock = new object();

        private static string TasksFilePath =>
          System.IO.Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanDB", "multiscantasks.json");


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
                var normalized_sanction_entries = GlobalFunctions.NormalizeSanctionListNames(sanction_entries);

                string file_extension = GlobalFunctions.GetFileFileExtension(task.FileName);

                TaskFileReadResult file_read_result = new TaskFileReadResult();

                if (file_extension == "txt")
                {
                    file_read_result = GlobalFunctions.ReadTaskFile(file_extension, task.AutoGenerateId, task.FilePath, "", "");
                }
                else
                {
                    file_read_result = GlobalFunctions.ReadTaskFile(file_extension, task.AutoGenerateId, task.FilePath, task.IdColumn, task.ScanColumn);
                }

                if (!file_read_result.Success)
                {
                    throw new Exception($"Failed to read task file: {file_read_result.Error}");
                }

                DataTable data_to_scan = file_read_result.Data;
                if (file_extension == "txt")
                {
                    data_to_scan = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, "ID");
                    data_to_scan = GlobalFunctions.NormaLizeNamesinColumn(data_to_scan, "ScanItems");
                }
                else
                {
                    data_to_scan = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, task.IdColumn);
                    data_to_scan = GlobalFunctions.NormaLizeNamesinColumn(data_to_scan, task.ScanColumn);
                }

                var svc = new UpSanctionSettingsService(db);
                SettingsResult<ScanSettings> scan_settings_result = await svc.GetScanSettingsAsync();

                if (!scan_settings_result.Success)
                {
                    throw new Exception($"Failed to load scan setttings: {scan_settings_result.Error}");
                }

                int default_threshold = scan_settings_result.Data.ScanThreshold;
                var tree = new SanctionNamesBKTree(default_threshold / 100.00, caseSensitive: false);
                tree.Load(normalized_sanction_entries);

                List<NameScanResult> scan_results = new List<NameScanResult>();

                if (file_extension== "txt")
                {
                    scan_results = ParallelNameScan(tree, data_to_scan, "ID", "ScanItems");
                }
                else
                {
                    scan_results = ParallelNameScan(tree, data_to_scan, task.IdColumn, task.ScanColumn);
                }

                var sanctionLookup =sanction_entries.ToDictionary(e => e.ID, e => e);

                string result_export_folder = System.IO.Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanResult");
                Directory.CreateDirectory(result_export_folder);
                string nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(task.FileName);
                string result_file_name = nameWithoutExtension+ "_result.xlsx";
                var result_export_path = System.IO.Path.Combine(result_export_folder,result_file_name);
                NameScanResultExporter.ExportToExcel(
                scan_results,
                sanctionLookup,
                scannedColumnName: task.ScanColumn,
                scanType: "Multi-Scan",
                outputPath: result_export_path);

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
        public static List<NameScanResult> ParallelNameScan(
            SanctionNamesBKTree sanctionTree,
            DataTable data_to_scan,
           
            string taskIdColumn,
            string taskScanColumn)
        {
            if (sanctionTree == null) throw new ArgumentNullException(nameof(sanctionTree));
            if (data_to_scan == null) throw new ArgumentNullException(nameof(data_to_scan));

            if (!data_to_scan.Columns.Contains(taskIdColumn))
                throw new ArgumentException($"Column '{taskIdColumn}' not found in DataTable.", nameof(taskIdColumn));

            if (!data_to_scan.Columns.Contains(taskScanColumn))
                throw new ArgumentException($"Column '{taskScanColumn}' not found in DataTable.", nameof(taskScanColumn));

            // Pre-size the results array to match data_to_scan length exactly
            var rows = data_to_scan.Rows.Cast<DataRow>().ToArray();
            var results = new NameScanResult[rows.Length];

            Parallel.ForEach(
                rows.Select((row, index) => (row, index)),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                item =>
                {
                    var rowId = item.row[taskIdColumn]?.ToString()?.Trim() ?? string.Empty;
                    var valueToScan = item.row[taskScanColumn]?.ToString()?.Trim() ?? string.Empty;

                    var hits = string.IsNullOrWhiteSpace(valueToScan)
                        ? new List<SanctionNamesBKTree.BKSearchResult>()
                        : sanctionTree.Search(valueToScan);

                    results[item.index] = new NameScanResult(rowId, valueToScan, hits);
                });

            return results.ToList();
        }



        public static async System.Threading.Tasks.Task TargetScanScreenerNoRetry(
     int targetID, string targetName, object targetfrequency, IServiceScopeFactory scopeFactory)
        {
            string log_folder = string.Empty;   
            string log_file = string.Empty;

            try
            {

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();





                string folderName = System.IO.Path.Combine(
                    GlobalVariables.root_folder, "Logs", "TargetScanLogs");
                log_folder = folderName;

                // Each trigger gets a uniquely named log file
                string fileName = BuildLogFileName(targetName, targetfrequency.ToString());
                log_file = fileName;

                // Ensure folder exists
                Directory.CreateDirectory(folderName);
                string fullPath = System.IO.Path.Combine(folderName, fileName + ".log");

                // Write opening entry
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
                Logger.LogToFile(folderName, fileName, $"[STEP 1 - COMPLETED]: PORTAL SETTINGS AND TARGET DETAILS RETRIEVED. ");

                Logger.LogToFile(folderName, fileName, $"[STEP 2]: FETCH DATA TO SCAN");
                TaskFileReadResult file_read_result = new TaskFileReadResult();
                DatabaseReadResult database_read_result = new DatabaseReadResult();
                DataTable data_to_scan = new DataTable();
                DataTable unique_items = new DataTable();
                DataTable NormalizedDataToScan = new DataTable();

                if (target.TargetType == "document")
                {
                    file_read_result = GlobalFunctions.ReadTargetFile(target.DocumentSettings.UploadPath, target.DocumentSettings.IdColumn, target.DocumentSettings.OtherFields);
                    if (!file_read_result.Success)
                    {
                        throw new Exception(file_read_result.Error);
                    }
                    data_to_scan = file_read_result.Data;
                    unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, target.DocumentSettings.IdColumn);
                    NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, target.DocumentSettings.OtherFields, "name");
                }
                else
                {
                    string Query = DatabaseDataReader.DatabaseQueryBuilder.BuildSelectQuery(target.DatabaseSettings.DataSettings);
                     Logger.LogToFile(folderName, fileName, $"Orignal Constructed Query:\n\n {Query}");
                    database_read_result = await DatabaseDataReader.ReadDatabaseRecords(Query, target.DatabaseSettings, folderName, fileName);
                    if (!database_read_result.Successful)
                    {
                        throw new Exception(database_read_result.Message);
                    }
                    data_to_scan = database_read_result.Data;
                    unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, target.DatabaseSettings.DataSettings.IdColumn);
                    NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, target.DatabaseSettings.DataSettings.OtherFields, "name");
                }


                
                data_to_scan = NormalizedDataToScan;
               


                Logger.LogToFile(folderName, fileName, $"[STEP 2 - COMPLETED]:  {data_to_scan.Rows.Count} Items Fetched, {unique_items.Rows.Count} Unique Items, normalized by ID");
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

                List<TargetScanResult> TargetScreenResults = ParallelTargetScan(tree, data_to_scan, sanction_entries, folderName, fileName, target.TargetType== "document" ? target.DocumentSettings.IdColumn: target.DatabaseSettings.DataSettings.IdColumn, target.TargetType == "document" ? target.DocumentSettings.OtherFields : target.DatabaseSettings.DataSettings.OtherFields);
               
                Logger.LogToFile(folderName, fileName, $"[STEP 4 - COMPLETED]: SCAN COMPLETED");

                //  Build output path ─────────────────────────────────────────────────────
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



                // ── Your scan logic goes here ──────────────────────────────────
                // e.g. await ScanAsync(target, fullPath);
                // ──────────────────────────────────────────────────────────────
                Logger.LogToFile(folderName, fileName, $"[END]   Target: {targetName} (ID: {targetID}) | {DateTime.Now:O}{Environment.NewLine}");




            }
            catch (Exception ex)
            {

                if (ex is AggregateException agg)
                {
                    foreach (var inner in agg.InnerExceptions)
                    {
                        Logger.LogToFile(log_folder, log_file, $"[ERROR] -  {inner.Message}");
                      
                    }
                        
                }
                else
                {
                    Logger.LogToFile(log_folder, log_file, $"[ERROR] -  {ex.Message}");
                   
                }

                

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
                    NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, target.DocumentSettings.OtherFields, "name");
                }
                else
                {
                    string Query = DatabaseDataReader.DatabaseQueryBuilder.BuildSelectQuery(target.DatabaseSettings.DataSettings);
                    Logger.LogToFile(folderName, fileName, $"Original Constructed Query:\n\n {Query}");

                    database_read_result = await DatabaseDataReader.ReadDatabaseRecords(Query, target.DatabaseSettings, folderName, fileName);

                    if (!database_read_result.Successful)
                    {
                        throw new Exception(database_read_result.Message);
                    }

                    data_to_scan = database_read_result.Data;
                    unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, target.DatabaseSettings.DataSettings.IdColumn);
                    NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinTargetColumn(unique_items, target.DatabaseSettings.DataSettings.OtherFields, "name");
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
                    target.TargetType == "document" ? target.DocumentSettings.OtherFields : target.DatabaseSettings.DataSettings.OtherFields);

                Logger.LogToFile(folderName, fileName, $"[STEP 4 - COMPLETED]: SCAN COMPLETED");

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


        //  public static List<TargetScanResult> ParallelTargetScan(
        //SanctionNamesBKTree sanctionTree,
        //DataTable data_to_scan,
        //List<SanctionEntry> sanction_entries,
        //string folderName,
        //string fileName,
        //string idColumn,
        //List<FieldMapping> mappings)
        //  {
        //      if (sanctionTree == null) throw new ArgumentNullException(nameof(sanctionTree));
        //      if (data_to_scan == null) throw new ArgumentNullException(nameof(data_to_scan));
        //      if (sanction_entries == null) throw new ArgumentNullException(nameof(sanction_entries));
        //      if (string.IsNullOrWhiteSpace(idColumn))
        //          throw new ArgumentException("ID column name cannot be empty.", nameof(idColumn));
        //      if (mappings == null || mappings.Count == 0)
        //          throw new ArgumentException("Mappings cannot be null or empty.", nameof(mappings));

        //      // Validate ID column exists
        //      if (!data_to_scan.Columns.Contains(idColumn))
        //      {
        //          Logger.LogToFile(folderName, fileName, $"DataTable must contain an '{idColumn}' column.");
        //          throw new ArgumentException($"DataTable must contain an '{idColumn}' column.");
        //      }

        //      // Resolve dynamic columns from mappings (case-insensitive)
        //      var nameColumns = mappings
        //          .Where(m => string.Equals(m.MatchAs, "name", StringComparison.OrdinalIgnoreCase))
        //          .Select(m => m.ColumnName)
        //          .Distinct(StringComparer.OrdinalIgnoreCase)
        //          .ToList();

        //      var addressColumns = mappings
        //          .Where(m => string.Equals(m.MatchAs, "address", StringComparison.OrdinalIgnoreCase))
        //          .Select(m => m.ColumnName)
        //          .Distinct(StringComparer.OrdinalIgnoreCase)
        //          .ToList();

        //      bool hasName = nameColumns.Any();
        //      bool hasAddress = addressColumns.Any();

        //      // Validate mapped columns exist in DataTable
        //      foreach (var col in nameColumns.Concat(addressColumns))
        //      {
        //          if (!data_to_scan.Columns.Contains(col))
        //              throw new ArgumentException($"Mapped column '{col}' does not exist in the DataTable.");
        //      }

        //      var rows = data_to_scan.Rows.Cast<DataRow>().ToArray();
        //      var results = new TargetScanResult[rows.Length];

        //      var sanctionLookup = sanction_entries
        //          .Where(e => !string.IsNullOrEmpty(e.ID))
        //          .ToDictionary(e => e.ID, e => e);

        //      Parallel.ForEach(
        //          rows.Select((row, index) => (row, index)),
        //          new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
        //          item =>
        //          {
        //              var rowId = item.row[idColumn]?.ToString()?.Trim();

        //              var resolvedHits = new List<SanctionNamesBKTree.BKSearchResult>();
        //              var resolvedEntries = new List<SanctionEntry>();
        //              string matchedColumn = null;
        //              string matchedNameValue = null;

        //              // Check ALL columns mapped to "name" — each is a candidate
        //              if (hasName)
        //              {
        //                  foreach (var nameCol in nameColumns)
        //                  {
        //                      var nameValue = item.row[nameCol]?.ToString()?.Trim() ?? string.Empty;
        //                      if (string.IsNullOrWhiteSpace(nameValue))
        //                          continue;

        //                      var nameHits = sanctionTree.Search(nameValue);

        //                      foreach (var hit in nameHits)
        //                      {
        //                          if (!sanctionLookup.TryGetValue(hit.EntryId, out var sanctionEntry))
        //                              continue;

        //                          bool addressMatched = false;

        //                          if (hasAddress)
        //                          {
        //                              // Check ALL address columns for any match
        //                              foreach (var addrCol in addressColumns)
        //                              {
        //                                  string scanAddress = item.row[addrCol]?.ToString()?.Trim() ?? string.Empty;

        //                                  if (!string.IsNullOrEmpty(scanAddress) && sanctionEntry.Addresses.Count > 0)
        //                                  {
        //                                      var words = scanAddress.Split(
        //                                          new[] { ' ', ',', '.', '-' },
        //                                          StringSplitOptions.RemoveEmptyEntries);

        //                                      bool matchFound = words.Any(word =>
        //                                          sanctionEntry.Addresses.Any(addr =>
        //                                              addr.Contains(word, StringComparison.OrdinalIgnoreCase)));

        //                                      if (matchFound)
        //                                      {
        //                                          addressMatched = true;
        //                                          break; // Found address match, no need to check other address columns
        //                                      }
        //                                  }
        //                                  else
        //                                  {
        //                                      // No address to compare — treat as match
        //                                      addressMatched = true;
        //                                      break;
        //                                  }
        //                              }
        //                          }
        //                          else
        //                          {
        //                              // No address mapping — name hit is sufficient
        //                              addressMatched = true;
        //                          }

        //                          if (addressMatched)
        //                          {
        //                              resolvedHits.Add(hit);
        //                              resolvedEntries.Add(sanctionEntry);
        //                              matchedColumn = nameCol;      // Track which column matched
        //                              matchedNameValue = nameValue; // Track the value that matched
        //                          }
        //                      }

        //                      // If you want ONLY the first matching column per row, break here:
        //                      // if (resolvedHits.Count > 0) break;
        //                  }
        //              }

        //              // Build result only if there were hits
        //              if (resolvedHits.Count > 0)
        //              {
        //                  // Concatenate all name values for reference (or just use the matched one)
        //                  var allNameValues = nameColumns
        //                      .Select(c => item.row[c]?.ToString()?.Trim())
        //                      .Where(v => !string.IsNullOrWhiteSpace(v));

        //                  var allAddressValues = addressColumns
        //                      .Select(c => item.row[c]?.ToString()?.Trim())
        //                      .Where(v => !string.IsNullOrWhiteSpace(v));

        //                  results[item.index] = new TargetScanResult
        //                  {
        //                      RowId = rowId,
        //                      Name = matchedNameValue ?? string.Join("; ", allNameValues),
        //                      Address = string.Join("; ", allAddressValues),
        //                      MatchedColumn = matchedColumn,
        //                      Hits = resolvedHits,
        //                      ResolvedSanctionEntries = resolvedEntries
        //                  };
        //              }
        //          });

        //      return results.Where(r => r != null).ToList();
        //  }

        public static List<TargetScanResult> ParallelTargetScan(
    SanctionNamesBKTree sanctionTree,
    DataTable data_to_scan,
    List<SanctionEntry> sanction_entries,
    string folderName,
    string fileName,
    string idColumn,
    List<FieldMapping> mappings)
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
            var nameColumns = mappings
                .Where(m => string.Equals(m.MatchAs, "name", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var addressColumns = mappings
                .Where(m => string.Equals(m.MatchAs, "address", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var emailColumns = mappings
                .Where(m => string.Equals(m.MatchAs, "email", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var phoneColumns = mappings
                .Where(m => string.Equals(m.MatchAs, "phone", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Validate all mapped columns exist
            foreach (var col in nameColumns.Concat(addressColumns).Concat(emailColumns).Concat(phoneColumns))
            {
                if (!data_to_scan.Columns.Contains(col))
                    throw new ArgumentException($"Mapped column '{col}' does not exist in the DataTable.");
            }

            var rows = data_to_scan.Rows.Cast<DataRow>().ToArray();
            var results = new TargetScanResult[rows.Length];

            var sanctionLookup = sanction_entries
                .Where(e => !string.IsNullOrEmpty(e.ID))
                .ToDictionary(e => e.ID, e => e);

            Parallel.ForEach(
                rows.Select((row, index) => (row, index)),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                item =>
                {
                    var rowId = item.row[idColumn]?.ToString()?.Trim();

                    var resolvedHits = new List<SanctionNamesBKTree.BKSearchResult>();
                    var resolvedEntries = new List<SanctionEntry>();
                    string matchedColumn = null;
                    string matchedNameValue = null;

                    // Collect all field values for the result
                    var allNames = nameColumns
                        .Select(c => item.row[c]?.ToString()?.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();

                    var allAddresses = addressColumns
                        .Select(c => item.row[c]?.ToString()?.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();

                    var allEmails = emailColumns
                        .Select(c => item.row[c]?.ToString()?.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();

                    var allPhones = phoneColumns
                        .Select(c => item.row[c]?.ToString()?.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();

                    // Search each name column
                    foreach (var nameCol in nameColumns)
                    {
                        var nameValue = item.row[nameCol]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(nameValue))
                            continue;

                        var nameHits = sanctionTree.Search(nameValue);

                        foreach (var hit in nameHits)
                        {
                            if (!sanctionLookup.TryGetValue(hit.EntryId, out var sanctionEntry))
                                continue;

                            bool addressMatched = false;

                            if (addressColumns.Any())
                            {
                                foreach (var addrCol in addressColumns)
                                {
                                    string scanAddress = item.row[addrCol]?.ToString()?.Trim() ?? string.Empty;

                                    if (!string.IsNullOrEmpty(scanAddress) && sanctionEntry.Addresses?.Count > 0)
                                    {
                                        var words = scanAddress.Split(
                                            new[] { ' ', ',', '.', '-' },
                                            StringSplitOptions.RemoveEmptyEntries);

                                        bool matchFound = words.Any(word =>
                                            sanctionEntry.Addresses.Any(addr =>
                                                addr.Contains(word, StringComparison.OrdinalIgnoreCase)));

                                        if (matchFound)
                                        {
                                            addressMatched = true;
                                            break;
                                        }
                                    }
                                    else if (string.IsNullOrEmpty(scanAddress) || sanctionEntry.Addresses?.Count == 0)
                                    {
                                        addressMatched = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                addressMatched = true;
                            }

                            if (addressMatched)
                            {
                                resolvedHits.Add(hit);
                                resolvedEntries.Add(sanctionEntry);
                                matchedColumn = nameCol;
                                matchedNameValue = nameValue;
                            }
                        }

                        // Uncomment to stop after first matching column:
                        // if (resolvedHits.Count > 0) break;
                    }

                    if (resolvedHits.Count > 0)
                    {
                        results[item.index] = new TargetScanResult
                        {
                            RowId = rowId,
                            Name = matchedNameValue ?? string.Join("; ", allNames),
                            Address = string.Join("; ", allAddresses),
                            Email = string.Join("; ", allEmails),
                            Phone = string.Join("; ", allPhones),
                            MatchedColumn = matchedColumn,
                            Hits = resolvedHits,
                            ResolvedSanctionEntries = resolvedEntries
                        };
                    }
                });

            return results.Where(r => r != null).ToList();
        }





    }

}































