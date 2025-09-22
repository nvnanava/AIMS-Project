using System.Security.Claims;
using AIMS.Data;
using AIMS.Queries;
using AIMS.Utilities;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Controllers;

[ApiController]
[Route("api/assets")]
public class AssetsApiController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly AssetQuery _assetQuery;
    private readonly IMemoryCache _cache;

    public AssetsApiController(AimsDbContext db, IMemoryCache cache, AssetQuery assetQuery)
    {
        _db = db;
        _cache = cache;
        _assetQuery = assetQuery;
    }

    // GET /api/assets?page=1&pageSize=50&sort=AssetName&dir=asc&q=thinkpad&types=Laptop&statuses=Assigned&scope=my
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = "asc",
        [FromQuery] string? q = null,
        [FromQuery] List<string>? types = null,
        [FromQuery] List<string>? statuses = null,
        [FromQuery] string scope = "all", // "all" | "my" | "reports"
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        // ----- Role/ownership guard (placeholder) -----
        bool isAdminOrIt = User.IsInRole("Admin") || User.IsInRole("IT Help Desk");

        // In dev or when unauthenticated, default to Admin behavior so scope=all works
        if (!User?.Identity?.IsAuthenticated ?? true)
            isAdminOrIt = true;

        var currentEmpId = User?.FindFirstValue("employeeNumber") ?? "28809"; // fallback for demo

        if (!isAdminOrIt && scope == "all")
            scope = "my"; // non-admins cannot see "all"

        // ---- cache key includes version stamp so it auto-busts after assign/close ----
        var ver = CacheStamp.AssetsVersion;
        var key = $"assets:v{ver}:p{page}:s{pageSize}:sort{sort}:{dir}:q{q}:t{string.Join(',', types ?? new())}:st{string.Join(',', statuses ?? new())}:sc{scope}:u{currentEmpId}";
        if (_cache.TryGetValue<AssetsPagePayloadVm>(key, out var cached))
        {
            var firstTag = cached!.Items.FirstOrDefault()?.Tag ?? string.Empty;
            var lastTag = cached.Items.LastOrDefault()?.Tag ?? "";
            var etag = $"W/\"{cached.Total}-{firstTag}-{lastTag}\"";

            if (Request.Headers.TryGetValue("If-None-Match", out var inm) && inm.Contains(etag))
                return StatusCode(304);

            Response.Headers.ETag = etag;
            return Ok(cached);
        }

        // ---------- Base dataset (hardware + software) ----------
        const int HardwareKind = 1;
        const int SoftwareKind = 2;

        var activeAssignments = _db.Assignments.AsNoTracking()
    .Where(a => a.UnassignedAtUtc == null)
    .Select(a => new { AssetKind = (int)a.AssetKind, a.AssetTag, a.SoftwareID, a.UserID, a.AssignedAtUtc });

        var hardwareBase =
            from h in _db.HardwareAssets.AsNoTracking()
            join aa in activeAssignments.Where(x => x.AssetKind == HardwareKind)
                on h.HardwareID equals aa.AssetTag into ha
            from aa in ha.DefaultIfEmpty()
            select new
            {
                AssetName = h.AssetName,
                TypeRaw = h.AssetType,
                Tag = h.AssetTag,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = h.Status,
                AssignedAtUtc = (DateTime?)aa.AssignedAtUtc,
                HardwareID = (int?)h.HardwareID // Nullable for now.may need a unified dto that includes IDs. Edits in db are called using the ID
            };

        var softwareBase =
            from s in _db.SoftwareAssets.AsNoTracking()
            join aa in activeAssignments.Where(x => x.AssetKind == SoftwareKind)
                on s.SoftwareID equals aa.SoftwareID into sa
            from aa in sa.DefaultIfEmpty()
            select new
            {
                AssetName = s.SoftwareName,
                TypeRaw = s.SoftwareType,
                Tag = s.SoftwareLicenseKey,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = "",
                AssignedAtUtc = (DateTime?)aa.AssignedAtUtc,
                HardwareID = (int?)null //
            };

        var queryable = hardwareBase.Concat(softwareBase);

        // ---------- Ownership scope ----------
        if (!isAdminOrIt)
        {
            // Non-admins: restrict to "my" or "reports"
            scope = scope is "reports" ? "reports" : "my";
        }

        int myUserId = 0;
        List<int> reportIds = new();

        if (scope is "my" or "reports")
        {
            var me = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeNumber == currentEmpId, ct);
            myUserId = me?.UserID ?? 0;

            if (scope == "my")
            {
                queryable = queryable.Where(x => x.AssignedUserId == myUserId);
            }
            else // "reports" => me + my direct reports
            {
                reportIds = await _db.Users.AsNoTracking()
                    .Where(u => u.SupervisorID == myUserId)
                    .Select(u => u.UserID)
                    .ToListAsync(ct);

                queryable = queryable.Where(x =>
                    x.AssignedUserId == myUserId ||
                    (x.AssignedUserId != null && reportIds.Contains(x.AssignedUserId.Value)));
            }
        }

        // ---------- Server filters (types/status + search) ----------
        if (types is { Count: > 0 })
            queryable = queryable.Where(x => types.Contains(string.IsNullOrWhiteSpace(x.TypeRaw) ? "Software" : x.TypeRaw));

        if (statuses is { Count: > 0 })
        {
            // StatusRaw is only set for hardware; software uses Assigned/Available
            queryable = queryable.Where(x =>
                (!string.IsNullOrEmpty(x.StatusRaw) && statuses.Contains(x.StatusRaw)) ||
                (string.IsNullOrEmpty(x.StatusRaw) && statuses.Contains(x.AssignedUserId != null ? "Assigned" : "Available"))
            );
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            queryable = queryable.Where(x =>
                (x.AssetName ?? "").ToLower().Contains(term) ||
                (x.TypeRaw ?? "Software").ToLower().Contains(term) ||
                (x.Tag ?? "").ToLower().Contains(term));
        }

        // ---------- Sorting ----------
        bool asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        queryable = (sort?.ToLower()) switch
        {
            "assetname" => asc ? queryable.OrderBy(x => x.AssetName) : queryable.OrderByDescending(x => x.AssetName),
            "type" => asc ? queryable.OrderBy(x => x.TypeRaw) : queryable.OrderByDescending(x => x.TypeRaw),
            "tag" => asc ? queryable.OrderBy(x => x.Tag) : queryable.OrderByDescending(x => x.Tag),
            "status" => asc
                ? queryable.OrderBy(x => string.IsNullOrWhiteSpace(x.StatusRaw) ? (x.AssignedUserId != null ? "Assigned" : "Available") : x.StatusRaw)
                : queryable.OrderByDescending(x => string.IsNullOrWhiteSpace(x.StatusRaw) ? (x.AssignedUserId != null ? "Assigned" : "Available") : x.StatusRaw),
            "assignedat" or "assigned" =>
                asc ? queryable.OrderBy(x => x.AssignedAtUtc) : queryable.OrderByDescending(x => x.AssignedAtUtc),
            _ => queryable.OrderByDescending(x => x.AssignedAtUtc).ThenBy(x => x.AssetName)
        };

        // ---------- Total before paging ----------
        var total = await queryable.CountAsync(ct);

        // ---------- Page slice ----------
        var slice = await queryable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Resolve names for the page
        var ids = slice.Where(r => r.AssignedUserId.HasValue)
                       .Select(r => r.AssignedUserId!.Value)
                       .Distinct()
                       .ToList();

        var userMap = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.UserID))
            .Select(u => new { u.UserID, u.FullName, u.EmployeeNumber })
            .ToDictionaryAsync(u => u.UserID, u => new { u.FullName, u.EmployeeNumber }, ct);

        var items = slice.Select(x =>
        {
            // Resolve assigned user safely (no unassigned local warnings)
            string assigned;
            string? empNum = null;
            string? empName = null;

            if (x.AssignedUserId.HasValue && userMap.TryGetValue(x.AssignedUserId.Value, out var uinfo))
            {
                assigned = $"{uinfo.FullName} ({uinfo.EmployeeNumber})";
                empNum = uinfo.EmployeeNumber;
                empName = uinfo.FullName;
            }
            else
            {
                assigned = "Unassigned";
            }

            var type = string.IsNullOrWhiteSpace(x.TypeRaw) ? "Software" : x.TypeRaw!;
            var status = !string.IsNullOrEmpty(x.StatusRaw)
                ? x.StatusRaw!
                : (x.AssignedUserId.HasValue ? "Assigned" : "Available");

            return new AssetRowVm
            {
                HardwareID = x.HardwareID,
                AssetName = x.AssetName ?? "",
                Type = type,
                Tag = x.Tag ?? "",
                AssignedTo = assigned,
                Status = status,

                // optional fields used by the client filter logic
                AssignedUserId = x.AssignedUserId,
                AssignedEmployeeNumber = empNum,
                AssignedEmployeeName = empName,
                AssignedAtUtc = x.AssignedAtUtc
            };
        }).ToList();

        // ---- Supervisor + Direct Reports (for dropdown) ----
        PersonVm? supervisorVm = null;
        List<PersonVm> reportVms = new();

        var meRow = await _db.Users.AsNoTracking()
            .Where(u => u.UserID == myUserId)
            .Select(u => new { u.UserID, u.FullName, u.EmployeeNumber })
            .FirstOrDefaultAsync(ct);

        if (meRow is not null)
        {
            supervisorVm = new PersonVm
            {
                UserID = meRow.UserID,
                Name = meRow.FullName,
                EmployeeNumber = meRow.EmployeeNumber
            };
        }

        // Always include direct reports for the current user so the UI can render the dropdown,
        // regardless of scope.
        reportVms = await _db.Users.AsNoTracking()
            .Where(u => u.SupervisorID == myUserId)
            .OrderBy(u => u.FullName)
            .Select(u => new PersonVm
            {
                UserID = u.UserID,
                Name = u.FullName,
                EmployeeNumber = u.EmployeeNumber
            })
            .ToListAsync(ct);

        // ---------- Final payload ----------
        var payload = new AssetsPagePayloadVm
        {
            Supervisor = supervisorVm,
            Reports = reportVms,
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        // Cache briefly to cut DB hits when the UI tweaks filters fast
        _cache.Set(key, payload, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromSeconds(30),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });

        var pageEtag = $"W/\"{total}-{items.FirstOrDefault()?.Tag}-{items.LastOrDefault()?.Tag}\"";
        Response.Headers.ETag = pageEtag;

        return Ok(payload);
    }

    [HttpGet("unique")]
    public async Task<IActionResult> unique()
    {
        var res = await _assetQuery.unique();
        return Ok(res);
    }
}
