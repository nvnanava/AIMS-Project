//FOR DEV ONLY - DELETE AFTER TESTING BEFORE MERGING PR 

using Microsoft.AspNetCore.Mvc;

public class TestController : Controller
{
    [HttpGet("/fetchtest")]
    public IActionResult FetchTest()
    {
        return View("~/Views/Shared/fetchTest.cshtml");
    }

    [HttpGet("/api/test/slow")]
    public async Task<IActionResult> SlowResponse()
    {
        // Simulate a slow endpoint (5 seconds delay)
        await Task.Delay(5000);
        return Json(new { message = "Delayed OK", timestamp = DateTime.UtcNow });
    }
}
