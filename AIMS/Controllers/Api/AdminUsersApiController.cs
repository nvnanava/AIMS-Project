using System.Threading; // for CancellationToken
using AIMS.Data;
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
    private readonly AimsDbContext _db;
    public AdminUsersApiController(IAdminUserUpsertService svc, AimsDbContext db)
    {
        _svc = svc;
        _db = db;
    }

    public record AddAadUserRequest(string GraphObjectId, int? RoleId, int? SupervisorId); //defines the request body for adding an AAD user, used in the POST method

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

        var saved = await _svc.UpsertAdminUserAsync(req.GraphObjectId, req.RoleId, req.SupervisorId, ct); //this calls the service to upsert the user from AAD
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

}

