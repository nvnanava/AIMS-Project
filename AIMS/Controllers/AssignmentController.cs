using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

[Authorize(Policy = "mbcAdmin")]
[ApiController]
[Route("api/assign")]
public class AssignmentController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly AssignmentsQuery _assignQuery;
    private readonly AuditLogQuery _auditQuery;

    public AssignmentController(AimsDbContext db, AssignmentsQuery assignQuery, AuditLogQuery auditQuery)
    {
        _db = db;
        _assignQuery = assignQuery;
        _auditQuery = auditQuery;
    }

    [HttpPost("create")]
    [ProducesResponseType(typeof(CreateAssignmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentDto req, CancellationToken ct = default)
    {
        // exactly one side must be set
        bool bothNull = req.HardwareID == null && req.SoftwareID == null;
        bool bothVals = req.HardwareID != null && req.SoftwareID != null;
        if (bothNull || bothVals)
            return BadRequest("You must specify either HardwareID or SoftwareID (but not both).");

        // validate user
        var user = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserID == req.UserID, ct);
        if (user is null)
            return BadRequest($"No user with UserID {req.UserID} exists!");

        // validate asset
        Hardware? gotHardware = null;
        Software? gotSoftware = null;

        if (req.AssetKind == AssetKind.Hardware)
        {
            if (req.HardwareID is null)
                return BadRequest("For AssetKind=Hardware you must supply HardwareID.");

            gotHardware = await _db.HardwareAssets
                .SingleOrDefaultAsync(hw => hw.HardwareID == req.HardwareID.Value, ct);

            if (gotHardware is null)
                return BadRequest("Please specify a valid HardwareID.");
        }
        else if (req.AssetKind == AssetKind.Software)
        {
            if (req.SoftwareID is null)
                return BadRequest("For AssetKind=Software you must supply SoftwareID.");

            gotSoftware = await _db.SoftwareAssets
                .SingleOrDefaultAsync(sw => sw.SoftwareID == req.SoftwareID.Value, ct);

            if (gotSoftware is null)
                return BadRequest("Please specify a valid SoftwareID.");
        }
        else
        {
            return BadRequest("Unknown AssetKind.");
        }

        // prevent double-open assignment
        var assignmentExists = await _db.Assignments
            .Where(a => a.UnassignedAtUtc == null)
            .AnyAsync(a =>
                (req.SoftwareID != null && a.SoftwareID == req.SoftwareID) ||
                (req.HardwareID != null && a.HardwareID == req.HardwareID),
                ct);

        if (assignmentExists)
        {
            if (req.AssetKind == AssetKind.Hardware)
                return Conflict($"An open assignment for HardwareID {req.HardwareID} already exists!");
            return Conflict($"An open assignment for SoftwareID {req.SoftwareID} already exists!");
        }

        // create
        var newAssignment = new Assignment
        {
            UserID = req.UserID,
            AssetKind = req.AssetKind,
            HardwareID = req.AssetKind == AssetKind.Hardware ? req.HardwareID : null,
            SoftwareID = req.AssetKind == AssetKind.Software ? req.SoftwareID : null,
            AssignedAtUtc = DateTime.UtcNow,
            UnassignedAtUtc = null
        };

        _db.Assignments.Add(newAssignment);

        // reflect hardware status immediately
        if (gotHardware is not null)
            gotHardware.Status = "Assigned";

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        // audit (best-effort)
        try
        {
            var assetName = gotHardware?.AssetName ?? gotSoftware?.SoftwareName ?? "";
            var idText = req.AssetKind == AssetKind.Hardware
                ? $"HardwareID {req.HardwareID}"
                : $"SoftwareID {req.SoftwareID}";
            var description = $"Assigned {req.AssetKind} {assetName} ({idText}) to {user.FullName} ({user.UserID}).";

            await _auditQuery.CreateAuditRecordAsync(new CreateAuditRecordDto
            {
                UserID = req.UserID,
                Action = "Assign",
                Description = description,
                AssetKind = req.AssetKind,
                HardwareID = req.AssetKind == AssetKind.Hardware ? req.HardwareID : null,
                SoftwareID = req.AssetKind == AssetKind.Software ? req.SoftwareID : null
            });
        }
        catch { /* swallow; success not blocked by audit */ }

        return CreatedAtAction(nameof(GetAssignment), new { AssignmentID = newAssignment.AssignmentID }, req);
    }

    [HttpGet("get")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssignment([FromQuery] int AssignmentID, CancellationToken ct = default)
    {
        var dto = await _assignQuery.GetAssignmentAsync(AssignmentID, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAssignments([FromQuery] string status = "active", CancellationToken ct = default)
    {
        var rows = await _assignQuery.GetAllAssignmentsAsync(status, ct);
        return Ok(rows);
    }

    [HttpPost("close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Close([FromQuery] int AssignmentID, CancellationToken ct = default)
    {
        var assignment = await _db.Assignments
            .SingleOrDefaultAsync(a => a.AssignmentID == AssignmentID, ct);
        if (assignment is null)
            return NotFound("Please specify a valid AssignmentID");

        if (assignment.UnassignedAtUtc is null)
        {
            assignment.UnassignedAtUtc = DateTime.UtcNow;

            // free hardware status on close
            if (assignment.AssetKind == AssetKind.Hardware && assignment.HardwareID is int hid)
            {
                var hw = await _db.HardwareAssets.SingleOrDefaultAsync(h => h.HardwareID == hid, ct);
                if (hw != null) hw.Status = "Available";
            }

            await _db.SaveChangesAsync(ct);
            CacheStamp.BumpAssets();

            // audit (best-effort)
            try
            {
                var description = assignment.AssetKind == AssetKind.Hardware
                    ? $"Closed assignment {AssignmentID} for HardwareID {assignment.HardwareID}."
                    : $"Closed assignment {AssignmentID} for SoftwareID {assignment.SoftwareID}.";

                await _auditQuery.CreateAuditRecordAsync(new CreateAuditRecordDto
                {
                    UserID = assignment.UserID ?? 0,
                    Action = "CloseAssignment",
                    Description = description,
                    AssetKind = assignment.AssetKind,
                    HardwareID = assignment.HardwareID,
                    SoftwareID = assignment.SoftwareID
                });
            }
            catch { /* ignore */ }
        }
        return Ok();
    }

    // histories
    [HttpGet("user/{userId}/history")]
    public async Task<IActionResult> GetUserAssignments(int userId, CancellationToken ct = default)
    {
        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.UserID == userId, ct);
        if (!exists) return NotFound($"No user with UserID {userId} exists!");

        var history = await _db.Assignments
            .Where(a => a.UserID == userId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(history);
    }

    [HttpGet("software/{softwareId}/history")]
    public async Task<IActionResult> GetSoftwareHistory(int softwareId, CancellationToken ct = default)
    {
        var exists = await _db.SoftwareAssets.AsNoTracking().AnyAsync(sw => sw.SoftwareID == softwareId, ct);
        if (!exists) return NotFound($"No software with ID {softwareId} exists!");

        var history = await _db.Assignments
            .Where(a => a.SoftwareID == softwareId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(history);
    }

    [HttpGet("hardware/{hardwareId}/history")]
    public async Task<IActionResult> GetHardwareHistory(int hardwareId, CancellationToken ct = default)
    {
        var exists = await _db.HardwareAssets.AsNoTracking().AnyAsync(hw => hw.HardwareID == hardwareId, ct);
        if (!exists) return NotFound($"No hardware with ID {hardwareId} exists!");

        var history = await _db.Assignments
            .Where(a => a.HardwareID == hardwareId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(history);
    }
}
