using System.Security.Claims;
using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Common;
using AIMS.Utilities;                 // ClaimsPrincipalExtensions (IsAdminOrHelpdesk / IsSupervisor)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Queries;

public sealed class AssetSearchQuery
{
    private readonly AimsDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _http;
    private readonly IHostEnvironment _env;

    public AssetSearchQuery(
        AimsDbContext db,
        IMemoryCache cache,
        IHttpContextAccessor http,
        IHostEnvironment env)
    {
        _db = db;
        _cache = cache;
        _http = http;
        _env = env;
    }

    public async Task<PagedResult<AssetRowDto>> SearchAsync(
        string? q,
        string? type,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default,
        string? category = null,
        PagingTotals totalsMode = PagingTotals.Exact,
        bool showArchived = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var norm = (q ?? string.Empty).Trim();
        var hasQ = norm.Length > 0;

        if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(category))
            type = category;


        var countFiltered = await _db.HardwareAssets.CountAsync();
        var countIgnored = await _db.HardwareAssets.IgnoreQueryFilters().CountAsync();

        // ----- building base IQueryable based on Archived filter ---------------
        var hardwareQuery = showArchived
        ? _db.HardwareAssets.AsNoTracking().IgnoreQueryFilters()
        : _db.HardwareAssets.AsNoTracking();

        var softwareQuery = showArchived
        ? _db.SoftwareAssets.AsNoTracking().IgnoreQueryFilters()
        : _db.SoftwareAssets.AsNoTracking();


        // ---------------- Base projection (Hardware ∪ Software) ----------------
        IQueryable<AssetRowDto> baseQ =
            hardwareQuery.Select(h => new AssetRowDto
            {
                HardwareID = h.HardwareID,
                SoftwareID = null,
                AssetName = h.AssetName ?? "",
                Type = h.AssetType ?? "",
                Tag = h.SerialNumber ?? "",
                IsArchived = h.IsArchived,

                Status = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.HardwareID == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Any()
                        ? "Assigned"
                        : (string.IsNullOrWhiteSpace(h.Status) ? "Available" : h.Status),

                AssignedTo = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.HardwareID == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User != null ? a.User.FullName : null)
                    .FirstOrDefault() ?? "Unassigned",

                AssignedUserId = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.HardwareID == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => (int?)a.UserID)
                    .FirstOrDefault(),

