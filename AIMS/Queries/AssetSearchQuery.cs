using System.Security.Claims;
using AIMS.Data;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Queries;

public sealed class AssetSearchQuery
{
    private readonly AimsDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _http;
    private readonly IHostEnvironment _env;

    public AssetSearchQuery(AimsDbContext db, IMemoryCache cache, IHttpContextAccessor http, IHostEnvironment env)
    {
        _db = db;
        _cache = cache;
        _http = http;
        _env = env;
    }

    public async Task<PagedResult<AssetRowVm>> SearchAsync(
        string? q,
        string? type,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default,
        string? category = null) // alias → flows into `type`
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var norm = (q ?? string.Empty).Trim();
        var hasQ = norm.Length > 0;

        // Normalize single 'category' into 'type' if 'type' not supplied
        if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(category))
        {
            type = category;
        }

        // ---------------- Base projection (Hardware ∪ Software) ----------------
        IQueryable<AssetRowVm> baseQ =
            _db.HardwareAssets.AsNoTracking().Select(h => new AssetRowVm
            {
                HardwareID = h.HardwareID,
                SoftwareID = null,
                AssetName = h.AssetName ?? "",
                Type = h.AssetType ?? "",
                Tag = h.SerialNumber ?? "",
                Status = string.IsNullOrWhiteSpace(h.Status)
                    ? (_db.Assignments.Any(a => a.AssetKind == Models.AssetKind.Hardware && a.AssetTag == h.HardwareID && a.UnassignedAtUtc == null)
                        ? "Assigned" : "Available")
                    : h.Status,

                AssignedTo = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware && a.AssetTag == h.HardwareID && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
                    .FirstOrDefault() ?? "Unassigned",

                AssignedUserId = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware && a.AssetTag == h.HardwareID && a.UnassignedAtUtc == null)
                    .Select(a => (int?)a.UserID)
                    .FirstOrDefault(),

                AssignedEmployeeNumber = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware && a.AssetTag == h.HardwareID && a.UnassignedAtUtc == null)
                    .Select(a => a.User.EmployeeNumber)
                    .FirstOrDefault(),

                AssignedEmployeeName = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware && a.AssetTag == h.HardwareID && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
                    .FirstOrDefault(),

                AssignedAtUtc = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware && a.AssetTag == h.HardwareID && a.UnassignedAtUtc == null)
                    .Select(a => (DateTime?)a.AssignedAtUtc)
                    .FirstOrDefault()
            })
            .Concat(
            _db.SoftwareAssets.AsNoTracking().Select(s => new AssetRowVm
            {
                HardwareID = null,
                SoftwareID = s.SoftwareID,
                AssetName = s.SoftwareName ?? "",
                Type = s.SoftwareType ?? "",
                Tag = s.SoftwareLicenseKey ?? "",

                // Derive software Status from open assignment
                Status = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Any() ? "Assigned" : "Available",

                AssignedTo = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software && a.SoftwareID == s.SoftwareID && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
                    .FirstOrDefault() ?? "Unassigned",

                AssignedUserId = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software && a.SoftwareID == s.SoftwareID && a.UnassignedAtUtc == null)
                    .Select(a => (int?)a.UserID)
                    .FirstOrDefault(),

                AssignedEmployeeNumber = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software && a.SoftwareID == s.SoftwareID && a.UnassignedAtUtc == null)
                    .Select(a => a.User.EmployeeNumber)
                    .FirstOrDefault(),

                AssignedEmployeeName = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software && a.SoftwareID == s.SoftwareID && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
                    .FirstOrDefault(),

                AssignedAtUtc = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software && a.SoftwareID == s.SoftwareID && a.UnassignedAtUtc == null)
                    .Select(a => (DateTime?)a.AssignedAtUtc)
                    .FirstOrDefault()
            }));

        // Role scoping
        baseQ = await ScopeByRoleAsync(baseQ, ct);

        // Facets (case-insensitive for safety)
        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim();
            baseQ = baseQ.Where(a => a.Type != null && a.Type.Equals(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim();
            baseQ = baseQ.Where(a => a.Status != null && a.Status.Equals(s, StringComparison.OrdinalIgnoreCase));
        }

        // Empty q → return page (role-scoped)
        if (!hasQ)
        {
            var pageItems = await baseQ
                .OrderBy(a => a.AssetName)
                .ThenBy(a => a.Type)
                .ThenBy(a => a.Tag)
                .ThenBy(a => a.HardwareID)
                .ThenBy(a => a.SoftwareID)
                .Take(pageSize + 1)
                .ToListAsync(ct);

            return PagedResult<AssetRowVm>.From(pageItems, pageSize);
        }

        // LIKE pattern prep
        var likeExact = EscapeLike(norm);
        var likePrefix = EscapeLike(norm) + "%";
        var likeContains = "%" + EscapeLike(norm) + "%";

        // 1) exact
        var exactQ = baseQ.Where(a =>
            EF.Functions.Like(a.AssetName ?? "", likeExact) ||
            EF.Functions.Like(a.Tag ?? "", likeExact) ||
            EF.Functions.Like(a.Type ?? "", likeExact) ||
            EF.Functions.Like(a.Status ?? "", likeExact) ||
            EF.Functions.Like(a.AssignedEmployeeName ?? "", likeExact) ||
            EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likeExact));

        var exact = await exactQ
            .OrderBy(a => a.AssetName)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Tag)
            .ThenBy(a => a.HardwareID)
            .ThenBy(a => a.SoftwareID)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        if (exact.Count >= pageSize || norm.Length < 3)
            return PagedResult<AssetRowVm>.From(exact, pageSize);

        // 2) prefix
        var prefixQ = baseQ.Where(a =>
            EF.Functions.Like(a.AssetName ?? "", likePrefix) ||
            EF.Functions.Like(a.Tag ?? "", likePrefix) ||
            EF.Functions.Like(a.Type ?? "", likePrefix) ||
            EF.Functions.Like(a.Status ?? "", likePrefix) ||
            EF.Functions.Like(a.AssignedEmployeeName ?? "", likePrefix) ||
            EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likePrefix));

        var prefix = await prefixQ
            .OrderBy(a => a.AssetName)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Tag)
            .ThenBy(a => a.HardwareID)
            .ThenBy(a => a.SoftwareID)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        if (prefix.Count >= pageSize)
            return PagedResult<AssetRowVm>.From(prefix, pageSize);

        // 3) contains
        var containsQ = baseQ.Where(a =>
            EF.Functions.Like(a.AssetName ?? "", likeContains) ||
            EF.Functions.Like(a.Tag ?? "", likeContains) ||
            EF.Functions.Like(a.Type ?? "", likeContains) ||
            EF.Functions.Like(a.Status ?? "", likeContains) ||
            EF.Functions.Like(a.AssignedEmployeeName ?? "", likeContains) ||
            EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likeContains));

        var contains = await containsQ
            .OrderBy(a => a.AssetName)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Tag)
            .ThenBy(a => a.HardwareID)
            .ThenBy(a => a.SoftwareID)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        return PagedResult<AssetRowVm>.From(contains, pageSize);
    }

    private static string EscapeLike(string input) =>
        input
            .Replace("\\", "\\\\")
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");

    private async Task<IQueryable<AssetRowVm>> ScopeByRoleAsync(IQueryable<AssetRowVm> q, CancellationToken ct)
    {
        var (user, roleName) = await ResolveCurrentUserAsync(ct);
        if (user is null) return q;

        if (roleName is "Admin" or "IT Help Desk")
            return q;

        if (roleName is "Supervisor")
        {
            var cacheKey = $"scopeIds:supervisor:{user.UserID}";
            if (!_cache.TryGetValue(cacheKey, out List<int>? scopeIds))
            {
                scopeIds = await _db.Users
                    .Where(u => u.UserID == user.UserID || u.SupervisorID == user.UserID)
                    .Select(u => u.UserID)
                    .ToListAsync(ct);

                _cache.Set(cacheKey, scopeIds, TimeSpan.FromMinutes(5));
            }

            return q.Where(a => a.AssignedUserId.HasValue && scopeIds!.Contains(a.AssignedUserId.Value));
        }

        // All other roles → no results (treated like anonymous)
        return q.Where(_ => false);
    }


    // Temporary: public so controllers can impersonate during dev/testing.
    // TODO: Make this private/internal once real user identity is wired up.
    public async Task<(AIMS.Models.User? user, string? roleName)> ResolveCurrentUserAsync(CancellationToken ct)
    {
        var http = _http.HttpContext;
        if (http is null) return (null, null);

        // Dev impersonation
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
            // Claims-based resolution
            var email = http.User.FindFirstValue(ClaimTypes.Email) ?? http.User.Identity?.Name;
            var emp = http.User.FindFirst("employee_number")?.Value;

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
