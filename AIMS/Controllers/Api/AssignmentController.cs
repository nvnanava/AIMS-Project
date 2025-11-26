using AIMS.Data;
using AIMS.Dtos.Assignments;
using AIMS.Dtos.Audit;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using AIMS.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[Authorize(Policy = "mbcAdmin")]
[ApiController]
[Route("api/assign")]
public class AssignmentController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly AssignmentsQuery _assignQuery;
    private readonly AuditLogQuery _auditQuery;

    private readonly ISummaryCardService _summaryCardService;
    private readonly ICurrentUser _currentUser;

    public AssignmentController(
        AimsDbContext db,
        AssignmentsQuery assignQuery,
        AuditLogQuery auditQuery,
        ISummaryCardService summaryCardService,
        ICurrentUser currentUser)
    {
        _db = db;
        _assignQuery = assignQuery;
        _auditQuery = auditQuery;
        _summaryCardService = summaryCardService;
        _currentUser = currentUser;
    }

    [HttpPost("create")]
    [ProducesResponseType(typeof(AssignmentCreatedDto), StatusCodes.Status201Created)]
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

        // prevent double-open assignment (hardware only)
        var assignmentExists = await _db.Assignments
            .Where(a => a.UnassignedAtUtc == null)
            .AnyAsync(a =>
                req.AssetKind == AssetKind.Hardware &&
                req.HardwareID != null &&
                a.HardwareID == req.HardwareID,
                ct);

        if (assignmentExists)
        {
            // Only hardware uses this path
            return Conflict($"An open assignment for HardwareID {req.HardwareID} already exists!");
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
            // Agreement* fields are set via the separate UploadAgreement endpoint
        };

        _db.Assignments.Add(newAssignment);

        string? oldHardwareStatus = null;
        string? newHardwareStatus = null;

        // reflect hardware status immediately
        if (gotHardware is not null)
        {
            oldHardwareStatus = gotHardware.Status;  // e.g. "Available"
            gotHardware.Status = "Assigned";
            newHardwareStatus = gotHardware.Status;  // "Assigned"
        }

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();
        _summaryCardService.InvalidateSummaryCache();

        // audit (best-effort)
        try
        {
            var assetName = gotHardware?.AssetName ?? gotSoftware?.SoftwareName ?? "";
            var idText = req.AssetKind == AssetKind.Hardware
                ? $"HardwareID {req.HardwareID}"
                : $"SoftwareID {req.SoftwareID}";

            var baseDesc = $"Assigned {req.AssetKind} {assetName} ({idText}) to {user.FullName} ({user.UserID}).";
            var description = string.IsNullOrWhiteSpace(req.Comment)
                ? baseDesc
                : $"{baseDesc} Comment: {req.Comment}";

            List<CreateAuditLogChangeDto>? changes = null;

            // Only log a change row for hardware status
            if (req.AssetKind == AssetKind.Hardware &&
                !string.IsNullOrWhiteSpace(oldHardwareStatus) &&
                !string.IsNullOrWhiteSpace(newHardwareStatus) &&
                !string.Equals(oldHardwareStatus, newHardwareStatus, StringComparison.OrdinalIgnoreCase))
            {
                changes = new List<CreateAuditLogChangeDto>
        {
            new()
            {
                Field = "Status",
                OldValue = oldHardwareStatus,
                NewValue = newHardwareStatus
            }
        };
            }

            var actorUserId = await _currentUser.GetUserIdAsync(ct);
            // If for some reason we can't resolve the actor, just skip audit
            if (actorUserId is not null)
            {
                await _auditQuery.CreateAuditRecordAsync(new CreateAuditRecordDto
                {
                    UserID = actorUserId.Value,
                    Action = "Assign",
                    Description = description,
                    AssetKind = req.AssetKind,
                    HardwareID = req.AssetKind == AssetKind.Hardware ? req.HardwareID : null,
                    SoftwareID = req.AssetKind == AssetKind.Software ? req.SoftwareID : null,
                    Changes = changes,
                    AssignmentID = newAssignment.AssignmentID
                });
            }
        }
        catch
        {
            // swallow; success not blocked by audit
        }

        // Return a DTO that includes AssignmentID so the JS can upload the agreement
        var dto = new AssignmentCreatedDto
        {
            AssignmentID = newAssignment.AssignmentID,
            UserID = newAssignment.UserID ?? req.UserID,
            AssetKind = newAssignment.AssetKind,
            HardwareID = newAssignment.HardwareID,
            SoftwareID = newAssignment.SoftwareID
        };

        return CreatedAtAction(
            nameof(GetAssignment),
            new { AssignmentID = newAssignment.AssignmentID },
            dto);
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
    public async Task<IActionResult> Close(
        [FromServices] SoftwareSeatService softwareSeatService,
        [FromQuery] int AssignmentID,
        [FromQuery] string? comment,
        CancellationToken ct = default)
    {
        var assignment = await _db.Assignments
            .SingleOrDefaultAsync(a => a.AssignmentID == AssignmentID, ct);

        if (assignment is null)
            return NotFound("Please specify a valid AssignmentID");

        // Idempotent: already closed
        if (assignment.UnassignedAtUtc is not null)
            return Ok();

        // -----------------------------
        // SOFTWARE: delegate to seat service
        // -----------------------------
        if (assignment.AssetKind == AssetKind.Software &&
            assignment.SoftwareID is int softwareId &&
            assignment.UserID is int userId)
        {
            // Actor = current logged-in AIMS user, not seat holder
            var actorUserId = await _currentUser.GetUserIdAsync(ct);
            if (actorUserId is null)
                return Forbid();

            await softwareSeatService.ReleaseSeatAsync(
                softwareId,
                userId,              // seat holder
                actorUserId.Value,   // actor
                comment,
                ct);

            // Seat service already bumps CacheStamp
            _summaryCardService.InvalidateSummaryCache();
            return Ok();
        }

        // -----------------------------
        // HARDWARE (existing behavior)
        // -----------------------------
        assignment.UnassignedAtUtc = DateTime.UtcNow;

        string? oldHardwareStatus = null;
        string? newHardwareStatus = null;

        if (assignment.AssetKind == AssetKind.Hardware && assignment.HardwareID is int hid)
        {
            var hw = await _db.HardwareAssets.SingleOrDefaultAsync(h => h.HardwareID == hid, ct);
            if (hw != null)
            {
                oldHardwareStatus = hw.Status;     // likely "Assigned"
                hw.Status = "Available";
                newHardwareStatus = hw.Status;     // "Available"
            }
        }

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();
        _summaryCardService.InvalidateSummaryCache();

        // ---- Audit for non-software close ----
        try
        {
            await _db.Entry(assignment).Reference(a => a.User).LoadAsync(ct);

            if (assignment.AssetKind == AssetKind.Hardware)
            {
                await _db.Entry(assignment).Reference(a => a.Hardware).LoadAsync(ct);
            }
            else
            {
                await _db.Entry(assignment).Reference(a => a.Software).LoadAsync(ct);
            }

            var assetKindText = assignment.AssetKind == AssetKind.Hardware ? "Hardware" : "Software";

            string assetName;
            string idText;

            if (assignment.AssetKind == AssetKind.Hardware)
            {
                assetName = assignment.Hardware?.AssetName
                            ?? $"HardwareID {assignment.HardwareID}";

                idText = assignment.Hardware?.AssetTag
                         ?? assignment.HardwareID?.ToString() ?? "Unknown";
            }
            else
            {
                assetName = assignment.Software?.SoftwareName
                            ?? $"SoftwareID {assignment.SoftwareID}";

                idText = assignment.Software?.SoftwareLicenseKey
                         ?? assignment.SoftwareID?.ToString() ?? "Unknown";
            }

            string userDisplay;
            if (assignment.User is { } u)
            {
                var idPart = !string.IsNullOrWhiteSpace(u.EmployeeNumber)
                    ? u.EmployeeNumber
                    : u.UserID.ToString();

                userDisplay = $"{u.FullName} ({idPart})";
            }
            else
            {
                userDisplay = "Unknown user";
            }

            var baseDesc = $"Unassigned {assetKindText} {assetName} ({idText}) from {userDisplay}.";
            var description = string.IsNullOrWhiteSpace(comment)
                ? baseDesc
                : $"{baseDesc} Comment: {comment}";

            List<CreateAuditLogChangeDto>? changes = null;

            if (assignment.AssetKind == AssetKind.Hardware &&
                !string.IsNullOrWhiteSpace(oldHardwareStatus) &&
                !string.IsNullOrWhiteSpace(newHardwareStatus) &&
                !string.Equals(oldHardwareStatus, newHardwareStatus, StringComparison.OrdinalIgnoreCase))
            {
                changes = new List<CreateAuditLogChangeDto>
                {
                    new()
                    {
                        Field = "Status",
                        OldValue = oldHardwareStatus,
                        NewValue = newHardwareStatus
                    }
                };
            }

            var actorUserId = await _currentUser.GetUserIdAsync(ct);
            if (actorUserId is not null)
            {
                await _auditQuery.CreateAuditRecordAsync(new CreateAuditRecordDto
                {
                    UserID = actorUserId.Value,
                    Action = "Unassign",
                    Description = description,
                    AssetKind = assignment.AssetKind,
                    HardwareID = assignment.HardwareID,
                    SoftwareID = assignment.SoftwareID,
                    Changes = changes,
                    AssignmentID = assignment.AssignmentID
                });
            }
        }
        catch { /* ignore; don't block unassign on audit failure */}
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

    [HttpPost("{assignmentId:int}/agreement")]
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * 1024 * 1024)] // e.g. 20 MB
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadAgreement(
        int assignmentId,
        IFormFile? file,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var assignment = await _db.Assignments
            .SingleOrDefaultAsync(a => a.AssignmentID == assignmentId, ct);

        if (assignment is null)
            return NotFound($"No assignment with ID {assignmentId}.");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        assignment.AgreementFile = ms.ToArray();
        assignment.AgreementFileName = file.FileName;
        assignment.AgreementContentType =
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{assignmentId:int}/agreement")]
    public async Task<IActionResult> GetAgreement(int assignmentId, CancellationToken ct = default)
    {
        var assignment = await _db.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.AssignmentID == assignmentId, ct);

        if (assignment is null)
            return NotFound($"No assignment with ID {assignmentId}.");

        if (assignment.AgreementFile == null || assignment.AgreementFile.Length == 0)
            return NotFound("No agreement file is attached to this assignment.");

        var contentType = string.IsNullOrWhiteSpace(assignment.AgreementContentType)
            ? "application/octet-stream"
            : assignment.AgreementContentType;

        var fileName = string.IsNullOrWhiteSpace(assignment.AgreementFileName)
            ? $"agreement-{assignmentId}.bin"
            : assignment.AgreementFileName;

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
        return File(assignment.AgreementFile, contentType);
    }
}
