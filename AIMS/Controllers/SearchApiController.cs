using System.Security.Claims;
using AIMS.Data;
using AIMS.Queries;
using AIMS.Utilities; // IsSupervisor()
using AIMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

[ApiController]
public sealed class SearchApiController : ControllerBase
{
    private readonly AssetSearchQuery _search;
    private readonly AimsDbContext _db;
    private readonly IWebHostEnvironment _env;

    public SearchApiController(AssetSearchQuery search, AimsDbContext db, IWebHostEnvironment env)
    {
        _search = search;
        _db = db;
        _env = env;
    }

    // GET /api/assets/search?q=&type=&status=&page=&pageSize=&impersonate=28809   (impersonate is DEV-only helper)
    [HttpGet("/api/assets/search")]
    public async Task<ActionResult<PagedResult<AssetRowVm>>> Get(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? impersonate = null)
    {
        // ----- DEV impersonation support -----
        if (!string.IsNullOrWhiteSpace(impersonate) && _env.IsDevelopment())
        {
            var key = impersonate.Trim();
            var impUser = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeNumber == key || u.Email == key);
            if (impUser is not null)
            {
                HttpContext.Items["ImpersonatedUserId"] = impUser.UserID;
                HttpContext.Items["ImpersonatedEmail"] = impUser.Email;
            }
        }

        // ----- If ALL filters are blank: only allow auto-load for Supervisors -----
        var isBlank = string.IsNullOrWhiteSpace(q)
                      && string.IsNullOrWhiteSpace(type)
                      && string.IsNullOrWhiteSpace(status);

        if (isBlank)
        {
            // Ask the query helper to resolve the DB role
            var (_, roleName) = await _search.ResolveCurrentUserAsync(HttpContext.RequestAborted);

            // Only DB "Supervisor" gets auto-load on blank; everyone else (including Admin/Helpdesk) = empty
            if (!string.Equals(roleName, "Supervisor", StringComparison.OrdinalIgnoreCase))
                return Ok(PagedResult<AssetRowVm>.Empty());
        }

        var result = await _search.SearchAsync(
            q: q, type: type, status: status,
            page: page, pageSize: pageSize,
            ct: HttpContext.RequestAborted);

        return Ok(result);
    }
}
