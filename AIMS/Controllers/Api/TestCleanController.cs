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
    private readonly ILogger<TestCleanController> _logger;
    private readonly bool isAllowed;

    public TestCleanController(IWebHostEnvironment env, AimsDbContext db, ILogger<TestCleanController> logger)
    {
        _env = env;
        _db = db;
        _logger = logger;

        isAllowed = _env.IsDevelopment() || _env.IsEnvironment("Playwright");
    }

    // Clean out a user added during testing
    [HttpDelete("user")]
    public async Task<IActionResult> DeleteUser([FromQuery] string GraphObjectID)
    {
        // if not in the development environment, forbid this api route
        if (!isAllowed)
        {
            return Forbid();
        }
        _logger.LogInformation("Beginning user delete");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.GraphObjectID == GraphObjectID);

        if (user == null)
        {
            // User not found, which is a successful cleanup for us.
            return NoContent();
        }
        _logger.LogInformation($"{user.FullName}");
        // remove user from AuditLog (otherwise the key constraints will not allow us to delete a user)
        var auditLogMsgs = await _db.AuditLogs.Where(a => a.UserID == user.UserID).ToListAsync();
        _logger.LogInformation($"{auditLogMsgs.Count}");
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
        if (!isAllowed)
        {
            return Forbid();
        }

        var office = await _db.Offices.Where(o => o.OfficeName == OfficeName).FirstOrDefaultAsync();
        if (office == null)
        {
            // User not found, which is a successful cleanup for us.
            return NoContent();
        }

        _db.Offices.Remove(office);
        await _db.SaveChangesAsync();

        return Ok($"Deleted test office {office.OfficeID} ({office.OfficeName})");
    }
    [HttpDelete("reports")]
    public async Task<IActionResult> DeleteTestReports()
    {
        if (!isAllowed)
        {
            return Forbid();
        }

        var reports = await _db.Reports.Where(r => r.Name.Contains("e2e-test")).ToListAsync();

        foreach (var report in reports)
        {
            _db.Reports.Remove(report);
        }
        await _db.SaveChangesAsync();

        return Ok();
    }
}