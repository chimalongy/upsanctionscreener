using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Models.ViewModels;


namespace Upsanctionscreener.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public DashboardController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        // ══════════════════════════════════════════════════════════════════════
        // VIEWS
        // ══════════════════════════════════════════════════════════════════════

        public IActionResult Index() => View();

        [Route("Dashboard/MultiScan")]
        public IActionResult MultiScan() =>
            View("~/Views/Dashboard/MultiScan.cshtml");

        [Route("Dashboard/List/UBOs")]
        public IActionResult UBOs() =>
            View("~/Views/Dashboard/List/UBOs.cshtml");

        [Route("Dashboard/List/PEPs")]
        public IActionResult PEPs() =>
            View("~/Views/Dashboard/List/PEPs.cshtml");

        [Route("Dashboard/List/NigerianSanctionList")]
        public IActionResult NigerianSanctionList() =>
            View("~/Views/Dashboard/List/NigerianSanctionList.cshtml");

        [Route("Dashboard/Reports/MerchantScanReports")]
        public IActionResult MerchantScanReports() =>
            View("~/Views/Dashboard/Reports/MerchantScanReports.cshtml");

        [Route("Dashboard/Reports/CustomerReports")]
        public IActionResult CustomerReports() =>
            View("~/Views/Dashboard/Reports/CustomerReports.cshtml");

        [Route("Dashboard/Settings/ScanSettings")]
        public IActionResult ScanSettings() =>
            View("~/Views/Dashboard/Settings/ScanSettings.cshtml");

        [Route("Dashboard/Settings/Users")]
        public IActionResult Users() =>
            View("~/Views/Dashboard/Settings/Users.cshtml");

        [Route("Dashboard/Settings/TargetSettings")]
        public IActionResult TargetSettings() =>
            View("~/Views/Dashboard/Settings/TargetSettings.cshtml");

        [Route("Dashboard/Settings/SourceSettings")]
        public IActionResult SourceSettings() =>
            View("~/Views/Dashboard/Settings/SourceSettings.cshtml");

        [Route("Dashboard/Settings/AuditLogs")]
        public IActionResult AuditLogs() =>
            View("~/Views/Dashboard/Settings/AuditLogs.cshtml");


        // ══════════════════════════════════════════════════════════════════════
        // USERS API  ── controller only routes; logic lives in GlobalFunctions
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        [Route("Dashboard/Settings/Users/GetAll")]
        public async Task<IActionResult> UsersGetAll()
        {
            var users = await GlobalFunctions.GetAllUsersAsync(_db);
            return Json(users);
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/Create")]
        public async Task<IActionResult> UsersCreate([FromBody] CreateUserRequest req)
        {
            var (result, error) = await GlobalFunctions.CreateUserAsync(
                _db, _config,
                req.FirstName, req.LastName, req.Email,
                req.Department, req.Role);

            if (error == "CONFLICT")
                return Conflict(new { message = "A user with this email already exists." });

            if (error is not null)
                return BadRequest(new { message = error });

            return Json(result);
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/Delete/{id:int}")]
        public async Task<IActionResult> UsersDelete(int id)
        {
            var error = await GlobalFunctions.DeleteUserAsync(_db, id);

            if (error == "NOT_FOUND")
                return NotFound(new { message = "User not found." });

            return Json(new { message = "User deleted." });
        }

        [HttpPost]
        [Route("Dashboard/Settings/Users/UpdateStatus/{id:int}")]
        public async Task<IActionResult> UsersUpdateStatus(int id, [FromBody] UpdateStatusRequest req)
        {
            var (result, error) = await GlobalFunctions.UpdateUserStatusAsync(_db, id, req.ProfileStatus);

            if (error == "NOT_FOUND")
                return NotFound(new { message = "User not found." });

            if (error is not null)
                return BadRequest(new { message = error });

            return Json(result);
        }
    }


   
}