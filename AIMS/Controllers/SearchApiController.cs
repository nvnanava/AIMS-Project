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

    // GET /api/assets/search?q=&type=&status=&page=&pageSize=&impersonate=...
    [HttpGet("/api/assets/search")]
    public async Task<ActionResult<PagedResult<AssetRowVm>>> Get(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? impersonate = null)
    {
        // DEV impersonation support
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

        // If ALL filters are blank: only allow auto-load for Supervisors
        var isBlank = string.IsNullOrWhiteSpace(q)
                      && string.IsNullOrWhiteSpace(type)
                      && string.IsNullOrWhiteSpace(status);

        if (isBlank)
        {
            var (_, roleName) = await _search.ResolveCurrentUserAsync(HttpContext.RequestAborted);
            if (!string.Equals(roleName, "Supervisor", StringComparison.OrdinalIgnoreCase))
                return Ok(PagedResult<AssetRowVm>.Empty());
        }

        var result = await _search.SearchAsync(
            q: q, type: type, status: status,
            page: page, pageSize: pageSize,
            ct: HttpContext.RequestAborted,
            category: null,
            totalsMode: PagingTotals.Exact);       // <— SEARCH = EXACT TOTALS

        return Ok(result);
    }
}