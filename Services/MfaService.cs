using OtpNet;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Services
{
    public interface IMfaService
    {
        Task<MfaRecord?> GetRecordAsync(int userId);
        Task<(string secret, string otpauthUri)> BeginEnrollmentAsync(int userId, string email);
        Task<bool> ConfirmEnrollmentAsync(int userId, string code);
        Task DisableAsync(int userId);
        Task<bool> ValidateCodeAsync(int userId, string code);
        Task<bool> UseRecoveryCodeAsync(int userId, string code);
    }

    public class MfaService : IMfaService
    {
        private readonly MfaJsonStore _store;
        private readonly string _issuer;

        public MfaService(MfaJsonStore store, IConfiguration config)
        {
            _store = store;
            _issuer = config["Mfa:Issuer"] ?? "UP Sanction Scan Portal";
        }

        public async Task<MfaRecord?> GetRecordAsync(int userId)
        {
            var all = await _store.LoadAllAsync();
            return all.TryGetValue(userId, out var rec) ? rec : null;
        }

        // Generates a new secret and stores it as *not yet enabled* until the
        // user proves they've scanned it correctly (ConfirmEnrollmentAsync).
        public async Task<(string secret, string otpauthUri)> BeginEnrollmentAsync(int userId, string email)
        {
            var secretBytes = KeyGeneration.GenerateRandomKey(20);
            var secret = Base32Encoding.ToString(secretBytes);

            var all = await _store.LoadAllAsync();
            all[userId] = new MfaRecord { UserId = userId, Secret = secret, Enabled = false, RecoveryCodes = new() };
            await _store.SaveAllAsync(all);

            var otpauthUri =
                $"otpauth://totp/{Uri.EscapeDataString(_issuer)}:{Uri.EscapeDataString(email)}" +
                $"?secret={secret}&issuer={Uri.EscapeDataString(_issuer)}&digits=6&period=30";

            return (secret, otpauthUri);
        }

        public async Task<bool> ConfirmEnrollmentAsync(int userId, string code)
        {
            var all = await _store.LoadAllAsync();
            if (!all.TryGetValue(userId, out var rec) || rec.Enabled) return false;

            var totp = new Totp(Base32Encoding.ToBytes(rec.Secret));
            if (!totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay))
                return false;

            rec.Enabled = true;
            rec.RecoveryCodes = GenerateRecoveryCodes();
            all[userId] = rec;
            await _store.SaveAllAsync(all);
            return true;
        }

        public async Task DisableAsync(int userId)
        {
            var all = await _store.LoadAllAsync();
            if (all.Remove(userId))
                await _store.SaveAllAsync(all);
        }

        public async Task<bool> ValidateCodeAsync(int userId, string code)
        {
            var all = await _store.LoadAllAsync();
            if (!all.TryGetValue(userId, out var rec) || !rec.Enabled) return false;

            var totp = new Totp(Base32Encoding.ToBytes(rec.Secret));
            return totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
        }

        public async Task<bool> UseRecoveryCodeAsync(int userId, string code)
        {
            var all = await _store.LoadAllAsync();
            if (!all.TryGetValue(userId, out var rec) || !rec.Enabled) return false;
            if (!rec.RecoveryCodes.Contains(code)) return false;

            rec.RecoveryCodes.Remove(code); // one-time use
            all[userId] = rec;
            await _store.SaveAllAsync(all);
            return true;
        }

        private static List<string> GenerateRecoveryCodes(int count = 8)
        {
            var codes = new List<string>();
            for (int i = 0; i < count; i++)
                codes.Add(Guid.NewGuid().ToString("N")[..10].ToUpperInvariant());
            return codes;
        }
    }
}
