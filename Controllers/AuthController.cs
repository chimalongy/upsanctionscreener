using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Services;

namespace Upsanctionscreener.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IMfaService _mfaService;
        private readonly bool _mfaEnabled;

        public AuthController(AppDbContext db, IConfiguration config, IMfaService mfaService)
        {
            _db = db;
            _config = config;
            _mfaService = mfaService;
            _mfaEnabled = config.GetValue<bool>("Mfa:Enabled");
        }

        // ── GET /Auth/Login ───────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    ModelState.AddModelError("", "Email and password are required.");
                    return View();
                }

                var user = await _db.SanctionScanUsers
                    .FirstOrDefaultAsync(u => u.Email == email.Trim().ToLower());

                if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
                {
                    ModelState.AddModelError("", "Invalid email or password.");
                    return View();
                }

                if (!string.Equals(user.ProfileStatus, "enabled", StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError("", "Your account has been disabled. Please contact your administrator.");
                    return View();
                }

                var defaultPassword = _config["NEW_PASSWORD"];
                if (!string.IsNullOrEmpty(defaultPassword) &&
                    BCrypt.Net.BCrypt.Verify(defaultPassword, user.Password))
                {
                    TempData["ForceChangeUserId"] = user.Id;
                    return RedirectToAction("UpdatePassword");
                }

                // ── MFA branch: enforced for every user ──────────────────────────
                if (_mfaEnabled)
                {
                    HttpContext.Session.SetInt32("mfa_user_id", user.Id);

                    var mfaRecord = await _mfaService.GetRecordAsync(user.Id);
                    if (mfaRecord is { Enabled: true })
                    {
                        // Already enrolled -> ask for a code.
                        return RedirectToAction("MfaVerify");
                    }

                    // Not enrolled yet -> force enrollment before they can sign in.
                    return RedirectToAction("MfaSetup");
                }

                // ── Direct sign-in path (only reachable when MFA is globally off) ─
                await SignInUserAsync(user);

                user.LastLoginDate = DateTime.UtcNow.ToString();
                await _db.SaveChangesAsync();
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{email} - LOGIN SUCESSFULL",
                    userId: user.Id,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );

                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "Login failed. Please try again.");
                return View();
            }
        }

        // ── GET /Auth/MfaVerify ────────────────────────────────────────────────
        [HttpGet]
        public IActionResult MfaVerify()
        {
            if (HttpContext.Session.GetInt32("mfa_user_id") is null)
                return RedirectToAction("Login");

            return View();
        }

        // ── POST /Auth/MfaVerify ───────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MfaVerify(string code)
        {
            var userId = HttpContext.Session.GetInt32("mfa_user_id");
            if (userId is null)
                return RedirectToAction("Login");

            var user = await _db.SanctionScanUsers.FindAsync(userId.Value);
            if (user is null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(code))
            {
                ModelState.AddModelError("", "Please enter your authentication code.");
                return View();
            }

            var isValid = await _mfaService.ValidateCodeAsync(userId.Value, code.Trim())
                       || await _mfaService.UseRecoveryCodeAsync(userId.Value, code.Trim().ToUpperInvariant());

            if (!isValid)
            {
                ModelState.AddModelError("", "Invalid or expired code. Please try again.");
                return View();
            }

            HttpContext.Session.Remove("mfa_user_id");

            await SignInUserAsync(user);

            user.LastLoginDate = DateTime.UtcNow.ToString();
            await _db.SaveChangesAsync();
            await AuditLogger.LogAsync(
                db: _db,
                eventName: $"{user.Email} - LOGIN SUCESSFULL (MFA)",
                userId: user.Id,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            return RedirectToAction("Index", "Dashboard");
        }

        // ── GET /Auth/MfaSetup ──────────────────────────────────────────────────
        // Reachable two ways:
        //   1. Authenticated user visiting their account settings to enroll/manage MFA.
        //   2. Unauthenticated user mid-login, forced here because they haven't
        //      enrolled yet (session "mfa_user_id" set by Login()).
        [HttpGet]
        public async Task<IActionResult> MfaSetup()
        {
            var userId = ResolveMfaUserId();
            if (userId is null)
                return RedirectToAction("Login");

            var record = await _mfaService.GetRecordAsync(userId.Value);

            if (record is { Enabled: true })
            {
                ViewBag.AlreadyEnabled = true;
                return View();
            }

            var user = await _db.SanctionScanUsers.FindAsync(userId.Value);
            if (user is null)
                return RedirectToAction("Login");

            var (secret, otpauthUri) = await _mfaService.BeginEnrollmentAsync(userId.Value, user.Email);

            ViewBag.Secret = secret;
            ViewBag.OtpauthUri = otpauthUri;
            return View();
        }

        // ── POST /Auth/MfaSetup (confirm the 6-digit code to finish enrollment) ─
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MfaSetup(string code)
        {
            var userId = ResolveMfaUserId();
            if (userId is null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(code) || !await _mfaService.ConfirmEnrollmentAsync(userId.Value, code.Trim()))
            {
                ModelState.AddModelError("", "That code didn't match. Please scan the QR code again and try the newest code.");
                var record = await _mfaService.GetRecordAsync(userId.Value);
                ViewBag.Secret = record?.Secret;
                return View();
            }

            var confirmed = await _mfaService.GetRecordAsync(userId.Value);

            // If this was a forced pre-login enrollment (not already authenticated),
            // sign the user in now that enrollment is complete.
            if (User.Identity?.IsAuthenticated != true)
            {
                var user = await _db.SanctionScanUsers.FindAsync(userId.Value);
                if (user is null)
                    return RedirectToAction("Login");

                HttpContext.Session.Remove("mfa_user_id");
                await SignInUserAsync(user);

                user.LastLoginDate = DateTime.UtcNow.ToString();
                await _db.SaveChangesAsync();
                await AuditLogger.LogAsync(
                    db: _db,
                    eventName: $"{user.Email} - LOGIN SUCESSFULL (MFA ENROLLED)",
                    userId: user.Id,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    pageUrl: HttpContext.Request.Path
                );
            }

            ViewBag.RecoveryCodes = confirmed?.RecoveryCodes;
            ViewBag.JustEnabled = true;
            return View();
        }

        // ── POST /Auth/MfaDisable ───────────────────────────────────────────────
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MfaDisable()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _mfaService.DisableAsync(userId);
            return RedirectToAction("MfaSetup");
        }

        // ── GET /Auth/UpdatePassword ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> UpdatePassword()
        {
            if (TempData["ForceChangeUserId"] is null)
                return RedirectToAction("Login");

            TempData.Keep("ForceChangeUserId");

            var userId = (int)TempData.Peek("ForceChangeUserId")!;
            var user = await _db.SanctionScanUsers.FindAsync(userId);

            if (user is null)
                return RedirectToAction("Login");

            ViewBag.UserEmail = user.Email;
            return View();
        }

        // ── POST /Auth/UpdatePassword ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePassword(string oldPassword, string newPassword, string confirmPassword)
        {
            if (TempData["ForceChangeUserId"] is not int userId)
                return RedirectToAction("Login");

            TempData["ForceChangeUserId"] = userId;

            var user = await _db.SanctionScanUsers.FindAsync(userId);
            if (user is null)
                return RedirectToAction("Login");

            ViewBag.UserEmail = user.Email;

            if (string.IsNullOrWhiteSpace(oldPassword) || !BCrypt.Net.BCrypt.Verify(oldPassword, user.Password))
            {
                ModelState.AddModelError("", "Current password is incorrect.");
                return View();
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                ModelState.AddModelError("", "New password must be at least 8 characters.");
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                return View();
            }

            var defaultPassword = _config["NEW_PASSWORD"];
            if (!string.IsNullOrEmpty(defaultPassword) && newPassword == defaultPassword)
            {
                ModelState.AddModelError("", "You cannot reuse the temporary password. Please choose a new one.");
                return View();
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _db.SaveChangesAsync();

            await AuditLogger.LogAsync(
                db: _db,
                eventName: "PASSWORD UPDATED",
                userId: user.Id,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                pageUrl: HttpContext.Request.Path
            );

            TempData["SuccessMessage"] = "Password updated successfully. Please sign in with your new password.";
            return RedirectToAction("Login");
        }

        // ── GET /Auth/Logout ──────────────────────────────────────────────────
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ── Private helpers ────────────────────────────────────────────────────

        // Resolves which user's MFA record MfaSetup should act on:
        // an already-authenticated user managing their own settings takes priority,
        // otherwise fall back to the pending-login session id set in Login().
        private int? ResolveMfaUserId()
        {
            if (User.Identity?.IsAuthenticated == true)
                return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            return HttpContext.Session.GetInt32("mfa_user_id");
        }

        private async Task SignInUserAsync(Upsanctionscreener.Models.SanctionScanUser user)
        {
            var jwt = JwtService.GenerateToken(user, _config);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name,           $"{user.FirstName} {user.LastName}"),
                new(ClaimTypes.Email,          user.Email),
                new(ClaimTypes.Role,           user.Role ?? "Regular User"),
                new("department",              user.Department ?? ""),
                new("firstName",               user.FirstName),
                new("lastName",                user.LastName),
                new("profileStatus",           user.ProfileStatus ?? "enabled"),
                new("jwt",                     jwt)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });
        }
    }
}