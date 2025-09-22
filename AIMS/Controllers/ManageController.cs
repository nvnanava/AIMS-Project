using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers
{
    [Route("manage")]
    public class ManageController : Controller
    {
        [HttpGet("bulk-upload")]
        public IActionResult BulkUpload()
        {
            // Replace with your real check; example: User.IsInRole("Admin")
            bool isAdmin = User.IsInRole("Admin");

            if (!isAdmin)
            {
                TempData["ToastError"] = "You don’t have access to Bulk Upload.";
                return RedirectToAction("Index", "Home");
            }

            return View();
        }
    }
}
