using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Upsanctionscreener.Controllers;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Utils
{
    // ══════════════════════════════════════════════════════════════════════════
    // TYPED MODELS
    // ══════════════════════════════════════════════════════════════════════════

    public class ScanSettings
    {
        [JsonPropertyName("scan_threshold")]
        public int ScanThreshold { get; set; }

        [JsonPropertyName("email_recipents")]
        public List<string> EmailRecipients { get; set; } = new();
    }

    public class TargetSetting
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; } = "";

        [JsonPropertyName("target_type")]
        public string TargetType { get; set; } = "database";

        [JsonPropertyName("database_settings")]
        public DatabaseSettings? DatabaseSettings { get; set; }

        [JsonPropertyName("document_settings")]
        public DocumentSettings? DocumentSettings { get; set; }

        [JsonPropertyName("notification_settings")]
        public NotificationSettings? NotificationSettings { get; set; }

        [JsonPropertyName("automation_settings")]
        public AutomationSettings AutomationSettings { get; set; } = new();
    }

    // ── Field mapping: one column → one scan field ───────────────────────────
    public class FieldMapping
    {
        [JsonPropertyName("column_name")]
        public string ColumnName { get; set; } = "";

        [JsonPropertyName("match_as")]
        public string MatchAs { get; set; } = "";
    }

    // ── Data settings: table + id column + field mappings ───────────────────
    public class DataSettings
    {
        [JsonPropertyName("table_name")]
        public string TableName { get; set; } = "";

        [JsonPropertyName("id_column")]
        public string IdColumn { get; set; } = "";

        [JsonPropertyName("other_fields")]
        public List<FieldMapping> OtherFields { get; set; } = new();
    }

    public class DatabaseSettings
    {
        [JsonPropertyName("database_type")]
        public string DatabaseType { get; set; } = "";

        [JsonPropertyName("host")]
        public string Host { get; set; } = "";

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("database_name")]
        public string DatabaseName { get; set; } = "";

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = "";

        [JsonPropertyName("connection_string")]
        public string ConnectionString { get; set; } = "";

        [JsonPropertyName("password")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Password { get; set; }

        [JsonPropertyName("data_settings")]
        public DataSettings DataSettings { get; set; } = new();
    }

    // ── Document settings ────────────────────────────────────────────────────
    public class DocumentSettings
    {
        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("upload_path")]
        public string? UploadPath { get; set; }

        [JsonPropertyName("file_extension")]
        public string? FileExtension { get; set; }

        [JsonPropertyName("id_column")]
        public string IdColumn { get; set; } = "";

        [JsonPropertyName("other_fields")]
        public List<FieldMapping> OtherFields { get; set; } = new();
    }

    // ── Notification settings ────────────────────────────────────────────────
    public class NotificationSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("recipients")]
        public List<string> Recipients { get; set; } = new();

        [JsonPropertyName("notify_on_complete")]
        public bool NotifyOnComplete { get; set; }

        [JsonPropertyName("notify_on_match")]
        public bool NotifyOnMatch { get; set; }

        [JsonPropertyName("notify_on_error")]
        public bool NotifyOnError { get; set; }
    }

    public class AutomationSettings
    {
        [JsonPropertyName("automate")]
        public bool Automate { get; set; }

        [JsonPropertyName("frequency")]
        public string Frequency { get; set; } = "daily";

        [JsonPropertyName("interval_minutes")]
        public int IntervalMinutes { get; set; }

        [JsonPropertyName("interval_hours")]
        public int IntervalHours { get; set; }

        [JsonPropertyName("start_time")]
        public string StartTime { get; set; } = "02:00";

        [JsonPropertyName("weekday")]
        public int Weekday { get; set; }

        [JsonPropertyName("day_of_month")]
        public int DayOfMonth { get; set; }
    }

    // ── Upsert request DTO from the client ───────────────────────────────────
    public class UpsertTargetRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; } = string.Empty;

        [JsonPropertyName("target_type")]
        public string TargetType { get; set; } = "database";

        [JsonPropertyName("db_settings_changed")]
        public bool DbSettingsChanged { get; set; }

        [JsonPropertyName("database_settings")]
        public DatabaseSettingsRequest? DatabaseSettings { get; set; }

        [JsonPropertyName("document_settings")]
        public DocumentSettingsRequest? DocumentSettings { get; set; }

        [JsonPropertyName("notification_settings")]
        public NotificationSettingsRequest? NotificationSettings { get; set; }

        [JsonPropertyName("automation_settings")]
        public AutomationSettingsRequest? AutomationSettings { get; set; }
    }


    public class AutomationSettingsRequest
    {
        [JsonPropertyName("automate")] public bool Automate { get; set; }

        /// <summary>"minutely" | "hourly" | "daily" | "weekly" | "monthly"</summary>
        [JsonPropertyName("frequency")] public string Frequency { get; set; } = "daily";

        /// <summary>Used when frequency == "minutely". Number of minutes between runs.</summary>
        [JsonPropertyName("interval_minutes")] public int IntervalMinutes { get; set; }

        /// <summary>Used when frequency == "hourly". Number of hours between runs.</summary>
        [JsonPropertyName("interval_hours")] public int IntervalHours { get; set; }

        /// <summary>Used for daily / weekly / monthly schedules (HH:mm).</summary>
        [JsonPropertyName("start_time")] public string StartTime { get; set; } = "02:00";

        /// <summary>Day of week (0=Sunday … 6=Saturday). Used when frequency == "weekly".</summary>
        [JsonPropertyName("weekday")] public int Weekday { get; set; }

        /// <summary>Day of month (1–28). Used when frequency == "monthly".</summary>
        [JsonPropertyName("day_of_month")] public int DayOfMonth { get; set; }
    }



    public class TargetSettingEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("target_type")]
        public string? TargetType { get; set; }

        [JsonPropertyName("document_settings")]
        public TargetDocumentSettingsEntry? DocumentSettings { get; set; }
    }

    public class TargetDocumentSettingsEntry
    {
        [JsonPropertyName("upload_path")]
        public string? UploadPath { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }




    public class DatabaseSettingsRequest
    {
        [JsonPropertyName("database_type")] public string? DatabaseType { get; set; }
        [JsonPropertyName("host")] public string? Host { get; set; }
        [JsonPropertyName("port")] public int? Port { get; set; }
        [JsonPropertyName("database_name")] public string? DatabaseName { get; set; }
        [JsonPropertyName("user_name")] public string? UserName { get; set; }
        [JsonPropertyName("password")] public string? Password { get; set; }
        [JsonPropertyName("data_settings")] public DataSettingsRequest? DataSettings { get; set; }
    }

    public class DocumentSettingsRequest
    {
        /// <summary>Human-readable file name (no extension, no timestamp).</summary>
        [JsonPropertyName("file_name")] public string? FileName { get; set; }

        /// <summary>Full path on disk where the file was saved.</summary>
        [JsonPropertyName("upload_path")] public string? UploadPath { get; set; }

        /// <summary>e.g. "xlsx" or "xls"</summary>
        [JsonPropertyName("file_extension")] public string? FileExtension { get; set; }

        [JsonPropertyName("id_column")] public string? IdColumn { get; set; }

        [JsonPropertyName("other_fields")]
        public List<FieldMappingRequest>? OtherFields { get; set; }
    }

    public class DataSettingsRequest
    {
        [JsonPropertyName("table_name")] public string? TableName { get; set; }
        [JsonPropertyName("id_column")] public string? IdColumn { get; set; }
        [JsonPropertyName("other_fields")] public List<FieldMappingRequest>? OtherFields { get; set; }
    }

    public class FieldMappingRequest
    {
        [JsonPropertyName("column_name")] public string ColumnName { get; set; } = string.Empty;
        [JsonPropertyName("match_as")] public string MatchAs { get; set; } = string.Empty;
    }

    public class NotificationSettingsRequest
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("recipients")] public List<string>? Recipients { get; set; }
        [JsonPropertyName("notify_on_complete")] public bool NotifyOnComplete { get; set; }
        [JsonPropertyName("notify_on_match")] public bool NotifyOnMatch { get; set; }
        [JsonPropertyName("notify_on_error")] public bool NotifyOnError { get; set; }
    }





    // ══════════════════════════════════════════════════════════════════════════
    // RESULT WRAPPER
    // ══════════════════════════════════════════════════════════════════════════

    public class SettingsResult<T>
    {
        public T? Data { get; private set; }
        public bool Success { get; private set; }
        public string? Error { get; private set; }

        public static SettingsResult<T> Ok(T data) => new() { Data = data, Success = true };
        public static SettingsResult<T> Fail(string error) => new() { Success = false, Error = error };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SERVICE CLASS
    // ══════════════════════════════════════════════════════════════════════════

    public class UpSanctionSettingsService
    {
        private const string SettingsId = "mainsettings";

        private readonly AppDbContext _db;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public UpSanctionSettingsService(AppDbContext db)
        {
            _db = db;
        }

        // ── Fetch the settings row ────────────────────────────────────────────
        private async Task<(UpSanctionSetting? Row, string? Error)> GetRowAsync()
        {
            try
            {
                var row = await _db.UpSanctionSettings
                    .FirstOrDefaultAsync(s => s.SettingId == SettingsId);

                return row is null
                    ? (null, "Settings record 'mainsettings' was not found in the database.")
                    : (row, null);
            }
            catch (Exception ex)
            {
                return (null, $"Failed to load settings: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // MAPPING HELPERS  (Request DTOs  →  internal stored models)
        // ══════════════════════════════════════════════════════════════════════

        private static AutomationSettings MapAutomation(AutomationSettingsRequest? req)
        {
            if (req is null) return new AutomationSettings();
            return new AutomationSettings
            {
                Automate = req.Automate,
                Frequency = req.Frequency ?? "daily",
                IntervalMinutes = req.IntervalMinutes,
                IntervalHours = req.IntervalHours,
                StartTime = req.StartTime ?? "02:00",
                Weekday = req.Weekday,
                DayOfMonth = req.DayOfMonth
            };
        }

        private static NotificationSettings MapNotification(NotificationSettingsRequest? req)
        {
            if (req is null) return new NotificationSettings();
            return new NotificationSettings
            {
                Enabled = req.Enabled,
                Recipients = req.Recipients ?? new List<string>(),
                NotifyOnComplete = req.NotifyOnComplete,
                NotifyOnMatch = req.NotifyOnMatch,
                NotifyOnError = req.NotifyOnError
            };
        }

        private static DataSettings MapDataSettings(DataSettingsRequest? req)
        {
            if (req is null) return new DataSettings();
            return new DataSettings
            {
                TableName = req.TableName ?? "",
                IdColumn = req.IdColumn ?? "",
                OtherFields = req.OtherFields?
                    .Select(f => new FieldMapping { ColumnName = f.ColumnName, MatchAs = f.MatchAs })
                    .ToList() ?? new List<FieldMapping>()
            };
        }

        private static DocumentSettings MapDocumentSettings(DocumentSettingsRequest? req)
        {
            if (req is null) return new DocumentSettings();
            return new DocumentSettings
            {
                FileName = req.FileName,
                UploadPath = req.UploadPath,
                FileExtension = req.FileExtension,
                IdColumn = req.IdColumn ?? "",
                OtherFields = req.OtherFields?
                    .Select(f => new FieldMapping { ColumnName = f.ColumnName, MatchAs = f.MatchAs })
                    .ToList() ?? new List<FieldMapping>()
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        // CONNECTION STRING HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private static string BuildAndEncryptConnectionString(DatabaseSettingsRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Password))
                throw new InvalidOperationException("Password is required to build the connection string.");

            string plain = req.DatabaseType switch
            {
                "PostgreSQL" =>
                    $"Host={req.Host};Port={req.Port};Database={req.DatabaseName};Username={req.UserName};Password={req.Password};",

                "Oracle" =>
                    $"User Id={req.UserName};Password={req.Password};Data Source={req.Host}:{req.Port}/{req.DatabaseName};",

                _ => throw new InvalidOperationException($"Unsupported database type: '{req.DatabaseType}'.")
            };

            return Cryptor.Encrypt(plain, useHashing: true);
        }

        // ══════════════════════════════════════════════════════════════════════
        // READ
        // ══════════════════════════════════════════════════════════════════════

        public async Task<SettingsResult<List<string>>> GetAdverseMediaFilterAsync()
        {
            var (row, error) = await GetRowAsync();
            if (row is null) return SettingsResult<List<string>>.Fail(error!);

            try
            {
                var data = JsonSerializer.Deserialize<List<string>>(row.AdverseMediaFilter, _json)
                           ?? new List<string>();
                return SettingsResult<List<string>>.Ok(data);
            }
            catch (Exception ex)
            {
                return SettingsResult<List<string>>.Fail($"Failed to parse adverse media filter: {ex.Message}");
            }
        }

        public async Task<SettingsResult<List<TargetSetting>>> GetTargetSettingsAsync()
        {
            var (row, error) = await GetRowAsync();
            if (row is null) return SettingsResult<List<TargetSetting>>.Fail(error!);

            try
            {
                var data = JsonSerializer.Deserialize<List<TargetSetting>>(row.TargetSettings, _json)
                           ?? new List<TargetSetting>();
                return SettingsResult<List<TargetSetting>>.Ok(data);
            }
            catch (Exception ex)
            {
                return SettingsResult<List<TargetSetting>>.Fail($"Failed to parse target settings: {ex.Message}");
            }
        }

        public async Task<SettingsResult<ScanSettings>> GetScanSettingsAsync()
        {
            var (row, error) = await GetRowAsync();
            if (row is null) return SettingsResult<ScanSettings>.Fail(error!);

            try
            {
                var data = JsonSerializer.Deserialize<ScanSettings>(row.ScanSettings, _json)
                           ?? new ScanSettings();
                return SettingsResult<ScanSettings>.Ok(data);
            }
            catch (Exception ex)
            {
                return SettingsResult<ScanSettings>.Fail($"Failed to parse scan settings: {ex.Message}");
            }
        }

        public async Task<SettingsResult<(List<string> AdverseMedia, List<TargetSetting> Targets, ScanSettings ScanSettings)>>
            GetAllAsync()
        {
            var (row, error) = await GetRowAsync();
            if (row is null)
                return SettingsResult<(List<string>, List<TargetSetting>, ScanSettings)>.Fail(error!);

            try
            {
                var adverseMedia = JsonSerializer.Deserialize<List<string>>(row.AdverseMediaFilter, _json)
                                   ?? new List<string>();
                var targets = JsonSerializer.Deserialize<List<TargetSetting>>(row.TargetSettings, _json)
                                   ?? new List<TargetSetting>();
                var scan = JsonSerializer.Deserialize<ScanSettings>(row.ScanSettings, _json)
                                   ?? new ScanSettings();

                return SettingsResult<(List<string>, List<TargetSetting>, ScanSettings)>
                    .Ok((adverseMedia, targets, scan));
            }
            catch (Exception ex)
            {
                return SettingsResult<(List<string>, List<TargetSetting>, ScanSettings)>
                    .Fail($"Failed to parse settings: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // UPDATE
        // ══════════════════════════════════════════════════════════════════════

        public async Task<SettingsResult<bool>> UpdateAdverseMediaFilterAsync(List<string> keywords)
        {
            var (row, error) = await GetRowAsync();
            if (row is null) return SettingsResult<bool>.Fail(error!);

            try
            {
                row.AdverseMediaFilter = JsonSerializer.Serialize(keywords, _json);
                await _db.SaveChangesAsync();
                return SettingsResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return SettingsResult<bool>.Fail($"Failed to update adverse media filter: {ex.Message}");
            }
        }

        public async Task<SettingsResult<bool>> UpdateScanSettingsAsync(ScanSettings scan)
        {
            var (row, error) = await GetRowAsync();
            if (row is null) return SettingsResult<bool>.Fail(error!);

            try
            {
                row.ScanSettings = JsonSerializer.Serialize(scan, _json);
                await _db.SaveChangesAsync();
                return SettingsResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return SettingsResult<bool>.Fail($"Failed to update scan settings: {ex.Message}");
            }
        }

        public async Task<SettingsResult<bool>> UpdateTargetSettingsAsync(List<TargetSetting> targets)
        {
            var (row, error) = await GetRowAsync();
            if (row is null) return SettingsResult<bool>.Fail(error!);

            try
            {
                row.TargetSettings = JsonSerializer.Serialize(targets, _json);
                await _db.SaveChangesAsync();
                return SettingsResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return SettingsResult<bool>.Fail($"Failed to update target settings: {ex.Message}");
            }
        }

        public async Task<SettingsResult<bool>> UpsertTargetAsync(UpsertTargetRequest request)
        {
            var result = await GetTargetSettingsAsync();
            if (!result.Success) return SettingsResult<bool>.Fail(result.Error!);

            try
            {
                var targets = result.Data!;
                var existing = targets.FindIndex(t => t.Id == request.Id);

                TargetSetting target;

                if (existing >= 0)
                {
                    // ── Edit existing target ──────────────────────────────
                    target = targets[existing];
                    target.TargetName = request.TargetName;
                    target.AutomationSettings = MapAutomation(request.AutomationSettings);
                    target.NotificationSettings = MapNotification(request.NotificationSettings);

                    if (request.TargetType == "document")
                    {
                        target.TargetType = "document";
                        target.DocumentSettings = MapDocumentSettings(request.DocumentSettings);
                        target.DatabaseSettings = null;
                    }
                    else
                    {
                        target.TargetType = "database";
                        target.DocumentSettings = null;

                        if (request.DbSettingsChanged)
                        {
                            if (request.DatabaseSettings is null)
                                return SettingsResult<bool>.Fail("Database settings are required when db_settings_changed is true.");

                            target.DatabaseSettings = new DatabaseSettings
                            {
                                DatabaseType = request.DatabaseSettings.DatabaseType ?? "",
                                Host = request.DatabaseSettings.Host ?? "",
                                Port = request.DatabaseSettings.Port ?? 0,
                                DatabaseName = request.DatabaseSettings.DatabaseName ?? "",
                                UserName = request.DatabaseSettings.UserName ?? "",
                                ConnectionString = BuildAndEncryptConnectionString(request.DatabaseSettings),
                                DataSettings = MapDataSettings(request.DatabaseSettings.DataSettings)
                            };
                        }
                        else
                        {
                            // Credentials unchanged — keep existing connection string, update data settings only
                            if (target.DatabaseSettings is null)
                                target.DatabaseSettings = new DatabaseSettings();

                            if (request.DatabaseSettings?.DataSettings is not null)
                                target.DatabaseSettings.DataSettings = MapDataSettings(request.DatabaseSettings.DataSettings);
                        }
                    }

                    targets[existing] = target;
                }
                else
                {
                    // ── New target ────────────────────────────────────────
                    int newId = targets.Count > 0 ? targets.Max(t => t.Id) + 1 : 1;

                    target = new TargetSetting
                    {
                        Id = newId,
                        TargetName = request.TargetName,
                        TargetType = request.TargetType,
                        AutomationSettings = MapAutomation(request.AutomationSettings),
                        NotificationSettings = MapNotification(request.NotificationSettings)
                    };

                    if (request.TargetType == "document")
                    {
                        target.DocumentSettings = MapDocumentSettings(request.DocumentSettings);
                    }
                    else
                    {
                        if (request.DatabaseSettings is null)
                            return SettingsResult<bool>.Fail("Database settings are required for a new database target.");

                        target.DatabaseSettings = new DatabaseSettings
                        {
                            DatabaseType = request.DatabaseSettings.DatabaseType ?? "",
                            Host = request.DatabaseSettings.Host ?? "",
                            Port = request.DatabaseSettings.Port ?? 0,
                            DatabaseName = request.DatabaseSettings.DatabaseName ?? "",
                            UserName = request.DatabaseSettings.UserName ?? "",
                            ConnectionString = BuildAndEncryptConnectionString(request.DatabaseSettings),
                            DataSettings = MapDataSettings(request.DatabaseSettings.DataSettings)
                        };
                    }

                    targets.Add(target);
                }

                return await UpdateTargetSettingsAsync(targets);
            }
            catch (Exception ex)
            {
                return SettingsResult<bool>.Fail($"Failed to upsert target: {ex.Message}");
            }
        }

        // ── Delete a single target ────────────────────────────────────────────
        public async Task<SettingsResult<bool>> DeleteTargetAsync(int targetId)
        {
            var result = await GetTargetSettingsAsync();
            if (!result.Success) return SettingsResult<bool>.Fail(result.Error!);

            try
            {
                var targets = result.Data!;
                targets.RemoveAll(t => t.Id == targetId);
                return await UpdateTargetSettingsAsync(targets);
            }
            catch (Exception ex)
            {
                return SettingsResult<bool>.Fail($"Failed to delete target: {ex.Message}");
            }
        }
    }
}