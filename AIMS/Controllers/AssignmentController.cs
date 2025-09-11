using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities; // <- for CacheStamp
using AIMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

// [Authorize(Roles = "Admin")] // enable when auth is wired
[ApiController]
[Route("api/assign")]
public class AssignmentController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly AssignmentsQuery _assignQuery;

    public AssignmentController(AimsDbContext db, AssignmentsQuery assignQuery)
    {
        _db = db;
        _assignQuery = assignQuery;
    }

    // POST /api/assign/create
    [HttpPost("create")]
    [ProducesResponseType(typeof(CreateAssignmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentDto req)
    {
        // one-of validation
        bool bothNull = req.AssetTag == null && req.SoftwareID == null;
        bool bothValues = req.SoftwareID != null && req.AssetTag != null;
        if (bothNull || bothValues)
            return BadRequest("You must specify either AssetTag (HardwareID) or SoftwareID (but not both).");

        // kind-specific validation
        if (req.AssetKind == AssetKind.Hardware)
        {
            if (req.AssetTag == null)
                return BadRequest("For AssetKind=Hardware you must supply AssetTag (HardwareID).");

            var assetTagExists = await _db.HardwareAssets.AnyAsync(hw => hw.HardwareID == req.AssetTag);
            if (!assetTagExists)
                return BadRequest("Please specify a valid AssetTag (HardwareID).");
        }
        else if (req.AssetKind == AssetKind.Software)
        {
            if (req.SoftwareID == null)
                return BadRequest("For AssetKind=Software you must supply SoftwareID.");

            var softwareIDExists = await _db.SoftwareAssets.AnyAsync(sw => sw.SoftwareID == req.SoftwareID);
            if (!softwareIDExists)
                return BadRequest("Please specify a valid SoftwareID.");
        }
        else
        {
            return BadRequest("Unknown AssetKind.");
        }

        // prevent open duplicate for the same asset
        bool assignmentExists = await _db.Assignments.AnyAsync(a =>
            a.UnassignedAtUtc == null && (
                (req.AssetKind == AssetKind.Hardware && req.AssetTag != null && a.AssetTag == req.AssetTag) ||
                (req.AssetKind == AssetKind.Software && req.SoftwareID != null && a.SoftwareID == req.SoftwareID)
            )
        );
        if (assignmentExists)
        {
            if (req.AssetKind == AssetKind.Hardware)
                return Conflict($"An open assignment for hardware with ID {req.AssetTag} already exists.");
            else
                return Conflict($"An open assignment for software with ID {req.SoftwareID} already exists.");
        }

        // create assignment (soft-open)
        var newAssignment = new Assignment
        {
            UserID = req.UserID,
            AssetKind = req.AssetKind,
            AssetTag = req.AssetKind == AssetKind.Hardware ? req.AssetTag : null,
            SoftwareID = req.AssetKind == AssetKind.Software ? req.SoftwareID : null,
            AssignedAtUtc = DateTime.UtcNow,
            UnassignedAtUtc = null
        };

        _db.Assignments.Add(newAssignment);

        // reflect hardware status
        if (req.AssetKind == AssetKind.Hardware && req.AssetTag is int hid)
        {
            var hw = await _db.HardwareAssets.FindAsync(hid);
            if (hw != null) hw.Status = "Assigned";
        }

        await _db.SaveChangesAsync();
        CacheStamp.BumpAssets(); // invalidate unified asset list cache

        return CreatedAtAction(nameof(GetAssignment), new { AssignmentID = newAssignment.AssignmentID }, req);
    }

    // GET /api/assign/get?AssignmentID=123
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

    // POST /api/assign/close?AssignmentID=123
    [HttpPost("close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Close([FromQuery] int AssignmentID)
    {
        var assignment = await _db.Assignments.FindAsync(AssignmentID);
        if (assignment == null)
            return NotFound("Please specify a valid AssignmentID");

        if (assignment.UnassignedAtUtc == null)
            assignment.UnassignedAtUtc = DateTime.UtcNow;

        // free hardware status on close
        if (assignment.AssetKind == AssetKind.Hardware && assignment.AssetTag is int hid)
        {
            var hw = await _db.HardwareAssets.FindAsync(hid);
            if (hw != null) hw.Status = "Available";
        }

        await _db.SaveChangesAsync();
        CacheStamp.BumpAssets(); // bust caches so asset list refreshes

        return Ok();
    }
}
