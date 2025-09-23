using Microsoft.AspNetCore.Mvc;
namespace AIMS.Controllers {
  [Route("error")]
  public class ErrorController : Controller {
    [HttpGet("{code:int}")]
    public IActionResult ByCode(int code) =>
      code switch {
        403 => RedirectToAction(nameof(NotAuthorized)),
        404 => RedirectToAction(nameof(NotFoundPage)),
        _   => View("Generic", code)
      };
    [HttpGet("not-found")] public IActionResult NotFoundPage(){ Response.StatusCode = 404; return View("NotFound"); }
    [HttpGet("not-authorized")] public IActionResult NotAuthorized(){ Response.StatusCode = 403; return View("NotAuthorized"); }
  }
}
