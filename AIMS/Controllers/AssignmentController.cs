using System.Linq;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;   // CacheStamp
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

// With EntraID wired, we gate via policy configured in Program.cs:
[Authorize(Policy = "mbcAdmin")]
[ApiController]
[Route("api/assign")]
public class AssignmentController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly AssignmentsQuery _assignQuery;
    private readonly AuditLogQuery _auditQuery;

    public AssignmentController(
        AimsDbContext db,
        AssignmentsQuery assignQuery,
        AuditLogQuery auditQuery)
    {
        _db = db;
        _assignQuery = assignQuery;
        _auditQuery = auditQuery;
    }

    // Quick sanity: table counts
    // POST /api/assign/create
    [HttpPost("create")]
    [ProducesResponseType(typeof(CreateAssignmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentDto req, CancellationToken ct = default)
    {
        // short-circuit: reject if request is malformed
        // make sure we get one of the identifiers for an asset
        bool bothNull = req.AssetTag == null && req.SoftwareID == null;
        bool bothValues = req.SoftwareID != null && req.AssetTag != null;
        if (bothNull || bothValues)
        {
            // for correct methods corresponding to request messages, see the Methods section under:
            // https://learn.microsoft.com/dotnet/api/system.web.http.apicontroller?view=aspnetcore-2.2
            return BadRequest("You must specify either AssetTag (HardwareID) or SoftwareID (but not both).");
        }

        // validate that the user exists
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserID == req.UserID)
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return BadRequest($"No user with UserID {req.UserID} exists!");

        // validate the specified asset exists (LINQ-forward)
        Hardware? gotHardware = null;
        Software? gotSoftware = null;

        if (req.AssetKind == AssetKind.Hardware)
        {
            if (req.AssetTag is null)
                return BadRequest("For AssetKind=Hardware you must supply AssetTag (HardwareID).");

            gotHardware = await _db.HardwareAssets
                .Where(hw => hw.HardwareID == req.AssetTag)
                .SingleOrDefaultAsync(ct);

            if (gotHardware is null)
                return BadRequest("Please specify a valid AssetTag");
        }
        else if (req.AssetKind == AssetKind.Software)
        {
            if (req.SoftwareID is null)
                return BadRequest("For AssetKind=Software you must supply SoftwareID.");

            gotSoftware = await _db.SoftwareAssets
                .Where(sw => sw.SoftwareID == req.SoftwareID)
                .SingleOrDefaultAsync(ct);

            if (gotSoftware is null)
                return BadRequest("Please specify a valid SoftwareID");
        }
        else
        {
            return BadRequest("Unknown AssetKind.");
        }

        // it does not make sense to specify both a Hardware and Software ID in one assignment request
        if (gotHardware is not null && gotSoftware is not null)
            return BadRequest("Please specify only one of either AssetTag (HardwareID) or SoftwareID.");

        // make sure that an OPEN assignment does not already exist (no double assign)
        var assignmentExists = await _db.Assignments
            .Where(a => a.UnassignedAtUtc == null)
            .AnyAsync(a =>
                (req.SoftwareID != null && a.SoftwareID == req.SoftwareID) ||
                (req.AssetTag != null && a.AssetTag == req.AssetTag),
                ct);

        if (assignmentExists)
        {
            if (req.AssetKind == AssetKind.Hardware && req.AssetTag is not null)
                return Conflict($"An open assignment for hardware device with ID {req.AssetTag} already exists!");
            if (req.AssetKind == AssetKind.Software && req.SoftwareID is not null)
                return Conflict($"An open assignment for software with ID {req.SoftwareID} already exists!");
        }

        // See comment above the DTO class; this can be automated using AutoMapper
        // finally, create assignment (soft-open with timestamps)
        var newAssignment = new Assignment
        {
            SoftwareID = req.AssetKind == AssetKind.Software ? req.SoftwareID : null,
            AssetKind = req.AssetKind,
            AssetTag = req.AssetKind == AssetKind.Hardware ? req.AssetTag : null,
            UserID = req.UserID,
            AssignedAtUtc = DateTime.UtcNow,
            UnassignedAtUtc = null
        };

        _db.Assignments.Add(newAssignment);

        // reflect hardware status immediately
        if (gotHardware is not null)
            gotHardware.Status = "Assigned";

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets(); // invalidate unified asset list cache

        // Audit record (non-fatal if it fails)
        try
        {
            string userName = user.FullName;

            string assetName;
            string description;
            if (req.AssetKind == AssetKind.Hardware)
            {
                assetName = gotHardware?.AssetName ?? "";
                description = $"Assigned Hardware Asset {assetName} ({req.AssetTag}) to {userName} ({req.UserID}).";
            }
            else
            {
                assetName = gotSoftware?.SoftwareName ?? "";
                description = $"Assigned Software {assetName} ({req.SoftwareID}) to {userName} ({req.UserID}).";
            }

            await _auditQuery.createAuditRecordAsync(new CreateAuditRecordDto
            {
                UserID = req.UserID,
                Description = description,
                AssetKind = req.AssetKind,
                AssetTag = req.AssetKind == AssetKind.Hardware ? req.AssetTag : null,
                SoftwareID = req.AssetKind == AssetKind.Software ? req.SoftwareID : null
            });
        }
        catch (Exception e)
        {
            // Keep API success; log server-side if needed
            Console.Error.WriteLine($"[Audit] Failed to write audit record: {e}");
        }

        return CreatedAtAction(nameof(GetAssignment), new { AssignmentID = newAssignment.AssignmentID }, req);
    }

    [HttpGet("get")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssignment([FromQuery] int AssignmentID, CancellationToken ct = default)
    {
        var assignment = await _assignQuery.GetAssignmentAsync(AssignmentID, ct);
        if (assignment is null) return NotFound();
        return Ok(assignment);
    }

    // GET /api/assign/list?status=active|closed|all
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
        // find assignment by primary key (LINQ-forward)
        var assignment = await _db.Assignments
            .Where(a => a.AssignmentID == AssignmentID)
            .SingleOrDefaultAsync(ct);

        // if not found, error
        if (assignment == null)
            return NotFound("Please specify a valid AssignmentID");

        // soft-close the record (do not delete; preserve history)
        bool wasOpen = assignment.UnassignedAtUtc == null;
        if (wasOpen)
        {
            assignment.UnassignedAtUtc = DateTime.UtcNow;

            // free hardware status on close
            if (assignment.AssetKind == AssetKind.Hardware && assignment.AssetTag is int hid)
            {
                var hw = await _db.HardwareAssets
                    .Where(h => h.HardwareID == hid)
                    .SingleOrDefaultAsync(ct);
                if (hw != null) hw.Status = "Available";
            }

            await _db.SaveChangesAsync(ct);
            CacheStamp.BumpAssets(); // bust caches so asset list refreshes

            // Audit close (non-fatal)
            try
            {
                string description = assignment.AssetKind == AssetKind.Hardware
                    ? $"Closed assignment {AssignmentID} for HardwareID {assignment.AssetTag}."
                    : $"Closed assignment {AssignmentID} for SoftwareID {assignment.SoftwareID}.";

                await _auditQuery.createAuditRecordAsync(new CreateAuditRecordDto
                {
                    UserID = assignment.UserID,
                    Description = description,
                    AssetKind = assignment.AssetKind,
                    AssetTag = assignment.AssetTag,
                    SoftwareID = assignment.SoftwareID
                });
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[Audit] Failed to write audit record: {e}");
            }
        }

        return Ok();
    }

    // GET /api/assign/user/{userId}/history
    [HttpGet("user/{userId}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserAssignments(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserID == userId)
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return NotFound($"No user with UserID {userId} exists!");

        var assignments = await _db.Assignments
            .Where(a => a.UserID == userId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(assignments);
    }

    // GET /api/assign/software/{softwareId}/history
    [HttpGet("software/{softwareId}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSoftwareHistory(int softwareId, CancellationToken ct = default)
    {
        var software = await _db.SoftwareAssets
            .AsNoTracking()
            .Where(sw => sw.SoftwareID == softwareId)
            .SingleOrDefaultAsync(ct);

        if (software is null)
            return NotFound($"No software with ID {softwareId} exists!");

        var history = await _db.Assignments
            .Where(a => a.SoftwareID == softwareId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(history);
    }

    // GET /api/assign/hardware/{hardwareId}/history
    [HttpGet("hardware/{hardwareId}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHardwareHistory(int hardwareId, CancellationToken ct = default)
    {
        var hardware = await _db.HardwareAssets
            .AsNoTracking()
            .Where(hw => hw.HardwareID == hardwareId)
            .SingleOrDefaultAsync(ct);

        if (hardware is null)
            return NotFound($"No hardware with ID {hardwareId} exists!");

        var history = await _db.Assignments
            .Where(a => a.AssetTag == hardwareId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(history);
    }

}

/**
See the following link for the reason for why we use DTOs:
https://blog.devart.com/working-with-data-transfer-objects-in-asp-net-core.html

We might move this to a new file:
https://learn.microsoft.com/aspnet/web-api/overview/data/using-web-api-with-entity-framework/part-5

(TODO): We can use an automapper to generate the bindings from this DTO to the actual model:
https://automapper.io

Note: DTO classes for this controller live in AIMS.ViewModels (AssignmentsDtos.cs) per project structure.
*/
