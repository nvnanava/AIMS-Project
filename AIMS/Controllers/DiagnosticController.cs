using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;

namespace AIMS.Controllers;

[ApiController]
[Route("api/diag")]
public class DiagnosticsController : ControllerBase
{
    private readonly AimsDbContext _db;
    public DiagnosticsController(AimsDbContext db) => _db = db;

    // Quick sanity: table counts
    [HttpGet("db-summary")]
    public async Task<IActionResult> GetDbSummary()
    {
        var summary = new
        {
            Users = await _db.Users.CountAsync(),
            Roles = await _db.Roles.CountAsync(),
            Hardware = await _db.HardwareAssets.CountAsync(),
            Software = await _db.SoftwareAssets.CountAsync(),
            Assignments = await _db.Assignments.CountAsync(),
            Feedback = await _db.FeedbackEntries.CountAsync(),
            AuditLogs = await _db.AuditLogs.CountAsync(),
            LatestAssignment = await _db.Assignments
                .AsNoTracking()
                .OrderByDescending(a => a.AssignedAtUtc)
                .Select(a => new
                {
                    a.AssignmentID,
                    a.UserID,
                    User = a.User.FullName,
                    a.AssetKind,
                    a.AssetTag,
                    a.SoftwareID,
                    a.AssignedAtUtc,
                    a.UnassignedAtUtc
                })
                .FirstOrDefaultAsync()
        };
        return Ok(summary);
    }

    // Show active assignments with who/what
    [HttpGet("active-assignments")]
    public async Task<IActionResult> GetActiveAssignments()
    {
        var rows = await _db.Assignments
            .AsNoTracking()
            .Where(a => a.UnassignedAtUtc == null)
            .Select(a => new
            {
                a.AssignmentID,
                a.AssetKind,
                a.UserID,
                User = a.User.FullName,
                HardwareID = a.AssetTag,
                SoftwareID = a.SoftwareID,
                a.AssignedAtUtc
            })
            .ToListAsync();

        return Ok(rows);
    }

    // Inspect supervisors/direct reports (role visibility smoke test)
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Select(u => new {
                u.UserID,
                u.FullName,
                u.Email,
                u.EmployeeNumber,
                Role = u.Role.RoleName,
                u.SupervisorID
            })
            .ToListAsync();
        return Ok(users);
    }
}