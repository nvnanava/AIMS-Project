using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Common;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace AIMS.Controllers.Api;

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

    [HttpGet("/api/assets/search")]
    public async Task<ActionResult<PagedResult<AssetRowDto>>> Get(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool showArchived = false,
        [FromQuery] string? impersonateRole = null) // <-- NEW (test-only)
    {
        var ct = HttpContext.RequestAborted;

        // -----------------------------------------------------
        // 1) Resolve actual user
        // -----------------------------------------------------
        var (_, roleName) = await _search.ResolveCurrentUserAsync(ct);

        // -----------------------------------------------------
        // 2) Test-only role override for branch coverage
        // -----------------------------------------------------
        if (!string.IsNullOrWhiteSpace(impersonateRole) && _env.IsEnvironment("Test"))
        {
            roleName = impersonateRole.Trim();
        }

        var isSupervisor =
            string.Equals(roleName, "Supervisor", StringComparison.OrdinalIgnoreCase);

        // -----------------------------------------------------
        // 3) Blank-search early return unless Supervisor
        // -----------------------------------------------------
        if (IsBlankSearch(q, type, status) && !isSupervisor)
        {
            return Ok(PagedResult<AssetRowDto>.Empty());
        }

        // -----------------------------------------------------
        // 4) Run the real search
        // -----------------------------------------------------
        var result = await _search.SearchAsync(
            q: q,
            type: type,
            status: status,
            page: page,
            pageSize: pageSize,
            ct: ct,
            category: null,
            showArchived: showArchived,
            totalsMode: PagingTotals.Exact);

        return Ok(result);
    }

    // ----------------- Helpers -----------------

    private static bool IsBlankSearch(string? q, string? type, string? status)
    {
        return string.IsNullOrWhiteSpace(q)
               && string.IsNullOrWhiteSpace(type)
               && string.IsNullOrWhiteSpace(status);
    }
}
