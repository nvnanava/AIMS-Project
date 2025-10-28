using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

[Route("manage")]
public class ManageController : Controller
{
    [AllowAnonymous]
    [HttpGet("ping")]
    public IActionResult Ping() => Content("manage ok");

    private static readonly bool RedirectForbiddenToDashboard = false;

    // primary route: /manage/bulk-upload
    // alias route:   /manage/bulkupload
    [HttpGet("bulk-upload")]
    [HttpGet("bulkupload", Name = "Manage_BulkUpload_Alias")]
    [Authorize(Policy = "CanBulkUpload")]   // Admin/Manager only
    public IActionResult BulkUpload() => View();

    [AllowAnonymous]
    [HttpGet("bulk-upload-gateway")]
    public IActionResult BulkUploadGateway()
    {
        if (User.IsInRole("Admin") || User.IsInRole("Manager"))
            return RedirectToAction(nameof(BulkUpload));

        if (RedirectForbiddenToDashboard)
        {
            TempData["Toast"] = "You don’t have access to Bulk Upload.";
            return RedirectToAction("Index", "Home");
        }
        return StatusCode(403); // themed 403 via error pipeline
    }
}

