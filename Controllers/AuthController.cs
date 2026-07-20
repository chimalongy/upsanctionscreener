using DuoUniversal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;

namespace Upsanctionscreener.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly Client? _duoClient;
        private readonly bool _useDuo;

        public AuthController(AppDbContext db, IConfiguration config, Client? duoClient = null)
        {
            _db = db;
            _config = config;
            _duoClient = duoClient;
            _useDuo = config.GetValue<bool>("Duo:UseDuo");
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

                // ── MFA branch ──────────────────────────────────────────────────
                if (_useDuo && _duoClient is not null)
                {
                    await _duoClient.DoHealthCheck();

                    var state = Client.GenerateState();
                    HttpContext.Session.SetString("duo_state", state);
                    HttpContext.Session.SetInt32("duo_user_id", user.Id);

                    string authUrl = _duoClient.GenerateAuthUri(user.Email, state);
                    return Redirect(authUrl);
                }

                // ── Duo disabled — original direct sign-in path ─────────────────
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
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Login failed. Please try again.");
                return View();
            }
        }

        // ── GET /Auth/DuoCallback (only reachable if Duo is enabled) ───────────
        [HttpGet]
        public async Task<IActionResult> DuoCallback(string state, string duo_code)
        {
            if (!_useDuo || _duoClient is null)
                return RedirectToAction("Login");

            var savedState = HttpContext.Session.GetString("duo_state");
            var userId = HttpContext.Session.GetInt32("duo_user_id");

            if (string.IsNullOrEmpty(savedState) || userId is null || state != savedState)
            {
                ModelState.AddModelError("", "Your session expired. Please sign in again.");
                return RedirectToAction("Login");
            }

            var user = await _db.SanctionScanUsers.FindAsync(userId.Value);
            if (user is null)
                return RedirectToAction("Login");

            try
            {
                IdToken token = await _duoClient.ExchangeAuthorizationCodeFor2faResult(duo_code, user.Email);
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "Two-factor authentication failed. Please try again.");
                return RedirectToAction("Login");
            }

            HttpContext.Session.Remove("duo_state");
            HttpContext.Session.Remove("duo_user_id");

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











        // ── GET /Auth/UpdatePassword ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> UpdatePassword()
        {
            if (TempData["ForceChangeUserId"] is null)
                return RedirectToAction("Login");

            TempData.Keep("ForceChangeUserId");

            // Fetch email to display in the view (read-only)
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

            // Always re-populate email for the view on any error path
            ViewBag.UserEmail = user.Email;

            // ── Validate old password ─────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(oldPassword) || !BCrypt.Net.BCrypt.Verify(oldPassword, user.Password))
            {
                ModelState.AddModelError("", "Current password is incorrect.");
                return View();
            }

            // ── Validate new password ─────────────────────────────────────────
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

            // ── Prevent reusing the default/temporary password ────────────────
            var defaultPassword = _config["NEW_PASSWORD"];
            if (!string.IsNullOrEmpty(defaultPassword) && newPassword == defaultPassword)
            {
                ModelState.AddModelError("", "You cannot reuse the temporary password. Please choose a new one.");
                return View();
            }

            // ── Save ──────────────────────────────────────────────────────────
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

        // ── Private helper ────────────────────────────────────────────────────
        private async Task SignInUserAsync(Upsanctionscreener.Models.SanctionScanUser user)
        {
            // Generate JWT (stored as a claim so API callers can extract it too)
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
                new("jwt",                     jwt)  // carry the raw token too
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