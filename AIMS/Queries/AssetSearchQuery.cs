using System.Security.Claims;
using AIMS.Data;
using AIMS.Utilities;                 // ClaimsPrincipalExtensions (IsAdminOrHelpdesk / IsSupervisor)
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

    public async Task<PagedResult<AssetRowVm>> SearchAsync(
        string? q,
        string? type,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default,
        string? category = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var norm = (q ?? string.Empty).Trim();
        var hasQ = norm.Length > 0;

        // Normalize single 'category' into 'type' if 'type' not supplied
        if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(category))
            type = category;

        // ---------------- Base projection (Hardware ∪ Software) ----------------
        IQueryable<AssetRowVm> baseQ =
            _db.HardwareAssets.AsNoTracking().Select(h => new AssetRowVm
            {
                HardwareID = h.HardwareID,
                SoftwareID = null,
                AssetName = h.AssetName ?? "",
                Type = h.AssetType ?? "",
                Tag = h.SerialNumber ?? "",

                // >>> IMPORTANT: Derive from open assignment FIRST; else fall back to stored status
                Status = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.AssetTag == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Any()
                        ? "Assigned"
                        : (string.IsNullOrWhiteSpace(h.Status) ? "Available" : h.Status),

                // Pull the current assignee info (if any open assignment exists)
                AssignedTo = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.AssetTag == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
                    .FirstOrDefault() ?? "Unassigned",

                AssignedUserId = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.AssetTag == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => (int?)a.UserID)
                    .FirstOrDefault(),

                AssignedEmployeeNumber = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.AssetTag == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User.EmployeeNumber)
                    .FirstOrDefault(),

                AssignedEmployeeName = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.AssetTag == h.HardwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
                    .FirstOrDefault(),

                AssignedAtUtc = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Hardware
                             && a.AssetTag == h.HardwareID
                             && a.UnassignedAtUtc == null)
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

                // Software already derived from assignments
                Status = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Any() ? "Assigned" : "Available",

                AssignedTo = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
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
                    .Select(a => a.User.EmployeeNumber)
                    .FirstOrDefault(),

                AssignedEmployeeName = _db.Assignments
                    .Where(a => a.AssetKind == Models.AssetKind.Software
                             && a.SoftwareID == s.SoftwareID
                             && a.UnassignedAtUtc == null)
                    .Select(a => a.User.FullName)
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

        // ----- Facets (EF-translatable, case-insensitive by ToLower) -----
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

        // ----- Blank query → first page -----
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

        // ----- LIKE patterns -----
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

    /// <summary>
    /// Role-based scoping:
    /// - Admin/Helpdesk (claims or DB role) → no filter
    /// - Supervisor (DB role) → only assets assigned to { self + direct reports }
    /// - Everyone else  → no results
    /// </summary>
    private async Task<IQueryable<AssetRowVm>> ScopeByRoleAsync(IQueryable<AssetRowVm> q, CancellationToken ct)
    {
        var http = _http.HttpContext;

        // If the current principal is Admin/Helpdesk by claims, show all
        if (http?.User != null && http.User.IsAdminOrHelpdesk())
            return q;

        // Otherwise, resolve DB user + role
        var (user, roleName) = await ResolveCurrentUserAsync(ct);
        if (user is null) return q.Where(_ => false);

        // DB Admin/Helpdesk → all
        if (roleName is "Admin" or "IT Help Desk")
            return q;

        // Supervisor → self + direct reports (filter by AssignedUserId, matches API behavior)
        if (roleName is "Supervisor")
        {
            var scopeIds = await AIMS.Utilities.SupervisorScopeHelper
                .GetSupervisorScopeUserIdsAsync(_db, user.UserID, _cache, ct);

            return q.Where(a => a.AssignedUserId.HasValue && scopeIds.Contains(a.AssignedUserId.Value));
        }

        // Everyone else → nothing
        return q.Where(_ => false);
    }

    // Temporary: public so controllers/dev can validate. Keep as-is if useful.
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
