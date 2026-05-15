using Microsoft.EntityFrameworkCore;
using Upsanctionscreener.Data;

namespace Upsanctionscreener.Classess.Utils
{
    public static class AuditLogger
    {
        public static async Task LogAsync(
            AppDbContext db,
            string eventName,
            int? userId = null,
            string? ipAddress = null,
            string? pageUrl = null)
        {
            try
            {
                int nextId = 1;
                if (await db.AuditLogs.AnyAsync())
                    nextId = await db.AuditLogs.MaxAsync(a => a.Id) + 1;

                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO public.audit_logs (id, event, eventdate, ipaddress, pageurl, userid)
                      VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                    nextId,
                    eventName,
                    DateTime.UtcNow,
                    ipAddress ?? (object)DBNull.Value,
                    pageUrl ?? (object)DBNull.Value,
                    userId.HasValue ? userId.Value.ToString() : (object)DBNull.Value
                );
            }
            catch (Exception ex)
            {
                // Never let audit log failure crash the main request
                Console.WriteLine($"[AuditLogger] Failed: {ex.Message}");
            }
        }
    }
}