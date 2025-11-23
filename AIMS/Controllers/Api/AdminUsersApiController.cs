using System.Threading; // for CancellationToken
using AIMS.Contracts;
using AIMS.Data;
using AIMS.Models;   // for User model
using AIMS.Queries;
using AIMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;



[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "mbcAdmin")] // Only users with the "Admin" role can access any action methods in this controller
public class AdminUsersApiController : ControllerBase
{
    private readonly IAdminUserUpsertService _svc; // Service to upsert admin users
    // use OfficeQuery to abstract complex logic
    private readonly OfficeQuery _officeQuery;
    private readonly AimsDbContext _db;
    public AdminUsersApiController(IAdminUserUpsertService svc, AimsDbContext db, OfficeQuery officeQuery)
    {
        _svc = svc;
        _db = db;
        _officeQuery = officeQuery;
    }

    // we use OfficeName here since that is what is held in common between AAD and the local DB
    public record AddAadUserRequest(string GraphObjectId, int? RoleId, int? SupervisorId, string? OfficeName); //defines the request body for adding an AAD user, used in the POST method

    [HttpGet("exists")] // Endpoint to check if a user with the given GraphObjectId exists
    public async Task<IActionResult> Exists([FromQuery] string graphObjectId, CancellationToken ct) //checks if a user with the specified GraphObjectId exists in the database
    {
        if (string.IsNullOrWhiteSpace(graphObjectId))
            return BadRequest("GraphObjectId is required."); // this returns a 400 Bad Request response if the GraphObjectId is missing

        var exists = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.GraphObjectID == graphObjectId, ct); // this queries the database to check for existence
        return Ok(new { exists });
    }

    [HttpPost] //this method handles HTTP POST requests to add or update an AAD user in the system
    public async Task<IActionResult> Add([FromBody] AddAadUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.GraphObjectId))
            return BadRequest("GraphObjectId is required.");


        // make sure that OfficeName is not null
        if (string.IsNullOrWhiteSpace(req.OfficeName))
        {
            return BadRequest("OfficeName is required.");
        }

        // check if the database contains an office of the same name
        var office = await _db.Offices.Where(o => o.OfficeName.ToLower() == req.OfficeName.ToLower()).FirstOrDefaultAsync(ct);
        // store the OfficeID
        var OfficeId = office is not null ? office.OfficeID : -1;

        // if a new office is being added
        if (office is null)
        {
            // create the new office and retrieve its ID in the local DB
            OfficeId = await _officeQuery.AddOffice(req.OfficeName);
        }

        // pass on OfficeId to the UpsertService
        var saved = await _svc.UpsertAdminUserAsync(req.GraphObjectId, req.RoleId, req.SupervisorId, OfficeId, ct); //this calls the service to upsert the user from AAD
        return Ok(new //returns the saved user details as JSON
        {
            saved.UserID,
            saved.GraphObjectID,
            saved.FullName,
            saved.Email,
            saved.RoleID,
            saved.SupervisorID

        });



    }
    // GET /api/admin/users?includeArchived=true|false
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeArchived = false, CancellationToken ct = default)
    {
        var q = includeArchived ? _db.Users.IgnoreQueryFilters() : _db.Users.AsQueryable();

        var rows = await q
            .Include(u => u.Office)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                userID = u.UserID,
                employeeNumber = u.EmployeeNumber,
                name = u.FullName,
                email = u.Email,
                officeId = u.OfficeID,
                officeName = u.Office != null ? u.Office.OfficeName : null,
                isArchived = u.IsArchived,
                archivedAtUtc = u.ArchivedAtUtc
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    // GET /api/admin/users/search?q=<search>&searchInactive=true|false
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q = null, [FromQuery] bool searchInactive = false, CancellationToken ct = default)
    {
        // If searchInactive is true, ONLY search inactive users
        // If searchInactive is false, ONLY search active users
        var query = searchInactive 
            ? _db.Users.IgnoreQueryFilters().Where(u => u.IsArchived) 
            : _db.Users.Where(u => !u.IsArchived);

        // Apply search filter if query is provided
        if (!string.IsNullOrWhiteSpace(q))
        {
            var searchTerm = q.Trim().ToLower();
            query = query.Where(u => 
                u.FullName.ToLower().Contains(searchTerm) || 
                u.Email.ToLower().Contains(searchTerm));
        }

        var rows = await query
            .Include(u => u.Office)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                userID = u.UserID,
                employeeNumber = u.EmployeeNumber,
                name = u.FullName,
                email = u.Email,
                officeId = u.OfficeID,
                officeName = u.Office != null ? u.Office.OfficeName : null,
                isArchived = u.IsArchived,
                archivedAtUtc = u.ArchivedAtUtc
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
    // POST /api/admin/users/archive/{id}
    [HttpPost("archive/{id:int}")]
    public async Task<IActionResult> Archive(
        int id,
        [FromServices] IAuditEventBroadcaster audit, // injected per-action
        CancellationToken ct)
    {
        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.UserID == id, ct);
        if (u is null) return NotFound();

        if (!u.IsArchived)
        {
            u.IsArchived = true;
            u.ArchivedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Write AuditLog row (AssetKind = 3 for user actions)
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "Archive User",   // if Action is enum(int) in your DB, cast your enum here
                AssetKind = AssetKind.User,
                HardwareID = null,
                SoftwareID = null,
                Description = $"Archived user {u.FullName} ({u.UserID}) at {u.ArchivedAtUtc:O}",
                TimestampUtc = DateTime.UtcNow,
                UserID = u.UserID
            });
            await _db.SaveChangesAsync(ct);

            // Realtime broadcast to the "audit" group
            await audit.BroadcastAsync(new AuditEventDto
            {
                Id = u.UserID.ToString(),
                OccurredAtUtc = DateTime.UtcNow,
                Type = "Archive User",
                User = $"{u.FullName} ({u.UserID})",
                Target = $"User#{u.UserID}",
                Details = $"Archived at {u.ArchivedAtUtc:O}"
            });
        }

        return NoContent();
    }
    // POST /api/admin/users/unarchive/{id}
    [HttpPost("unarchive/{id:int}")]
    public async Task<IActionResult> Unarchive(
        int id,
        [FromServices] IAuditEventBroadcaster audit,
        CancellationToken ct)
    {
        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.UserID == id, ct);
        if (u is null) return NotFound();

        if (u.IsArchived)
        {
            u.IsArchived = false;
            u.ArchivedAtUtc = null;
            await _db.SaveChangesAsync(ct);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "Unarchive User",
                AssetKind = AssetKind.User,
                HardwareID = null,
                SoftwareID = null,
                Description = $"Unarchived user {u.FullName} ({u.UserID})",
                TimestampUtc = DateTime.UtcNow,
                UserID = u.UserID
            });
            await _db.SaveChangesAsync(ct);

            await audit.BroadcastAsync(new AuditEventDto
            {
                Id = u.UserID.ToString(),
                OccurredAtUtc = DateTime.UtcNow,
                Type = "Unarchive User",
                User = $"{u.FullName} ({u.UserID})",
                Target = $"User#{u.UserID}",
                Details = "Unarchived"
            });
        }

        return NoContent();
    }

}

