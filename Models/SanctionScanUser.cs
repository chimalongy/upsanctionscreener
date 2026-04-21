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
        [StringLength(100)]
        public string FirstName { get; set; }

        [Column("last_name")]
        [StringLength(100)]
        public string LastName { get; set; }

        [Column("department")]
        [StringLength(150)]
        public string Department { get; set; }

        [Column("last_login_date")]
        public DateTime? LastLoginDate { get; set; }

        [Column("profile_status")]
        [StringLength(50)]
        public string ProfileStatus { get; set; }

        [Column("role")]
        [StringLength(50)]
        public string Role { get; set; }

        [Required]
        [Column("email")]
        [StringLength(255)]
        public string Email { get; set; }

        [Required]
        [Column("password")]
        public string Password { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}