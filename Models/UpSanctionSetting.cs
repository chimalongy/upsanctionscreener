using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Upsanctionscreener.Models
{
    [Table("upsanction_settings", Schema = "public")]
    public class UpSanctionSetting
    {
        [Key]
        [Column("settingid")]
        public string SettingId { get; set; }

        [Column("adverse_media_filter")]
        public string AdverseMediaFilter { get; set; }

        [Column("target_settings")]
        public string TargetSettings { get; set; }

        [Column("scan_settings")]
        public string ScanSettings { get; set; }
    }
}