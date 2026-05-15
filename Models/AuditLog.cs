using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Upsanctionscreener.Models
{
    [Table("audit_logs", Schema = "public")]
    public class AuditLog
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("userid")]
        [StringLength(100)]
        public string? UserId { get; set; }   // was int? — DB column is varchar(100)

        [Column("ipaddress")]
        [StringLength(100)]
        public string? IpAddress { get; set; }

        [Column("event")]
        public string? Event { get; set; }

        [Column("eventdate")]
        public DateTime EventDate { get; set; } = DateTime.UtcNow;

        [Column("pageurl")]
        public string? PageUrl { get; set; }
    }
}