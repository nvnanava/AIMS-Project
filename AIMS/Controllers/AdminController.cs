using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers
{


    [Authorize(Policy = "mbcAdmin")] // Only users with the "Admin" role can access any action methods in this controller

    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }

}

