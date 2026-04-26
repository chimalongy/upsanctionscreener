using System.Text.Json;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Search
{
    public static class Scanner
    {
        internal static readonly object _multiscantaskFileLock = new object();

        private static string TasksFilePath =>
     Path.Combine(GlobalVariables.root_folder, "MultiScan", "MultiScanDB", "multiscantasks.json");


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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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

        // ── Background scan ───────────────────────────────────────────────────
        public static async Task MultiScanScreener(MultiScanTask task)
        {
            UpdateMultiScanTask(task.Id, "Scanning", null);

            await Task.Delay(TimeSpan.FromMinutes(2));

            var random = new Random();
            var status = random.Next(0, 2) == 0 ? "Completed" : "Failed";

            if (status == "Completed")
                UpdateMultiScanTask(task.Id, "Completed", "");
            else
                UpdateMultiScanTask(task.Id, "Failed", "test error message");
        }
    }
}