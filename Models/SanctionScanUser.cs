using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Upsanctionscreener.Models
{
    [Table("sanction_scan_users", Schema = "public")]
    public class SanctionScanUser
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("first_name")]
        public string? FirstName { get; set; }

        [Column("last_name")]
        public string? LastName { get; set; }

        [Column("department")]
        public string? Department { get; set; }

        // because DB is text
        [Column("last_login_date")]
        public string? LastLoginDate { get; set; }

        [NotMapped]
        public string SafeLastLoginDate =>
            string.IsNullOrWhiteSpace(LastLoginDate) ? "Never" : LastLoginDate;

        [Column("profile_status")]
        public string? ProfileStatus { get; set; }

        [Column("role")]
        public string? Role { get; set; }

        [Required]
        [Column("email")]
        public string Email { get; set; }

        [Required]
        [Column("password")]
        public string Password { get; set; }

        // MUST match postgres timestamp
        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}