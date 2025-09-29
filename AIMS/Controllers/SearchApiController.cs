using System.Security.Claims;
using AIMS.Data;
using AIMS.Queries;
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
        // AssetSearchQuery looks for HttpContext.Items["ImpersonatedEmail"] / ["ImpersonatedUserId"].
        if (!string.IsNullOrWhiteSpace(impersonate) && _env.IsDevelopment())
        {
            var key = impersonate.Trim();
            // Prefer employee number, then email. Resolve here and stash in Items so the query can scope correctly.
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
            var role = await ResolveRoleNameAsync();
            // Only Supervisors auto-load their scoped list on blank; everyone else gets empty (no DB hit).
            if (!string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase))
                return Ok(PagedResult<AssetRowVm>.Empty());
        }

        var result = await _search.SearchAsync(
            q: q, type: type, status: status,
            page: page, pageSize: pageSize,
            ct: HttpContext.RequestAborted);

        return Ok(result);
    }

    // -------- helpers --------
    private async Task<string?> ResolveRoleNameAsync()
    {
        // If impersonating, we already looked up the user—re-use that
        if (HttpContext.Items.TryGetValue("ImpersonatedUserId", out var idObj) && idObj is int impId)
        {
            var role = await (from u in _db.Users.AsNoTracking()
                              join r in _db.Roles.AsNoTracking() on u.RoleID equals r.RoleID
                              where u.UserID == impId
                              select r.RoleName).FirstOrDefaultAsync();
            return role;
        }

        // Otherwise use claims (email or employeeNumber)
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var emp = User.FindFirstValue("employeeNumber");

        var me = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u =>
                (!string.IsNullOrEmpty(email) && u.Email == email) ||
                (!string.IsNullOrEmpty(emp) && u.EmployeeNumber == emp));

        if (me is null) return null;

        var myRole = await _db.Roles.AsNoTracking()
            .Where(r => r.RoleID == me.RoleID)
            .Select(r => r.RoleName)
            .FirstOrDefaultAsync();

        return myRole;
    }
}
