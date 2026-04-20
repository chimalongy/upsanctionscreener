using Microsoft.AspNetCore.Mvc;

namespace Upsanctionscreener.Controllers
{
    public class AuthController : Controller
    {
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password))
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError("", "Invalid email or password.");
            return View();
        }

        public IActionResult Logout()
        {
            return RedirectToAction("Login", "Auth");
        }
    }
}