                AssignedEmployeeNumber = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.HardwareID == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User != null ? a.User.EmployeeNumber : null)
                    .FirstOrDefault(),

                AssignedEmployeeName = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.HardwareID == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User != null ? a.User.FullName : null)
                    .FirstOrDefault(),

                AssignedAtUtc = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.HardwareID == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => (DateTime?)a.AssignedAtUtc)
                    .FirstOrDefault()
            })
            .Concat(
            softwareQuery.Select(s => new AssetRowDto
            {
                HardwareID = null,
                SoftwareID = s.SoftwareID,
                AssetName = s.SoftwareName ?? "",
                Type = s.SoftwareType ?? "",
                Tag = s.SoftwareLicenseKey ?? "",
                IsArchived = s.IsArchived,

                Status = s.IsArchived
                    ? "Archived"
                    : _db.Assignments
                        .Where(a => a.AssetKind == Models.AssetKind.Software
                                 && a.SoftwareID == s.SoftwareID
                                 && a.UnassignedAtUtc == null)
                        .Any() ? "Assigned" : "Available",

                AssignedTo = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User != null ? a.User.FullName : null)
                    .FirstOrDefault() ?? "Unassigned",

                AssignedUserId = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => (int?)a.UserID)
                    .FirstOrDefault(),

                AssignedEmployeeNumber = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User != null ? a.User.EmployeeNumber : null)
                    .FirstOrDefault(),

                AssignedEmployeeName = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User != null ? a.User.FullName : null)
                    .FirstOrDefault(),

                AssignedAtUtc = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => (DateTime?)a.AssignedAtUtc)
                    .FirstOrDefault()
            }));

        // ----- Role scoping (ALWAYS APPLIED) -----
        baseQ = await ScopeByRoleAsync(baseQ, ct);

        // ----- Facets -----
        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim().ToLower();
            baseQ = baseQ.Where(a => a.Type != null && a.Type.ToLower() == t);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLower();
            baseQ = baseQ.Where(a => a.Status != null && a.Status.ToLower() == s);
        }

        // ----- LIKE patterns (when q present) -----
        IQueryable<AssetRowDto> finalQ;
        if (hasQ)
        {
            var likeExact = EscapeLike(norm);
            var likePrefix = EscapeLike(norm) + "%";
            var likeContains = "%" + EscapeLike(norm) + "%";

            var exactQ = baseQ.Where(a =>
                EF.Functions.Like(a.AssetName ?? "", likeExact) ||
                EF.Functions.Like(a.Tag ?? "", likeExact) ||
                EF.Functions.Like(a.Type ?? "", likeExact) ||
                EF.Functions.Like(a.Status ?? "", likeExact) ||
                EF.Functions.Like(a.AssignedEmployeeName ?? "", likeExact) ||
                EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likeExact));

            var prefixQ = baseQ.Where(a =>
                EF.Functions.Like(a.AssetName ?? "", likePrefix) ||
                EF.Functions.Like(a.Tag ?? "", likePrefix) ||
                EF.Functions.Like(a.Type ?? "", likePrefix) ||
                EF.Functions.Like(a.Status ?? "", likePrefix) ||
                EF.Functions.Like(a.AssignedEmployeeName ?? "", likePrefix) ||
                EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likePrefix));

            var containsQ = baseQ.Where(a =>
                EF.Functions.Like(a.AssetName ?? "", likeContains) ||
                EF.Functions.Like(a.Tag ?? "", likeContains) ||
                EF.Functions.Like(a.Type ?? "", likeContains) ||
                EF.Functions.Like(a.Status ?? "", likeContains) ||
                EF.Functions.Like(a.AssignedEmployeeName ?? "", likeContains) ||
                EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likeContains));

            finalQ = exactQ.Union(prefixQ).Union(containsQ);
        }
        else
        {
            finalQ = baseQ;
        }

        // ----- Consistent ordering -----
        finalQ = finalQ
            .OrderBy(a => a.AssetName)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Tag)
            .ThenBy(a => a.HardwareID)
            .ThenBy(a => a.SoftwareID);

        // ----- Cache key base -----
        var scopeKey = await GetScopeCacheKeyAsync(ct);
        var stamp = CacheStamp.AssetsVersion;
        var cacheKeyBase =
            $"assets:search:v={stamp}:scope={scopeKey}:q={norm.ToLower()}|type={type?.ToLower() ?? ""}|status={status?.ToLower() ?? ""}|archived={showArchived}";

        return totalsMode == PagingTotals.Exact
            ? await Paging.PageExactCachedAsync(_cache, cacheKeyBase, finalQ, page, pageSize, ct)
            : await Paging.PageLookAheadCachedAsync(_cache, cacheKeyBase, finalQ, page, pageSize, ct);
    }

    private static string EscapeLike(string input) =>
        input.Replace("\\", "\\\\").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private async Task<IQueryable<AssetRowDto>> ScopeByRoleAsync(IQueryable<AssetRowDto> q, CancellationToken ct)
    {
        var http = _http.HttpContext;

        if (http?.User != null && http.User.IsAdminOrHelpdesk())
            return q;

        var (user, roleName) = await ResolveCurrentUserAsync(ct);
        if (user is null) return q.Where(_ => false);

        if (roleName is "Admin" or "IT Help Desk")
            return q;

        if (roleName is "Supervisor")
        {
            var scopeIds = await AIMS.Utilities.SupervisorScopeHelper
                .GetSupervisorScopeUserIdsAsync(_db, user.UserID, _cache, ct);

            return q.Where(a => a.AssignedUserId.HasValue && scopeIds.Contains(a.AssignedUserId.Value));
        }

        return q.Where(_ => false);
    }

    private async Task<string> GetScopeCacheKeyAsync(CancellationToken ct)
    {
        var http = _http.HttpContext;
        if (http?.User != null && http.User.IsAdminOrHelpdesk())
            return "admin";

        var (user, roleName) = await ResolveCurrentUserAsync(ct);
        if (user is null) return "anon";

        if (roleName is "Admin" or "IT Help Desk") return "admin";

        if (roleName is "Supervisor")
        {
            var ids = await AIMS.Utilities.SupervisorScopeHelper
                .GetSupervisorScopeUserIdsAsync(_db, user.UserID, _cache, ct);

            var min = ids.DefaultIfEmpty(0).Min();
            var max = ids.DefaultIfEmpty(0).Max();
            return $"sup:{user.UserID}:{ids.Count}:{min}-{max}";
        }

        return $"user:{user.UserID}";
    }

    public async Task<(AIMS.Models.User? user, string? roleName)> ResolveCurrentUserAsync(CancellationToken ct)
    {
        var http = _http.HttpContext;
        if (http is null) return (null, null);

        var impUserId = http.Items["ImpersonatedUserId"] as int?;
        var impEmail = http.Items["ImpersonatedEmail"] as string;

        AIMS.Models.User? user = null;

        if (impUserId is not null)
        {
            user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == impUserId.Value, ct);
        }
        else if (!string.IsNullOrWhiteSpace(impEmail))
        {
            user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == impEmail, ct);
        }
        else
        {
            var email =
                http.User.FindFirst("preferred_username")?.Value
                ?? http.User.FindFirstValue(ClaimTypes.Email)
                ?? http.User.Identity?.Name;

            var emp =
                http.User.FindFirst("employee_number")?.Value
                ?? http.User.FindFirst("employeeNumber")?.Value;

            if (!string.IsNullOrWhiteSpace(email))
                user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user is null && !string.IsNullOrWhiteSpace(emp))
                user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.EmployeeNumber == emp, ct);
        }

        if (user is null) return (null, null);

        var roleName = await _db.Roles.AsNoTracking()
            .Where(r => r.RoleID == user.RoleID)
            .Select(r => r.RoleName)
            .FirstOrDefaultAsync(ct);

        return (user, roleName);
    }
}
