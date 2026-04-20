using Microsoft.AspNetCore.Mvc;

namespace Upsanctionscreener.Controllers
{
    public class DashboardController : Controller
    {
        // ── Search ──
        public IActionResult Index()
        {
            return View();
        }

        // ── Multi-Scan ──
        [Route("Dashboard/MultiScan")]
        public IActionResult MultiScan()
        {
            return View("~/Views/Dashboard/MultiScan.cshtml");
        }

        // ── Lists ──
        [Route("Dashboard/List/UBOs")]
        public IActionResult UBOs()
        {
            return View("~/Views/Dashboard/List/UBOs.cshtml");
        }

        [Route("Dashboard/List/PEPs")]
        public IActionResult PEPs()
        {
            return View("~/Views/Dashboard/List/PEPs.cshtml");
        }

        [Route("Dashboard/List/NigerianSanctionList")]
        public IActionResult NigerianSanctionList()
        {
            return View("~/Views/Dashboard/List/NigerianSanctionList.cshtml");
        }

        // ── Reports ──
        [Route("Dashboard/Reports/MerchantScanReports")]
        public IActionResult MerchantScanReports()
        {
            return View("~/Views/Dashboard/Reports/MerchantScanReports.cshtml");
        }

        [Route("Dashboard/Reports/CustomerReports")]
        public IActionResult CustomerReports()
        {
            return View("~/Views/Dashboard/Reports/CustomerReports.cshtml");
        }

        // ── Settings ──
        [Route("Dashboard/Settings/ScanSettings")]
        public IActionResult ScanSettings()
        {
            return View("~/Views/Dashboard/Settings/ScanSettings.cshtml");
        }

        [Route("Dashboard/Settings/Users")]
        public IActionResult Users()
        {
            return View("~/Views/Dashboard/Settings/Users.cshtml");
        }

        [Route("Dashboard/Settings/TargetSettings")]
        public IActionResult TargetSettings()
        {
            return View("~/Views/Dashboard/Settings/TargetSettings.cshtml");
        }

        [Route("Dashboard/Settings/SourceSettings")]
        public IActionResult SourceSettings()
        {
            return View("~/Views/Dashboard/Settings/SourceSettings.cshtml");
        }

        [Route("Dashboard/Settings/AuditLogs")]
        public IActionResult AuditLogs()
        {
            return View("~/Views/Dashboard/Settings/AuditLogs.cshtml");
        }
    }
}