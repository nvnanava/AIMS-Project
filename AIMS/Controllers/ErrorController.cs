using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers
{
    [Route("error")]
    public class ErrorController : Controller
    {
        [HttpGet("not-found")]
        public IActionResult NotFoundPage()
        {
            Response.StatusCode = 404;
            return View("NotFound");
        }

        [HttpGet("not-authorized")]
        public IActionResult NotAuthorizedPage()
        {
            Response.StatusCode = 403;
            return View("NotAuthorized");
        }
    }
}
