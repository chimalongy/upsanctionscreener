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
        public List<SanctionNamesBKTree.BKSearchResult> Hits { get; set; } = new();
        public List<DataRow> ResolvedSanctionEntries { get; set; } = new();
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



        public static async System.Threading.Tasks.Task TargetScanScreener(
     int targetID, string targetName, object targetfrequency, IServiceScopeFactory scopeFactory)
        {
           
            try
            {
              
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

               

                   

                    string folderName = System.IO.Path.Combine(
                        GlobalVariables.root_folder, "Logs", "TargetScanLogs");

                    // Each trigger gets a uniquely named log file
                    string fileName = BuildLogFileName(targetName, targetfrequency.ToString());

                    // Ensure folder exists
                    Directory.CreateDirectory(folderName);
                    string fullPath = System.IO.Path.Combine(folderName, fileName + ".log");

                // Write opening entry
                Logger.LogToFile(folderName, fileName, $"[START] Target: {targetName} (ID: {targetID}) | Frequency: {targetfrequency} | {DateTime.Now:O}{Environment.NewLine}");
                Logger.LogToFile(folderName, fileName, $"[STEP 1]: GET SACTION PORTAL SETTINGS AND TARGET DETAILS");
                var svc = new UpSanctionSettingsService(db);
                var allSanctionSettings = await svc.GetAllAsync();

                if (!allSanctionSettings.Success )
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
                if (scansettings is null) {
                    throw new Exception($"Could not find scan settings ");
                }

                Logger.LogToFile(folderName, fileName, $"[STEP 2]: FETCH DATA TO SCAN");
                    TaskFileReadResult file_read_result = new TaskFileReadResult();
                    DatabaseReadResult database_read_result = new DatabaseReadResult();
                    DataTable data_to_scan = new DataTable();

                    if (target.TargetType == "document")
                    {
                        file_read_result = GlobalFunctions.ReadTargetFile(target.DocumentSettings.UploadPath, target.DocumentSettings.IdColumn, target.DocumentSettings.OtherFields);
                        if (!file_read_result.Success)
                        {
                            throw new Exception(file_read_result.Error);
                        }
                        data_to_scan = file_read_result.Data;
                    }
                    else
                    {
                        string Query = DatabaseDataReader.DatabaseQueryBuilder.BuildSelectQuery(target.DatabaseSettings.DataSettings);
                        database_read_result = await DatabaseDataReader.ReadDatabaseRecords(Query, target.DatabaseSettings);
                        if (!database_read_result.Successful)
                        {
                            throw new Exception(database_read_result.Message);
                        }
                        data_to_scan = database_read_result.Data;
                }

                DataTable unique_items = GlobalFunctions.DeduplicateDatatbaleById(data_to_scan, "ID");
                DataTable NormalizedDataToScan = GlobalFunctions.NormaLizeNamesinColumn(unique_items, "Name");
                data_to_scan = NormalizedDataToScan;
               

                Logger.LogToFile(folderName, fileName, $"[STEP 2- COMPLETED]:  {data_to_scan.Rows.Count} Items Fetched, {unique_items.Rows.Count} Unique Items, normalized by ID");
                Logger.LogToFile(folderName, fileName, $"[STEP 2- COMPLETED]:  {data_to_scan.Rows.Count} Items Fetched, {unique_items.Rows.Count}Unique Items by ID");

                unique_items = null;
                NormalizedDataToScan = null;





                // ── Your scan logic goes here ──────────────────────────────────
                // e.g. await ScanAsync(target, fullPath);
                // ──────────────────────────────────────────────────────────────
                Logger.LogToFile(folderName, fileName, $"[END]   Target: {targetName} (ID: {targetID}) | {DateTime.Now:O}{Environment.NewLine}");

                  
                        
                
            }
            catch (Exception ex)
            {
                // Optionally write a fallback error log here
                string errorFolder = System.IO.Path.Combine(
                    GlobalVariables.root_folder, "Logs", "TargetScanLogs", "_errors");
                Directory.CreateDirectory(errorFolder);
                string errorFile = System.IO.Path.Combine(errorFolder,
                    $"{targetName}_ERROR_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                await File.AppendAllTextAsync(errorFile,
                    $"[EXCEPTION] {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
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
     DataTable sanction_entries)
        {
            if (sanctionTree == null) throw new ArgumentNullException(nameof(sanctionTree));
            if (data_to_scan == null) throw new ArgumentNullException(nameof(data_to_scan));
            if (sanction_entries == null) throw new ArgumentNullException(nameof(sanction_entries));

            if (!data_to_scan.Columns.Contains("ID"))
                throw new ArgumentException("DataTable must contain an 'ID' column.");

            // Detect which optional fields are present
            bool hasName = data_to_scan.Columns.Contains("name");
            bool hasAddress = data_to_scan.Columns.Contains("address");
            //bool hasEmail = data_to_scan.Columns.Contains("email");
            //bool hasPhone = data_to_scan.Columns.Contains("phone");

            var rows = data_to_scan.Rows.Cast<DataRow>().ToArray();
            var results = new TargetScanResult[rows.Length];

            // Index sanction_entries by EntryID for fast O(1) lookup
            var sanctionLookup = sanction_entries.Rows
                .Cast<DataRow>()
                .Where(r => r["ID"] != DBNull.Value)
                .ToDictionary(r => r["ID"].ToString()!, r => r);

            Parallel.ForEach(
                rows.Select((row, index) => (row, index)),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                item =>
                {
                    var rowId = item.row["ID"].ToString()?.Trim();

                    // --- Search by name ---
                    List<SanctionNamesBKTree.BKSearchResult> nameHits = new();
                    // --- Resolve full sanction entries from hits ---
                    var resolvedHits = new List<SanctionNamesBKTree.BKSearchResult>();
                    var resolvedEntries = new List<DataRow>();

                    if (hasName)
                    {
                        var nameValue = item.row["name"]?.ToString()?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(nameValue))
                            nameHits = sanctionTree.Search(nameValue);
                    }

                   

                    foreach (var hit in nameHits)
                    {
                        if (!sanctionLookup.TryGetValue(hit.EntryId, out var sanctionRow))
                            continue;

                        if (hasAddress)
                        {
                            string scan_item_address = item.row["address"]?.ToString()?.Trim() ?? string.Empty;
                            string saction_entry_address = sanctionRow["Addresses"].ToString().Trim();
                            if (!string.IsNullOrEmpty(scan_item_address) && !string.IsNullOrEmpty(saction_entry_address))
                            {
                                //check for addresss match at this point
                                string[] sanction_addresses = saction_entry_address.Split('|', StringSplitOptions.RemoveEmptyEntries)
                                .Select(x => x.Trim())
                                .ToArray();

                                var words = scan_item_address.Split(new[] { ' ', ',', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);

                                bool matchFound = words.Any(word =>
                                    sanction_addresses.Any(addr =>
                                        addr.Contains(word, StringComparison.OrdinalIgnoreCase)
                                    )
                                );

                                if (matchFound)
                                {
                                    resolvedHits.Add(hit);
                                    resolvedEntries.Add(sanctionRow);
                                }
                            }
                            else
                            {
                                resolvedHits.Add(hit);
                                resolvedEntries.Add(sanctionRow);
                            }



                        }
                        else
                        {
                            resolvedHits.Add(hit);
                            resolvedEntries.Add(sanctionRow);
                        }







                    }

                    // --- Build result row ---
                    results[item.index] = new TargetScanResult
                    {
                        RowId = rowId,
                        Name = hasName ? item.row["name"]?.ToString()?.Trim() : null,
                        Address = hasAddress ? item.row["address"]?.ToString()?.Trim() : null,
                        //Email = hasEmail ? item.row["email"]?.ToString()?.Trim() : null,
                        //Phone = hasPhone ? item.row["phone"]?.ToString()?.Trim() : null,
                        Hits = resolvedHits,
                        ResolvedSanctionEntries = resolvedEntries
                    };
                });

            return results.ToList();
        }


    }

}































