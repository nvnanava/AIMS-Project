using AIMS.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/clean")]

// This controller is for cleaning up the database after tests (e.g., playwright).
// It is gated so that these routes are only available in the development environment
public class TestCleanController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly AimsDbContext _db;

    public TestCleanController(IWebHostEnvironment env, AimsDbContext db)
    {
        _env = env;
        _db = db;
    }

    // Clean out a user added during testing
    [HttpDelete("user")]
    public async Task<IActionResult> DeleteUser([FromQuery] string GraphObjectID)
    {
        // if not in the development environment, forbid this api route
        if (!_env.IsDevelopment())
        {
            return Forbid();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GraphObjectID == GraphObjectID);
        if (user == null) return NotFound();

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok($"Deleted test user {user.FullName} ({user.GraphObjectID})");
    }
}