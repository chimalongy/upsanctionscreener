// UpSanctionSettingsService.cs  — full replacement
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
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

    // ── API Key ───────────────────────────────────────────────────────────────
    public class ApiKey
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";
    }

    // ── Scan Settings — now includes api_keys; handles legacy typo on read ───
    public class ScanSettings
    {
        // Canonical spelling going forward
        [JsonPropertyName("scan_threshold")]
        public int ScanThreshold { get; set; }

        // Written as "email_recipients" (correct spelling)
        [JsonPropertyName("email_recipients")]
        public List<string> EmailRecipients { get; set; } = new();

        // Legacy typo field — populated by a custom converter so old rows still
        // deserialise correctly; always null when we serialise (WhenWritingNull).
        [JsonPropertyName("email_recipents")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? EmailRecipientsLegacy { get; set; }

        [JsonPropertyName("api_keys")]
        public List<ApiKey> ApiKeys { get; set; } = new();

        // ── Merge helper ──────────────────────────────────────────────────────
        // After deserialisation, migrate the legacy field into the canonical one.
        public void NormaliseLegacyFields()
        {
            if (EmailRecipientsLegacy is { Count: > 0 } && EmailRecipients.Count == 0)
                EmailRecipients = EmailRecipientsLegacy;
            EmailRecipientsLegacy = null;
        }
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

    public class FieldMapping
    {
        [JsonPropertyName("column_name")]
        public string ColumnName { get; set; } = "";

        [JsonPropertyName("match_as")]
        public string MatchAs { get; set; } = "";
    }

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
        [JsonPropertyName("frequency")] public string Frequency { get; set; } = "daily";
        [JsonPropertyName("interval_minutes")] public int IntervalMinutes { get; set; }
        [JsonPropertyName("interval_hours")] public int IntervalHours { get; set; }
        [JsonPropertyName("start_time")] public string StartTime { get; set; } = "02:00";
        [JsonPropertyName("weekday")] public int Weekday { get; set; }
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
        [JsonPropertyName("file_name")] public string? FileName { get; set; }
        [JsonPropertyName("upload_path")] public string? UploadPath { get; set; }
        [JsonPropertyName("file_extension")] public string? FileExtension { get; set; }
        [JsonPropertyName("id_column")] public string? IdColumn { get; set; }
        [JsonPropertyName("other_fields")] public List<FieldMappingRequest>? OtherFields { get; set; }
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

    // ── API Key request DTOs ──────────────────────────────────────────────────
    public class CreateApiKeyRequest
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;
    }

    public class UpdateApiKeyStatusRequest
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
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

        // ── Generate a cryptographically random API key ───────────────────────
        private static string GenerateRawApiKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(32); // 256-bit
            return Convert.ToHexString(bytes).ToLowerInvariant(); // 64-char hex string
        }

        // ══════════════════════════════════════════════════════════════════════
        // MAPPING HELPERS
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

                // Migrate old typo field if present
                data.NormaliseLegacyFields();

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
                scan.NormaliseLegacyFields();

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
                // Guarantee the legacy field is never written back
                scan.EmailRecipientsLegacy = null;
                row.ScanSettings = JsonSerializer.Serialize(scan, _json);
                await _db.SaveChangesAsync();
                return SettingsResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return SettingsResult<bool>.Fail($"Failed to update scan settings: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // API KEY CRUD
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a new API key for <paramref name="clientId"/>.
        /// Returns the full updated <see cref="ScanSettings"/> on success so the
        /// caller can push the fresh state back to the frontend in one response.
        /// </summary>
        public async Task<SettingsResult<ScanSettings>> CreateApiKeyAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return SettingsResult<ScanSettings>.Fail("Client ID is required.");

            var scanResult = await GetScanSettingsAsync();
            if (!scanResult.Success) return SettingsResult<ScanSettings>.Fail(scanResult.Error!);

            var settings = scanResult.Data!;

            // Duplicate client_id guard
            if (settings.ApiKeys.Any(k => k.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase)))
                return SettingsResult<ScanSettings>.Fail($"An API key for client '{clientId}' already exists.");

            // Generate and encrypt a raw key
            var rawKey = GenerateRawApiKey();
            var encKey = Cryptor.Encrypt(rawKey, useHashing: true);

            int newId = settings.ApiKeys.Count > 0 ? settings.ApiKeys.Max(k => k.Id) + 1 : 1;

            settings.ApiKeys.Add(new ApiKey
            {
                Id = newId,
                ClientId = clientId,
                Key = encKey,
                Status = "active"
            });

            var saveResult = await UpdateScanSettingsAsync(settings);
            if (!saveResult.Success) return SettingsResult<ScanSettings>.Fail(saveResult.Error!);

            return SettingsResult<ScanSettings>.Ok(settings);
        }

        /// <summary>
        /// Deletes the API key with the given <paramref name="keyId"/>.
        /// Returns the full updated <see cref="ScanSettings"/> on success.
        /// </summary>
        public async Task<SettingsResult<ScanSettings>> DeleteApiKeyAsync(int keyId)
        {
            var scanResult = await GetScanSettingsAsync();
            if (!scanResult.Success) return SettingsResult<ScanSettings>.Fail(scanResult.Error!);

            var settings = scanResult.Data!;

            var existing = settings.ApiKeys.FirstOrDefault(k => k.Id == keyId);
            if (existing is null)
                return SettingsResult<ScanSettings>.Fail($"API key with ID {keyId} was not found.");

            settings.ApiKeys.Remove(existing);

            var saveResult = await UpdateScanSettingsAsync(settings);
            if (!saveResult.Success) return SettingsResult<ScanSettings>.Fail(saveResult.Error!);

            return SettingsResult<ScanSettings>.Ok(settings);
        }

        /// <summary>
        /// Updates the status of the API key with the given <paramref name="keyId"/>
        /// to either <c>"active"</c> or <c>"inactive"</c>.
        /// Returns the full updated <see cref="ScanSettings"/> on success.
        /// </summary>
        public async Task<SettingsResult<ScanSettings>> UpdateApiKeyStatusAsync(int keyId, string newStatus)
        {
            var allowed = new[] { "active", "inactive" };
            if (!allowed.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
                return SettingsResult<ScanSettings>.Fail("Status must be 'active' or 'inactive'.");

            var scanResult = await GetScanSettingsAsync();
            if (!scanResult.Success) return SettingsResult<ScanSettings>.Fail(scanResult.Error!);

            var settings = scanResult.Data!;

            var key = settings.ApiKeys.FirstOrDefault(k => k.Id == keyId);
            if (key is null)
                return SettingsResult<ScanSettings>.Fail($"API key with ID {keyId} was not found.");

            key.Status = newStatus.ToLowerInvariant();

            var saveResult = await UpdateScanSettingsAsync(settings);
            if (!saveResult.Success) return SettingsResult<ScanSettings>.Fail(saveResult.Error!);

            return SettingsResult<ScanSettings>.Ok(settings);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TARGET SETTINGS CRUD  (unchanged)
        // ══════════════════════════════════════════════════════════════════════

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