using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

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










    }
}