namespace Upsanctionscreener.Models
{
    public class MfaRecord
    {
        public int UserId { get; set; }
        public string Secret { get; set; } = "";
        public bool Enabled { get; set; }
        public List<string> RecoveryCodes { get; set; } = new();
    }
}
