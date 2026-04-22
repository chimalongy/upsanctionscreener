using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public string TargetType { get; set; } = "";

        [JsonPropertyName("database_settings")]
        public DatabaseSettings DatabaseSettings { get; set; } = new();

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

        // Stored value is always the ENCRYPTED connection string.
        // Never serialised back to JSON with a raw password inside.
        [JsonPropertyName("connection_string")]
        public string ConnectionString { get; set; } = "";

        // Transient — only populated when coming IN from the client (upsert payload).
        // Never stored; used solely to build + encrypt the connection string.
        [JsonPropertyName("password")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Password { get; set; }

        // ── NEW: table / column / field-mapping configuration ────────────────
        [JsonPropertyName("data_settings")]
        public DataSettings DataSettings { get; set; } = new();
    }

    public class AutomationSettings
    {
        [JsonPropertyName("automate")]
        public bool Automate { get; set; }

        [JsonPropertyName("frequency")]
        public string Frequency { get; set; } = "";

        [JsonPropertyName("start_time")]
        public string StartTime { get; set; } = "";

        [JsonPropertyName("weekday")]
        public int Weekday { get; set; }

        [JsonPropertyName("day_of_month")]
        public int DayOfMonth { get; set; }
    }

    // ── Upsert request DTO from the client ───────────────────────────────────
    // db_settings_changed = true  → build + encrypt new connection string
    // db_settings_changed = false → keep existing encrypted connection string
    //
    // In both cases database_settings is sent so that data_settings can always
    // be updated independently of the connection credentials.
    public class UpsertTargetRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; } = "";

        [JsonPropertyName("target_type")]
        public string TargetType { get; set; } = "";

        [JsonPropertyName("db_settings_changed")]
        public bool DbSettingsChanged { get; set; }

        // Always present — contains data_settings even when credentials unchanged.
        // Contains full credentials + data_settings when db_settings_changed = true.
        [JsonPropertyName("database_settings")]
        public DatabaseSettings? DatabaseSettings { get; set; }

        [JsonPropertyName("automation_settings")]
        public AutomationSettings AutomationSettings { get; set; } = new();
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
        // CONNECTION STRING HELPERS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a plain-text connection string for the given database type,
        /// then encrypts it using <see cref="Cryptor"/> before returning.
        /// The raw password is never stored anywhere after this call.
        /// </summary>
        private static string BuildAndEncryptConnectionString(DatabaseSettings db)
        {
            if (string.IsNullOrWhiteSpace(db.Password))
                throw new InvalidOperationException("Password is required to build the connection string.");

            string plain = db.DatabaseType switch
            {
                "PostgreSQL" =>
                    $"Host={db.Host};Port={db.Port};Database={db.DatabaseName};Username={db.UserName};Password={db.Password};",

                "Oracle" =>
                    $"User Id={db.UserName};Password={db.Password};Data Source={db.Host}:{db.Port}/{db.DatabaseName};",

                _ => throw new InvalidOperationException($"Unsupported database type: '{db.DatabaseType}'.")
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

        public async Task<SettingsResult<(List<string> AdverseMedia, List<TargetSetting> Targets, ScanSettings Scan)>>
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

        // ── Upsert a single target ────────────────────────────────────────────

        /// <summary>
        /// Inserts or updates a target from a client upsert request.
        /// <para>
        /// When <see cref="UpsertTargetRequest.DbSettingsChanged"/> is <c>true</c> the service
        /// builds the appropriate connection string from the supplied credentials, encrypts it
        /// with <see cref="Cryptor"/>, and stores only the encrypted value — the raw password
        /// is discarded immediately after encryption.  The incoming <c>data_settings</c> are
        /// also written as part of the new <see cref="DatabaseSettings"/> object.
        /// </para>
        /// <para>
        /// When <see cref="UpsertTargetRequest.DbSettingsChanged"/> is <c>false</c> the existing
        /// encrypted connection string is preserved untouched; only non-credential fields
        /// (target name, automation settings, and data_settings) are updated.
        /// </para>
        /// </summary>
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
                    target.AutomationSettings = request.AutomationSettings;

                    if (request.DbSettingsChanged)
                    {
                        // User explicitly changed DB credentials — build + encrypt new connection string
                        if (request.DatabaseSettings is null)
                            return SettingsResult<bool>.Fail("Database settings are required when db_settings_changed is true.");

                        var encryptedConnStr = BuildAndEncryptConnectionString(request.DatabaseSettings);

                        target.DatabaseSettings = new DatabaseSettings
                        {
                            DatabaseType = request.DatabaseSettings.DatabaseType,
                            Host = request.DatabaseSettings.Host,
                            Port = request.DatabaseSettings.Port,
                            DatabaseName = request.DatabaseSettings.DatabaseName,
                            UserName = request.DatabaseSettings.UserName,
                            ConnectionString = encryptedConnStr,
                            // Carry over data_settings from the request
                            DataSettings = request.DatabaseSettings.DataSettings ?? new DataSettings()
                            // Password intentionally NOT stored
                        };
                    }
                    else
                    {
                        // Credentials unchanged — keep existing encrypted connection string.
                        // Only update data_settings if the client sent them.
                        if (request.DatabaseSettings?.DataSettings is not null)
                        {
                            target.DatabaseSettings.DataSettings = request.DatabaseSettings.DataSettings;
                        }
                    }

                    targets[existing] = target;
                }
                else
                {
                    // ── New target ────────────────────────────────────────
                    if (request.DatabaseSettings is null)
                        return SettingsResult<bool>.Fail("Database settings are required for a new target.");

                    var encryptedConnStr = BuildAndEncryptConnectionString(request.DatabaseSettings);

                    target = new TargetSetting
                    {
                        Id = targets.Count > 0 ? targets.Max(t => t.Id) + 1 : 1,
                        TargetName = request.TargetName,
                        TargetType = request.TargetType,
                        DatabaseSettings = new DatabaseSettings
                        {
                            DatabaseType = request.DatabaseSettings.DatabaseType,
                            Host = request.DatabaseSettings.Host,
                            Port = request.DatabaseSettings.Port,
                            DatabaseName = request.DatabaseSettings.DatabaseName,
                            UserName = request.DatabaseSettings.UserName,
                            ConnectionString = encryptedConnStr,
                            DataSettings = request.DatabaseSettings.DataSettings ?? new DataSettings()
                            // Password intentionally NOT stored
                        },
                        AutomationSettings = request.AutomationSettings
                    };

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