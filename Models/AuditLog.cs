using System;
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
        public int? UserId { get; set; }

        [Column("ipaddress")]
        [StringLength(50)]
        public string IpAddress { get; set; }

        [Column("event")]
        public string Event { get; set; }

        [Column("eventdate")]
        public DateTime EventDate { get; set; } = DateTime.Now;

        [Column("pageurl")]
        public string PageUrl { get; set; }
    }
}