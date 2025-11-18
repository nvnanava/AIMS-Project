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
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Playwright"))
        {
            return Forbid();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GraphObjectID == GraphObjectID);
        if (user == null)
        {
            // User not found, which is a successful cleanup for us.
            return NoContent();
        }

        // remove user from AuditLog (otherwise the key constraints will not allow us to delete a user)
        var auditLogMsgs = await _db.AuditLogs.Where(a => a.UserID == user.UserID).ToListAsync();

        foreach (var msg in auditLogMsgs)
        {
            _db.AuditLogs.Remove(msg);
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok($"Deleted test user {user.FullName} ({user.GraphObjectID})");
    }

    [HttpDelete("office")]
    public async Task<IActionResult> DeleteOffice([FromQuery] string OfficeName)
    {
        // if not in the development environment, forbid this api route
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Playwright"))
        {
            return Forbid();
        }

        var office = await _db.Offices.FirstOrDefaultAsync(o => o.OfficeName == OfficeName);
        if (office == null)
        {
            // User not found, which is a successful cleanup for us.
            return NoContent();
        }

        _db.Offices.Remove(office);
        await _db.SaveChangesAsync();

        return Ok($"Deleted test office {office.OfficeID} ({office.OfficeName})");
    }
}