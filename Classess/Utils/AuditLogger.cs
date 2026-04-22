using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models;

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
            var log = new AuditLog
            {
                Event = eventName,
                UserId = userId,
                IpAddress = ipAddress,
                PageUrl = pageUrl,
                EventDate = DateTime.UtcNow
            };

            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();
        }
    }
}
