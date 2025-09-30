using System.Security.Claims;
using AIMS.Data;
using AIMS.Models;
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
    private readonly IWebHostEnvironment _env;

    public AssetsApiController(AimsDbContext db, IMemoryCache cache, AssetQuery assetQuery, IWebHostEnvironment env)
    {
        _db = db;
        _cache = cache;
        _assetQuery = assetQuery;
        _env = env;
    }

    // --------------------------------------------------------------------
    // GET /api/assets/whoami?impersonate=28809      (DEV only)
    // Quick self-diagnosis endpoint: who am I, what role, my reports.
    // --------------------------------------------------------------------
    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI([FromQuery] string? impersonate = null, CancellationToken ct = default)
    {
        var (user, role) = await ResolveCurrentUserAsync(impersonate, ct);
        if (user is null)
            return Ok(new { user = (object?)null, role = (string?)null, devImpersonation = false });

        var reports = await _db.Users.AsNoTracking()
            .Where(u => u.SupervisorID == user.UserID)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.UserID, u.FullName, u.EmployeeNumber })
            .ToListAsync(ct);

        return Ok(new
        {
            user = new { user.UserID, user.FullName, user.Email, user.EmployeeNumber },
            role,
            devImpersonation = !string.IsNullOrWhiteSpace(impersonate) && _env.IsDevelopment(),
            directReportCount = reports.Count,
            directReports = reports
        });
    }

    // --------------------------------------------------------------------
    // GET /api/assets
    // page/pageSize/sort/dir/q/types/statuses/scope=my|reports|all&impersonate=...
    // --------------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = "asc",
        [FromQuery] string? q = null,
        [FromQuery] List<string>? types = null,
        [FromQuery] string? category = null, // alias: single category maps into `types`
        [FromQuery] List<string>? statuses = null,
        [FromQuery] string scope = "all", // "all" | "my" | "reports"
        [FromQuery] string? impersonate = null,
        [FromQuery] bool devBypass = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        // Normalize single 'category' into the existing 'types' list
        if (!string.IsNullOrWhiteSpace(category))
        {
            types ??= new List<string>();
            if (!string.IsNullOrWhiteSpace(category))
            {
                types ??= new List<string>();
                if (!types.Contains(category, StringComparer.OrdinalIgnoreCase))
                    types.Add(category);
            }
        }
        // Defensive: if a 'tag' is present, this is a single-asset ask → use /api/assets/one
        if (Request.Query.ContainsKey("tag"))
            return BadRequest(new { error = "Use /api/assets/one for single-asset lookups (by tag/hardwareId/softwareId)." });



        // ----- Resolve current user (supports DEV impersonation) -----
        var (me, myRole) = await ResolveCurrentUserAsync(impersonate, ct);
        var myRoleName = myRole ?? "Employee";
        var myUserId = me?.UserID ?? 0;
        var myEmpNumber = me?.EmployeeNumber ?? "unknown";
        var isAdminOrIt = myRoleName is "Admin" or "IT Help Desk";
        var bypass = _env.IsDevelopment() && devBypass;
        if (bypass)
        {
            // make the request behave like an admin in dev
            isAdminOrIt = true;
            scope = "all";
            Console.WriteLine("[devBypass] Skipping role scoping for /api/assets");
        }

        // Non-admins cannot request "all"
        if (!isAdminOrIt && scope == "all")
            scope = "my"; // non-admins cannot see "all"

        // ---- cache key includes version stamp so it auto-busts after assign/close ----
        var ver = CacheStamp.AssetsVersion;
        var key = $"assets:v{ver}:p{page}:s{pageSize}:sort{sort}:{dir}:q{q}:t{string.Join(',', types ?? new())}:st{string.Join(',', statuses ?? new())}:sc{scope}:u{myEmpNumber}";
        if (_cache.TryGetValue<AssetsPagePayloadVm>(key, out var cached))
        {
            var firstTag = cached!.Items.FirstOrDefault()?.Tag ?? string.Empty;
            var lastTag = cached.Items.LastOrDefault()?.Tag ?? string.Empty;
            var etag = $"W/\"{cached.Total}-{firstTag}-{lastTag}\"";

            if (Request.Headers.TryGetValue("If-None-Match", out var inm) && inm.Contains(etag))
                return StatusCode(304);

            Response.Headers.ETag = etag;
            return Ok(cached);
        }

        // ---------- Base dataset (hardware + software) ----------
        var activeAssignments = _db.Assignments.AsNoTracking()
            .Where(a => a.UnassignedAtUtc == null)
            .Select(a => new { a.AssetKind, a.AssetTag, a.SoftwareID, a.UserID, a.AssignedAtUtc });

        var hardwareBase =
            from h in _db.HardwareAssets.AsNoTracking()
            join aa in activeAssignments.Where(a => a.AssetKind == AssetKind.Hardware)
                on h.HardwareID equals aa.AssetTag into ha
            from aa in ha.DefaultIfEmpty()
            select new
            {
                AssetName = h.AssetName,
                TypeRaw = h.AssetType,
                Tag = h.SerialNumber,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = h.Status,
                AssignedAtUtc = (DateTime?)aa.AssignedAtUtc,
                Comment = h.Comment,
                SoftwareID = (int?)null,
                HardwareID = (int?)h.HardwareID // Nullable for now.may need a unified dto that includes IDs. Edits in db are called using the ID
            };

        var softwareBase =
            from s in _db.SoftwareAssets.AsNoTracking()
            join aa in activeAssignments.Where(a => a.AssetKind == AssetKind.Software)
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
                Comment = s.Comment,
                SoftwareID = (int?)s.SoftwareID,
                HardwareID = (int?)null
            };

        var queryable = hardwareBase.Concat(softwareBase);

        // ---------- Role/Ownership scope ----------
        if (!isAdminOrIt)
        {
            if (scope == "my")
            {
                queryable = queryable.Where(x => x.AssignedUserId == myUserId);
            }
            else // "reports" => me + my direct reports
            {
                var reportIds = await _db.Users.AsNoTracking()
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
        {
            var tset = types
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            queryable = queryable.Where(x =>
                tset.Contains(string.IsNullOrWhiteSpace(x.TypeRaw) ? "Software" : x.TypeRaw!));
        }

        if (statuses is { Count: > 0 })
            queryable = queryable.Where(x =>
                (!string.IsNullOrEmpty(x.StatusRaw) && statuses.Contains(x.StatusRaw)) ||
                (string.IsNullOrEmpty(x.StatusRaw) && statuses.Contains(x.AssignedUserId != null ? "Assigned" : "Available")));

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

        // ---------- Total + slice ----------
        var total = await queryable.CountAsync(ct);
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
            string assigned = "Unassigned";
            string? empNum = null, empName = null;

            if (x.AssignedUserId.HasValue && userMap.TryGetValue(x.AssignedUserId.Value, out var uinfo))
            {
                assigned = $"{uinfo.FullName} ({uinfo.EmployeeNumber})";
                empNum = uinfo.EmployeeNumber;
                empName = uinfo.FullName;
            }

            var type = string.IsNullOrWhiteSpace(x.TypeRaw) ? "Software" : x.TypeRaw!;
            var status = !string.IsNullOrEmpty(x.StatusRaw)
                ? x.StatusRaw!
                : (x.AssignedUserId.HasValue ? "Assigned" : "Available");

            return new AssetRowVm
            {
                HardwareID = x.HardwareID,
                SoftwareID = x.SoftwareID,
                Comment = x.Comment,
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
        if (me is not null)
        {
            supervisorVm = new PersonVm
            {
                UserID = me.UserID,
                Name = me.FullName,
                EmployeeNumber = me.EmployeeNumber
            };
        }

        // Always include direct reports for the current user so the UI can render the dropdown,
        // regardless of scope.
        var reportVms = await _db.Users.AsNoTracking()
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

    [HttpGet("types/unique")]
    public async Task<IActionResult> unique()
    {
        var res = await _assetQuery.unique();
        return Ok(res);
    }

    // --------------------------------------------------------------------
    // GET /api/assets/one?tag=CC-0019 | hardwareId=123 | softwareId=7 [&impersonate=...] [&devBypass=true]
    // Returns a single AssetRowVm (role scoped: Admin/IT can fetch any; others only “my or reports”).
    // In Development, passing devBypass=true skips the role scope (useful for UI testing).
    // --------------------------------------------------------------------
    [HttpGet("one")]
    public async Task<ActionResult<AssetRowVm>> GetOne(
        [FromQuery] string? tag = null,
        [FromQuery] int? hardwareId = null,
        [FromQuery] int? softwareId = null,
        [FromQuery] string? impersonate = null,
        [FromQuery] bool devBypass = false,
        CancellationToken ct = default)
    {
        var (me, myRole) = await ResolveCurrentUserAsync(impersonate, ct);
        var isAdminOrIt = myRole is "Admin" or "IT Help Desk";
        var myUserId = me?.UserID ?? 0;

        if (string.IsNullOrWhiteSpace(tag) && hardwareId is null && softwareId is null)
            return BadRequest(new { error = "Provide tag OR hardwareId OR softwareId." });

        // -------------- Try HARDWARE --------------
        if (!string.IsNullOrWhiteSpace(tag) || hardwareId is not null)
        {
            var t = tag?.Trim();
            var hw = await (
                from h in _db.HardwareAssets.AsNoTracking()
                where (t != null && h.SerialNumber == t) || (hardwareId != null && h.HardwareID == hardwareId)
                let a = _db.Assignments
                            .Where(x => x.AssetKind == AssetKind.Hardware && x.AssetTag == h.HardwareID && x.UnassignedAtUtc == null)
                            .OrderByDescending(x => x.AssignedAtUtc)
                            .FirstOrDefault()
                select new
                {
                    h.HardwareID,
                    AssetName = h.AssetName,
                    Type = h.AssetType,
                    Tag = h.SerialNumber,
                    Status = string.IsNullOrEmpty(h.Status)
                                ? (a != null ? "Assigned" : "Available")
                                : h.Status,
                    AssignedUserId = a != null ? (int?)a.UserID : null
                }
            ).FirstOrDefaultAsync(ct);

            if (hw is not null)
            {
                // Role scope unless devBypass in Development
                if (!isAdminOrIt && !(_env.IsDevelopment() && devBypass))
                {
                    bool allowed = false;
                    if (hw.AssignedUserId == myUserId) allowed = true;
                    else if (hw.AssignedUserId is int uid)
                    {
                        var reportIds = await _db.Users.AsNoTracking()
                            .Where(u => u.SupervisorID == myUserId)
                            .Select(u => u.UserID)
                            .ToListAsync(ct);
                        allowed = reportIds.Contains(uid);
                    }
                    if (!allowed) return NotFound();
                }

                string assignedDisplay = "Unassigned";
                string? empNum = null, empName = null;
                if (hw.AssignedUserId is int uid2)
                {
                    var u = await _db.Users.AsNoTracking()
                        .Where(x => x.UserID == uid2)
                        .Select(x => new { x.FullName, x.EmployeeNumber })
                        .FirstOrDefaultAsync(ct);
                    if (u is not null)
                    {
                        assignedDisplay = $"{u.FullName} ({u.EmployeeNumber})";
                        empNum = u.EmployeeNumber; empName = u.FullName;
                    }
                }

                var dto = new AssetRowVm
                {
                    HardwareID = hw.HardwareID,
                    SoftwareID = null,
                    AssetName = hw.AssetName ?? "",
                    Type = string.IsNullOrWhiteSpace(hw.Type) ? "Hardware" : hw.Type!,
                    Tag = hw.Tag ?? "",
                    AssignedTo = assignedDisplay,
                    Status = hw.Status ?? "Available",
                    AssignedUserId = hw.AssignedUserId,
                    AssignedEmployeeNumber = empNum,
                    AssignedEmployeeName = empName,
                    AssignedAtUtc = null
                };
                var etagHw = $"W/\"{dto.Tag}-{dto.Status}-{dto.AssignedUserId?.ToString() ?? "none"}\"";
                if (Request.Headers.TryGetValue("If-None-Match", out var inmHw) && inmHw.Contains(etagHw))
                    return StatusCode(304);
                Response.Headers.ETag = etagHw;
                return Ok(dto);
            }
        }

        // -------------- Try SOFTWARE --------------
        if (!string.IsNullOrWhiteSpace(tag) || softwareId is not null)
        {
            var t = tag?.Trim();
            var sw = await (
                from s in _db.SoftwareAssets.AsNoTracking()
                where (t != null && s.SoftwareLicenseKey == t) || (softwareId != null && s.SoftwareID == softwareId)
                let a = _db.Assignments
                            .Where(x => x.AssetKind == AssetKind.Software && x.SoftwareID == s.SoftwareID && x.UnassignedAtUtc == null)
                            .OrderByDescending(x => x.AssignedAtUtc)
                            .FirstOrDefault()
                select new
                {
                    s.SoftwareID,
                    AssetName = s.SoftwareName,
                    Type = s.SoftwareType,
                    Tag = s.SoftwareLicenseKey,
                    Status = a != null ? "Assigned" : "Available",
                    AssignedUserId = a != null ? (int?)a.UserID : null
                }
            ).FirstOrDefaultAsync(ct);

            if (sw is not null)
            {
                if (!isAdminOrIt && !(_env.IsDevelopment() && devBypass))
                {
                    bool allowed = false;
                    if (sw.AssignedUserId == myUserId) allowed = true;
                    else if (sw.AssignedUserId is int uid)
                    {
                        var reportIds = await _db.Users.AsNoTracking()
                            .Where(u => u.SupervisorID == myUserId)
                            .Select(u => u.UserID)
                            .ToListAsync(ct);
                        allowed = reportIds.Contains(uid);
                    }
                    if (!allowed) return NotFound();
                }

                string assignedDisplay = "Unassigned";
                string? empNum = null, empName = null;
                if (sw.AssignedUserId is int uid2)
                {
                    var u = await _db.Users.AsNoTracking()
                        .Where(x => x.UserID == uid2)
                        .Select(x => new { x.FullName, x.EmployeeNumber })
                        .FirstOrDefaultAsync(ct);
                    if (u is not null)
                    {
                        assignedDisplay = $"{u.FullName} ({u.EmployeeNumber})";
                        empNum = u.EmployeeNumber; empName = u.FullName;
                    }
                }

                var dto = new AssetRowVm
                {
                    HardwareID = null,
                    SoftwareID = sw.SoftwareID,
                    AssetName = sw.AssetName ?? "",
                    Type = string.IsNullOrWhiteSpace(sw.Type) ? "Software" : sw.Type!,
                    Tag = sw.Tag ?? "",
                    AssignedTo = assignedDisplay,
                    Status = sw.Status ?? "Available",
                    AssignedUserId = sw.AssignedUserId,
                    AssignedEmployeeNumber = empNum,
                    AssignedEmployeeName = empName,
                    AssignedAtUtc = null
                };
                var etagSw = $"W/\"{dto.Tag}-{dto.Status}-{dto.AssignedUserId?.ToString() ?? "none"}\"";
                if (Request.Headers.TryGetValue("If-None-Match", out var inmSw) && inmSw.Contains(etagSw))
                    return StatusCode(304);
                Response.Headers.ETag = etagSw;
                return Ok(dto);
            }
        }

        return NotFound();
    }

    // --------------------------------------------------------------------
    // Helper: real user or DEV impersonation (by employee # or email)
    // --------------------------------------------------------------------
    private async Task<(AIMS.Models.User? user, string? role)> ResolveCurrentUserAsync(string? impersonate, CancellationToken ct)
    {
        // DEV impersonation: /api/assets?impersonate=28809 or email
        if (!string.IsNullOrWhiteSpace(impersonate) && _env.IsDevelopment())
        {
            var key = impersonate.Trim();
            var imp = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeNumber == key || u.Email == key, ct);

            if (imp is not null)
            {
                var roleName = await _db.Roles.AsNoTracking()
                    .Where(r => r.RoleID == imp.RoleID)
                    .Select(r => r.RoleName)
                    .FirstOrDefaultAsync(ct);
                return (imp, roleName);
            }
        }

        // Normal resolution from claims
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var emp = User.FindFirstValue("employeeNumber");

        var me = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u =>
                (!string.IsNullOrEmpty(email) && u.Email == email) ||
                (!string.IsNullOrEmpty(emp) && u.EmployeeNumber == emp), ct);

        if (me is null) return (null, null);

        var myRole = await _db.Roles.AsNoTracking()
            .Where(r => r.RoleID == me.RoleID)
            .Select(r => r.RoleName)
            .FirstOrDefaultAsync(ct);

        return (me, myRole);
    }
}
