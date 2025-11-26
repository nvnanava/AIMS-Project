using AIMS.Data;
using AIMS.Dtos.Users;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly AimsDbContext _db;
        private readonly ILogger<UsersController> _logger;

        public UsersController(AimsDbContext db, ILogger<UsersController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET /api/users/search?searchString=...&skip=0&take=25&softwareId=123
        [HttpGet("search")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<List<PersonDto>>> SearchUsers(
            [FromQuery] string? searchString,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 25,
            [FromQuery] int? softwareId = null)
        {
            try
            {
                var term = (searchString ?? string.Empty).Trim();
                take = Math.Clamp(take, 1, 100);
                skip = Math.Max(skip, 0);

                // Base query: only non-archived users
                var query = _db.Users
                    .AsNoTracking()
                    .Where(u => !u.IsArchived);

                // Seat-aware filter: if softwareId is provided,
                // exclude users who ALREADY have an ACTIVE assignment for that software.
                if (softwareId.HasValue)
                {
                    var sid = softwareId.Value;

                    query = query.Where(u =>
                        !_db.Assignments
                            .AsNoTracking()
                            .Any(a =>
                                a.AssetKind == AssetKind.Software &&
                                a.SoftwareID == sid &&
                                a.UnassignedAtUtc == null &&
                                a.UserID == u.UserID));
                }

                if (!string.IsNullOrWhiteSpace(term))
                {
                    var pattern = $"%{term}%";
                    query = query.Where(u =>
                        EF.Functions.Like(u.FullName, pattern) ||
                        EF.Functions.Like(u.EmployeeNumber, pattern) ||
                        EF.Functions.Like(u.Email, pattern));
                }

                var users = await query
                    .OrderBy(u => u.FullName)
                    .Skip(skip)
                    .Take(take)
                    .Select(u => new PersonDto
                    {
                        UserID = u.UserID,
                        EmployeeNumber = u.EmployeeNumber,
                        Name = u.FullName,
                        OfficeID = u.OfficeID
                    })
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error in UsersController.SearchUsers (searchString={Search}, skip={Skip}, take={Take}, softwareId={SoftwareId})",
                    searchString, skip, take, softwareId
                );

                return StatusCode(StatusCodes.Status500InternalServerError, ex.ToString());
            }
        }
    }
}
