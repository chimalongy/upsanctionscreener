using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using System.Data;
using System.Text.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Utils
{
    public static class GlobalFunctions
    {
        // ── GET all users ─────────────────────────────────────────────────────
        public static async Task<IEnumerable<object>> GetAllUsersAsync(AppDbContext db)
        {
            return await db.SanctionScanUsers
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Department,
                    u.Role,
                    u.ProfileStatus,
                    u.LastLoginDate,
                    u.CreatedAt
                })
                .ToListAsync();
        }

        // ── CREATE user ───────────────────────────────────────────────────────
        /// <returns>(result object, error string) — error is null on success.</returns>
        public static async Task<(object? Result, string? Error)> CreateUserAsync(
            AppDbContext db,
            IConfiguration config,
            string firstName,
            string lastName,
            string email,
            string? department,
            string? role)
        {
            if (string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email))
            {
                return (null, "First name, last name, and email are required.");
            }

            bool emailExists = await db.SanctionScanUsers
                .AnyAsync(u => u.Email.ToLower() == email.ToLower());

            if (emailExists)
                return (null, "CONFLICT");

            var defaultPassword = config["NEW_PASSWORD"];
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(defaultPassword);

            var user = new SanctionScanUser
            {
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Email = email.Trim().ToLower(),
                Department = department?.Trim(),
                Role = role ?? "Regular User",
                ProfileStatus = "enabled",
                Password = hashedPassword,
                CreatedAt = DateTime.UtcNow
            };

            db.SanctionScanUsers.Add(user);
            await db.SaveChangesAsync();

            return (new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Department,
                user.Role,
                user.ProfileStatus,
                user.LastLoginDate,
                user.CreatedAt
            }, null);
        }

        // ── DELETE user ───────────────────────────────────────────────────────
        /// <returns>null on success, error string on failure.</returns>
        public static async Task<string?> DeleteUserAsync(AppDbContext db, int id)
        {
            var user = await db.SanctionScanUsers.FindAsync(id);
            if (user is null)
                return "NOT_FOUND";

            db.SanctionScanUsers.Remove(user);
            await db.SaveChangesAsync();
            return null;
        }

        // ── UPDATE user status ────────────────────────────────────────────────
        /// <returns>(result object, error string) — error is null on success.</returns>
        public static async Task<(object? Result, string? Error)> UpdateUserStatusAsync(
            AppDbContext db,
            int id,
            string? newStatus)
        {
            var allowed = new[] { "enabled", "disabled" };
            if (!allowed.Contains(newStatus?.ToLower()))
                return (null, "Status must be 'enabled' or 'disabled'.");

            var user = await db.SanctionScanUsers.FindAsync(id);
            if (user is null)
                return (null, "NOT_FOUND");

            user.ProfileStatus = newStatus!.ToLower();
            await db.SaveChangesAsync();

            return (new { user.Id, user.ProfileStatus }, null);
        }

        // ── GET all audit logs ────────────────────────────────────────────────
        public static async Task<IEnumerable<object>> GetAllAuditLogsAsync(AppDbContext db)
        {
            return await db.AuditLogs
                .OrderByDescending(l => l.EventDate)
                .Select(l => new
                {
                    l.Id,
                    l.UserId,
                    l.IpAddress,
                    l.Event,
                    l.EventDate,
                    l.PageUrl
                })
                .ToListAsync();
        }



        // ── GET main settings ─────────────────────────────────────────────────────
        public static async Task<UpSanctionSetting?> GetMainSettingsAsync(AppDbContext db)
        {
            return await db.UpSanctionSettings
                .FirstOrDefaultAsync(s => s.SettingId == "mainsettings");
        }










        ///////// READ UBOs

        public static List<UboEntry> FetchAllUBOs()
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "UBOs.json");

                if (!System.IO.File.Exists(filePath))
                    return new List<UboEntry>();

                var json = System.IO.File.ReadAllText(filePath);

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                List<UboEntry> entries =
                    JsonSerializer.Deserialize<List<UboEntry>>(json, serializerOptions)
                    ?? new List<UboEntry>();

                return entries;
            }
            catch
            {
                return new List<UboEntry>();
            }
        }


        public static List<PepEntry> FetchAllPeps()
        {
            try
            {
                var filePath = Path.Combine(GlobalVariables.root_folder, "Lists", "PEPs.json");

                if (!System.IO.File.Exists(filePath))
                    return new List<PepEntry>();

                var json = System.IO.File.ReadAllText(filePath);

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var entries =
                    JsonSerializer.Deserialize<List<PepEntry>>(json, serializerOptions)
                    ?? new List<PepEntry>();

                return entries;
            }
            catch
            {
                return new List<PepEntry>();
            }
        }

        public static string NormalizeString(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Trim and uppercase
            name = name.Trim().ToUpperInvariant();

            // Normalize spaces
            name = Regex.Replace(name, @"\s+", " ");

            // Remove unwanted characters
            name = Regex.Replace(name, @"[^A-Z0-9\s]", "");

            // Split safely for older .NET versions
            var parts = name
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(x => x)
                .ToArray();

            return string.Join(" ", parts);
        }
        public static DataTable NormaLizeNamesinColumn(DataTable dataTable, string columnname)
        {
            if (dataTable == null)
                throw new ArgumentNullException(nameof(dataTable));

            if (string.IsNullOrWhiteSpace(columnname))
                throw new ArgumentException("Column name cannot be empty.", nameof(columnname));

            if (!dataTable.Columns.Contains(columnname))
                throw new ArgumentException($"Column '{columnname}' does not exist in the DataTable.");

            foreach (DataRow row in dataTable.Rows)
            {
                if (row[columnname] != DBNull.Value)
                {
                    string original = row[columnname]?.ToString();

                    row[columnname] = NormalizeString(original);
                }
                else
                {
                    row[columnname] = string.Empty;
                }
            }

            return dataTable;
        }

        public static List<SanctionEntry> NormalizeSanctionListNames(List<SanctionEntry> sanctionList)
        {
            if (sanctionList == null || sanctionList.Count == 0)
                return sanctionList;

            foreach (var entry in sanctionList)
            {
                if (entry.Names == null || entry.Names.Count == 0)
                    continue;

                entry.Names = entry.Names
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => NormalizeString(n))
                    .ToList();
            }

            return sanctionList;
        }


        public static string GetFileFileExtension(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLower();

            return extension switch
            {
                ".csv" => "csv",
                ".txt" => "txt",
                ".xls" or ".xlsx" => "excel",
                _ => "unknown"
            };
        }



        public static TaskFileReadResult ReadTaskFile(
        string file_extension,
        string autoGenerateId,
        string file_path,
        string idColumnName,
        string ColumnToScan)
        {
            TaskFileReadResult result = new TaskFileReadResult
            {
                Success = false,
                Data = null,
                Error = null
            };

            // Normalize string -> bool (safe parsing)
            bool generateId = string.Equals(autoGenerateId, "true", StringComparison.OrdinalIgnoreCase);

            try
            {
                switch (file_extension.ToLower())
                {
                    case "csv":
                        {
                            CsvFileReader csv_reader = new CsvFileReader();
                            CsvScanResult csv_result =
                                csv_reader.ReadCsvFileFromPath(file_path, idColumnName, ColumnToScan, generateId);

                            if (csv_result.Success && csv_result.Data != null)
                            {
                                result.Success = true;
                                result.Data = csv_result.Data;
                            }
                            else
                            {
                                result.Error = csv_result.Error ?? "Failed to read CSV file.";
                            }

                            break;
                        }

                   
                    case "excel":
                        {
                            ExcelMultiSheetReader excel_reader = new ExcelMultiSheetReader();
                            ExcelReadResult excel_result =
                                excel_reader.ReadExcelFromPath(file_path, idColumnName, ColumnToScan, generateId);

                            if (excel_result.Success && excel_result.Data != null)
                            {
                                result.Success = true;
                                result.Data = excel_result.Data;
                            }
                            else
                            {
                                result.Error = excel_result.Error ?? "Failed to read Excel file.";
                            }

                            break;
                        }

                    case "txt":
                        {
                            DataTable table = new DataTable();

                            table.Columns.Add("ID", typeof(int));
                            table.Columns.Add("ScanItems", typeof(string));

                            if (!File.Exists(file_path))
                            {
                                result.Error = "File not found.";
                                return result;
                            }

                            var lines = File.ReadAllLines(file_path);
                            int id = 1;

                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    DataRow row = table.NewRow();

                                    row["ID"] = id++; // TXT still auto-generates always (your current logic)
                                    row["ScanItems"] = line.Trim();

                                    table.Rows.Add(row);
                                }
                            }

                            result.Success = true;
                            result.Data = table;

                            break;
                        }

                    default:
                        result.Error = $"Unsupported file type: {file_extension}";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        public static DataTable DeduplicateDatatbaleById(DataTable table, string idColumnName)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            if (!table.Columns.Contains(idColumnName))
                throw new ArgumentException($"Column '{idColumnName}' does not exist.");

            var distinctRows = table.AsEnumerable()
                                    .GroupBy(r => r[idColumnName])
                                    .Select(g => g.First());

            if (!distinctRows.Any())
                return table.Clone(); // empty structure

            return distinctRows.CopyToDataTable();
        }


    }
}