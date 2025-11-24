using System.Security.Claims;
using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Assignments;
using AIMS.Dtos.Common;
using AIMS.Models;
using AIMS.Utilities;                 // ClaimsPrincipalExtensions (IsAdminOrHelpdesk / IsSupervisor)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Queries;

/// <summary>
/// Executes asset search queries across hardware and software, applying
/// role-based scoping, filters, search terms, and paging.
/// </summary>
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

    /// <summary>
    /// Internal container for normalized search parameters.
    /// </summary>
    private sealed record SearchInputs(
        int Page,
        int PageSize,
        string NormalizedQuery,
        bool HasQuery,
        string? EffectiveType,
        string? Status);

    // ------------------------------------------------------------------------
    // PUBLIC: SearchAsync
    // ------------------------------------------------------------------------

    /// <summary>
    /// Performs a paged asset search over hardware and software.
    /// The method:
    ///  - Normalizes incoming parameters
    ///  - Builds a base hardware/software projection
    ///  - Applies role-based scoping
    ///  - Applies facet filters (type, status)
    ///  - Applies text search filters (if a query is present)
    ///  - Orders results and returns a paged result set
    /// </summary>
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
        // 1) Normalize incoming parameters
        var inputs = NormalizeSearchInputs(q, type, status, page, pageSize, category);

        // 2) Build base union of hardware + software, respecting archived flag
        var baseQ = BuildBaseQuery(showArchived);

        // 3) Apply role scoping (admin/helpdesk/supervisor/normal user)
        baseQ = await ScopeByRoleAsync(baseQ, ct);

        // 4) Apply facets (type/status)
        baseQ = ApplyFacetFilters(baseQ, inputs.EffectiveType, inputs.Status);

        // 5) Apply LIKE search if a query is present (plural/singular handling)
        var finalQ = inputs.HasQuery
            ? ApplyQueryFilters(baseQ, inputs.NormalizedQuery)
            : baseQ;

        // 6) Apply consistent ordering
        finalQ = ApplyOrdering(finalQ);

        // 7) Build cache key and page via the existing Paging helpers
        var cacheKeyBase = await BuildCacheKeyBaseAsync(
            scopeKey: await GetScopeCacheKeyAsync(ct),
            norm: inputs.NormalizedQuery,
            type: inputs.EffectiveType,
            status: inputs.Status,
            showArchived: showArchived);

        var paged = await PageWithTotalsAsync(
            finalQ,
            cacheKeyBase,
            inputs.Page,
            inputs.PageSize,
            totalsMode,
            ct);

        await HydrateSeatAssignmentsAsync(paged.Items, ct);
        return paged;
    }

    // ------------------------------------------------------------------------
    //  Helpers for SearchAsync
    // ------------------------------------------------------------------------

    /// <summary>
    /// Normalizes all search parameters and applies bounds on page and page size.
    /// </summary>
    private static SearchInputs NormalizeSearchInputs(
        string? q,
        string? type,
        string? status,
        int page,
        int pageSize,
        string? category)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 5, 50);

        var norm = (q ?? string.Empty).Trim();
        var hasQ = norm.Length > 0;

        // category acts as a fallback when type is blank
        var effectiveType =
            string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(category)
                ? category
                : type;

        return new SearchInputs(
            Page: safePage,
            PageSize: safePageSize,
            NormalizedQuery: norm,
            HasQuery: hasQ,
            EffectiveType: effectiveType,
            Status: status);
    }

    /// <summary>
    /// Builds the base hardware and software projection used by search.
    /// Applies the archived flag and projects into <see cref="AssetRowDto"/>.
    /// </summary>
    private IQueryable<AssetRowDto> BuildBaseQuery(bool showArchived)
    {
        // Hardware query (archived handling)
        var hardwareQuery = _db.HardwareAssets.AsNoTracking();
        hardwareQuery = showArchived
            ? hardwareQuery.IgnoreQueryFilters()
            : hardwareQuery.Where(h => !h.IsArchived);

        // Software query (archived handling)
        var softwareQuery = _db.SoftwareAssets.AsNoTracking();
        softwareQuery = showArchived
            ? softwareQuery.IgnoreQueryFilters()
            : softwareQuery.Where(s => !s.IsArchived);

        // Hardware projection
        var hardwareQ =
            hardwareQuery.Select(h => new AssetRowDto
            {
                HardwareID = h.HardwareID,
                SoftwareID = null,
                AssetName = h.AssetName ?? "",
                Type = h.AssetType ?? "",
                Tag = h.AssetTag ?? "",
                IsArchived = h.IsArchived,
                LicenseTotalSeats = 0,
                LicenseSeatsUsed = 0,

                Status = h.IsArchived
                    ? "Archived"
                    : (
                        _db.Assignments
                        .Where(a => a.AssetKind == Models.AssetKind.Hardware
                                 && a.HardwareID == h.HardwareID
                                 && a.UnassignedAtUtc == null)
                        .Any()
                            ? "Assigned"
                            : (string.IsNullOrWhiteSpace(h.Status) ? "Available" : h.Status)
                    ),

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
            });

        // Software projection
        var softwareQ =
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
                    : (s.LicenseTotalSeats > 0 && s.LicenseSeatsUsed >= s.LicenseTotalSeats
                        ? "Seats Full"
                        : "Available"),

                LicenseTotalSeats = s.LicenseTotalSeats,
                LicenseSeatsUsed = s.LicenseSeatsUsed,

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
            });

        return hardwareQ.Concat(softwareQ);
    }

    /// <summary>
    /// Applies type and status filters to the base query.
    /// </summary>
    private static IQueryable<AssetRowDto> ApplyFacetFilters(
        IQueryable<AssetRowDto> query,
        string? type,
        string? status)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim().ToLower();
            query = query.Where(a => a.Type != null && a.Type.ToLower() == t);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLower();
            query = query.Where(a => a.Status != null && a.Status.ToLower() == s);
        }

        return query;
    }

    /// <summary>
    /// Applies text search filters (including plural/singular handling).
    /// </summary>
    private IQueryable<AssetRowDto> ApplyQueryFilters(
        IQueryable<AssetRowDto> baseQ,
        string normalizedQuery)
    {
        var terms = BuildQueryTerms(normalizedQuery);

        IQueryable<AssetRowDto>? aggregateQ = null;

        foreach (var term in terms)
        {
            var termQ = BuildQueryForSingleTerm(baseQ, term);
            aggregateQ = aggregateQ is null ? termQ : aggregateQ.Union(termQ);
        }

        return aggregateQ!;
    }

    /// <summary>
    /// Produces one or more search terms from the normalized query.
    /// Adds a simple singular variant when a plural ending in 's' is detected.
    /// </summary>
    private static IReadOnlyCollection<string> BuildQueryTerms(string norm)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            norm
        };

        // Simple plural -> singular (strip trailing 's' for words of length >= 4)
        if (norm.EndsWith("s", StringComparison.OrdinalIgnoreCase) && norm.Length >= 4)
        {
            var singular = norm[..^1].Trim();
            if (!string.IsNullOrWhiteSpace(singular))
                set.Add(singular);
        }

        return set;
    }

    /// <summary>
    /// Builds the LIKE predicates for a single term (exact, prefix, contains).
    /// </summary>
    private IQueryable<AssetRowDto> BuildQueryForSingleTerm(
        IQueryable<AssetRowDto> baseQ,
        string term)
    {
        var escaped = EscapeLike(term);
        var likeExact = escaped;
        var likePrefix = escaped + "%";
        var likeContains = "%" + escaped + "%";

        IQueryable<AssetRowDto> exactQ = baseQ.Where(a =>
            EF.Functions.Like(a.AssetName ?? "", likeExact) ||
            EF.Functions.Like(a.Tag ?? "", likeExact) ||
            EF.Functions.Like(a.Type ?? "", likeExact) ||
            EF.Functions.Like(a.Status ?? "", likeExact) ||
            EF.Functions.Like(a.AssignedEmployeeName ?? "", likeExact) ||
            EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likeExact));

        IQueryable<AssetRowDto> prefixQ = baseQ.Where(a =>
            EF.Functions.Like(a.AssetName ?? "", likePrefix) ||
            EF.Functions.Like(a.Tag ?? "", likePrefix) ||
            EF.Functions.Like(a.Type ?? "", likePrefix) ||
            EF.Functions.Like(a.Status ?? "", likePrefix) ||
            EF.Functions.Like(a.AssignedEmployeeName ?? "", likePrefix) ||
            EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likePrefix));

        IQueryable<AssetRowDto> containsQ = baseQ.Where(a =>
            EF.Functions.Like(a.AssetName ?? "", likeContains) ||
            EF.Functions.Like(a.Tag ?? "", likeContains) ||
            EF.Functions.Like(a.Type ?? "", likeContains) ||
            EF.Functions.Like(a.Status ?? "", likeContains) ||
            EF.Functions.Like(a.AssignedEmployeeName ?? "", likeContains) ||
            EF.Functions.Like(a.AssignedEmployeeNumber ?? "", likeContains));

        return exactQ.Union(prefixQ).Union(containsQ);
    }

    /// <summary>
    /// Applies a stable ordering to the query.
    /// </summary>
    private static IQueryable<AssetRowDto> ApplyOrdering(IQueryable<AssetRowDto> query)
        => query
            .OrderBy(a => a.AssetName)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Tag)
            .ThenBy(a => a.HardwareID)
            .ThenBy(a => a.SoftwareID);

    /// <summary>
    /// Builds the cache key prefix used by paging helpers.
    /// The key includes a version stamp, role scope, query text, facets, and
    /// the archived flag.
    /// </summary>
    private async Task<string> BuildCacheKeyBaseAsync(
        string scopeKey,
        string norm,
        string? type,
        string? status,
        bool showArchived)
    {
        var stamp = CacheStamp.AssetsVersion;
        var normLower = norm.ToLowerInvariant();
        var typeLower = type?.ToLowerInvariant() ?? "";
        var statusLower = status?.ToLowerInvariant() ?? "";
        var archivedFlag = showArchived ? "true" : "false";

        return await Task.FromResult(
            $"assets:search:v={stamp}:scope={scopeKey}:q={normLower}|type={typeLower}|status={statusLower}|archived={archivedFlag}");
    }

    /// <summary>
    /// Executes the paged query using either exact totals or look-ahead totals.
    /// </summary>
    private async Task<PagedResult<AssetRowDto>> PageWithTotalsAsync(
        IQueryable<AssetRowDto> query,
        string cacheKeyBase,
        int page,
        int pageSize,
        PagingTotals totalsMode,
        CancellationToken ct)
    {
        return totalsMode == PagingTotals.Exact
            ? await Paging.PageExactCachedAsync(_cache, cacheKeyBase, query, page, pageSize, ct)
            : await Paging.PageLookAheadCachedAsync(_cache, cacheKeyBase, query, page, pageSize, ct);
    }

    private async Task HydrateSeatAssignmentsAsync(
    IReadOnlyCollection<AssetRowDto> items,
    CancellationToken ct)
    {
        if (items == null || items.Count == 0)
            return;

        // All software IDs on this page
        var softwareIds = items
            .Where(i => i.SoftwareID.HasValue)
            .Select(i => i.SoftwareID!.Value)
            .Distinct()
            .ToList();

        if (softwareIds.Count == 0)
            return;

        var seatRows = await _db.Assignments
            .AsNoTracking()
            .Where(a =>
                a.AssetKind == AIMS.Models.AssetKind.Software &&
                a.SoftwareID != null &&
                softwareIds.Contains(a.SoftwareID.Value) &&
                a.UnassignedAtUtc == null)
            .Select(a => new
            {
                SoftwareID = a.SoftwareID!.Value,
                UserId = a.UserID ?? 0,
                DisplayName = a.User != null ? a.User.FullName : null,
                EmployeeNumber = a.User != null ? a.User.EmployeeNumber : null
            })
            .ToListAsync(ct);

        var bySoftware = seatRows
            .GroupBy(x => x.SoftwareID)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new SeatAssignmentChipDto
                {
                    UserId = x.UserId,
                    DisplayName = x.DisplayName,
                    EmployeeNumber = x.EmployeeNumber
                }).ToList());

        foreach (var row in items)
        {
            if (row.SoftwareID is int id &&
                bySoftware.TryGetValue(id, out var chips))
            {
                row.SeatAssignments = chips;
            }
        }
    }

    // ------------------------------------------------------------------------
    // LIKE escaping helper
    // ------------------------------------------------------------------------

    private static string EscapeLike(string input) =>
        input
            .Replace("\\", "\\\\")
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");

    // ------------------------------------------------------------------------
    // Role flags and helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Aggregates claim-based and database role information.
    /// </summary>
    private sealed class RoleFlags
    {
        public bool IsAdminOrHelpdeskClaim { get; }
        public bool IsSupervisorClaim { get; }
        public bool IsAdminDb { get; }
        public bool IsHelpdeskDb { get; }
        public bool IsSupervisorDb { get; }

        public RoleFlags(
            bool isAdminOrHelpdeskClaim,
            bool isSupervisorClaim,
            string? roleName)
        {
            IsAdminOrHelpdeskClaim = isAdminOrHelpdeskClaim;
            IsSupervisorClaim = isSupervisorClaim;

            IsAdminDb = string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase);
            IsHelpdeskDb = string.Equals(roleName, "IT Help Desk", StringComparison.OrdinalIgnoreCase);
            IsSupervisorDb = string.Equals(roleName, "Supervisor", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAdminOrHelpdesk =>
            IsAdminOrHelpdeskClaim || IsAdminDb || IsHelpdeskDb;

        public bool IsSupervisor =>
            IsSupervisorClaim || IsSupervisorDb;
    }

    /// <summary>
    /// Factory for <see cref="RoleFlags"/> that trims role names.
    /// </summary>
    private static RoleFlags BuildRoleFlags(
        bool isAdminOrHelpdeskClaim,
        bool isSupervisorClaim,
        string? roleName)
        => new RoleFlags(isAdminOrHelpdeskClaim, isSupervisorClaim, roleName?.Trim());

    // =====================================================================
    //  ROLE / SCOPE HELPERS
    // =====================================================================

    /// <summary>
    /// Aggregated scope information used by role-based filters and cache keys.
    /// </summary>
    private sealed record RoleScope(
        bool HasUser,
        int? UserId,
        bool IsAdminOrHelpdesk,
        bool IsSupervisor,
        IReadOnlyCollection<int>? SupervisorScopeUserIds
    );

    /// <summary>
    /// Current claims principal on the HTTP context, when present.
    /// </summary>
    private ClaimsPrincipal? CurrentPrincipal
        => _http.HttpContext?.User;

    /// <summary>
    /// Flag for admin/help desk based purely on claims.
    /// </summary>
    private bool IsAdminOrHelpdeskClaim =>
        CurrentPrincipal is { } p && p.IsAdminOrHelpdesk();

    /// <summary>
    /// Flag for supervisor based purely on claims.
    /// </summary>
    private bool IsSupervisorClaim =>
        CurrentPrincipal is { } p && p.IsSupervisor();

    /// <summary>
    /// Computes effective role information and supervisor scope identifiers.
    /// Relies on both claims and user records.
    /// </summary>
    private async Task<RoleScope> BuildRoleScopeAsync(CancellationToken ct)
    {
        var principal = CurrentPrincipal;

        // No principal at all -> anonymous; no access.
        if (principal is null)
        {
            return new RoleScope(
                HasUser: false,
                UserId: null,
                IsAdminOrHelpdesk: false,
                IsSupervisor: false,
                SupervisorScopeUserIds: null);
        }

        // 1) Claims-based flags
        var isAdminOrHelpdeskClaim = IsAdminOrHelpdeskClaim;
        var isSupervisorClaim = IsSupervisorClaim;

        // 2) Resolve DB user + role (source of truth for hierarchy)
        var (user, roleName) = await ResolveCurrentUserAsync(ct);

        // If no DB user is resolved, claim-based flags are still honored.
        if (user is null)
        {
            return new RoleScope(
                HasUser: false,
                UserId: null,
                IsAdminOrHelpdesk: isAdminOrHelpdeskClaim,
                IsSupervisor: isSupervisorClaim,
                SupervisorScopeUserIds: null);
        }

        var isAdminDb =
            string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase);

        var isHelpdeskDb =
            string.Equals(roleName, "IT Help Desk", StringComparison.OrdinalIgnoreCase);

        var isSupervisorDb =
            string.Equals(roleName, "Supervisor", StringComparison.OrdinalIgnoreCase);

        var isAdminOrHelpdesk =
            isAdminOrHelpdeskClaim || isAdminDb || isHelpdeskDb;

        var isSupervisor =
            isSupervisorClaim || isSupervisorDb;

        IReadOnlyCollection<int>? supervisorScopeIds = null;

        if (isSupervisor)
        {
            supervisorScopeIds = await SupervisorScopeHelper
                .GetSupervisorScopeUserIdsAsync(_db, user.UserID, _cache, ct);
        }

        return new RoleScope(
            HasUser: true,
            UserId: user.UserID,
            IsAdminOrHelpdesk: isAdminOrHelpdesk,
            IsSupervisor: isSupervisor,
            SupervisorScopeUserIds: supervisorScopeIds);
    }

    // =====================================================================
    //  SCOPE BY ROLE  (used by SearchAsync)
    // =====================================================================

    /// <summary>
    /// Applies role-based scoping to the query.
    /// Admin/help desk see all assets.
    /// Supervisors see assets assigned to themselves and direct reports.
    /// All other cases return an empty set.
    /// </summary>
    private async Task<IQueryable<AssetRowDto>> ScopeByRoleAsync(
        IQueryable<AssetRowDto> query,
        CancellationToken ct)
    {
        var scope = await BuildRoleScopeAsync(ct);

        // 1) Admin / Help Desk -> full set (no scoping)
        if (scope.IsAdminOrHelpdesk)
            return query;

        // 2) No user (anonymous or unresolved) -> empty
        if (!scope.HasUser)
            return query.Where(_ => false);

        // 3) Supervisor -> scope to self + direct reports
        if (scope.IsSupervisor)
        {
            var ids = scope.SupervisorScopeUserIds!; // never null in practice

            return query.Where(a =>
                a.AssignedUserId.HasValue &&
                ids.Contains(a.AssignedUserId.Value));
        }

        // 4) Everyone else -> no asset search results
        return query.Where(_ => false);
    }

    // =====================================================================
    //  CACHE KEY SCOPE (used by SearchAsync for paging cache)
    // =====================================================================

    /// <summary>
    /// Produces a short string that represents the security scope for paging cache keys.
    /// </summary>
    private async Task<string> GetScopeCacheKeyAsync(CancellationToken ct)
    {
        var scope = await BuildRoleScopeAsync(ct);

        // Admin / Help Desk -> shared global bucket
        if (scope.IsAdminOrHelpdesk)
            return "admin";

        // Anonymous / unresolved user -> anon bucket
        if (!scope.HasUser || scope.UserId is null)
            return "anon";

        // Supervisor -> deterministic but scoped cache key
        if (scope.IsSupervisor)
        {
            var ids = scope.SupervisorScopeUserIds!; // non-null for supervisors
            var count = ids.Count;
            var min = ids.DefaultIfEmpty(0).Min();
            var max = ids.DefaultIfEmpty(0).Max();
            return $"sup:{scope.UserId}:{count}:{min}-{max}";
        }

        // Normal user -> user-specific bucket
        return $"user:{scope.UserId}";
    }

    // =====================================================================
    //  CURRENT USER RESOLUTION
    // =====================================================================

    /// <summary>
    /// Resolves the current user and role name based on claims.
    /// Uses AAD object identifier first, then email and employee number.
    /// </summary>
    public async Task<(AIMS.Models.User? user, string? roleName)> ResolveCurrentUserAsync(
        CancellationToken ct)
    {
        var http = _http.HttpContext;
        if (http is null)
            return (null, null);

        var principal = http.User;

        // 1) AAD object identifier (primary path)
        var oid =
            principal.FindFirst("oid")?.Value ??
            principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")
                     ?.Value;

        if (!string.IsNullOrWhiteSpace(oid))
        {
            var userFromOid = await ResolveUserFromObjectIdAsync(oid, ct);
            if (userFromOid is not null)
            {
                var roleNameFromOid = await _db.Roles.AsNoTracking()
                    .Where(r => r.RoleID == userFromOid.RoleID)
                    .Select(r => r.RoleName)
                    .FirstOrDefaultAsync(ct);

                return (userFromOid, roleNameFromOid);
            }
        }

        // 2) Email and employee number fallback
        var email =
            principal.FindFirst("preferred_username")?.Value ??
            principal.FindFirstValue(ClaimTypes.Email) ??
            principal.Identity?.Name;

        var employeeNumber =
            principal.FindFirst("employee_number")?.Value ??
            principal.FindFirst("employeeNumber")?.Value;

        var userFromEmailOrEmp =
            await ResolveUserFromEmailOrEmployeeAsync(email, employeeNumber, ct);

        if (userFromEmailOrEmp is null)
            return (null, null);

        var dbRoleName = await _db.Roles.AsNoTracking()
            .Where(r => r.RoleID == userFromEmailOrEmp.RoleID)
            .Select(r => r.RoleName)
            .FirstOrDefaultAsync(ct);

        return (userFromEmailOrEmp, dbRoleName);
    }

    /// <summary>
    /// Resolves a user from a Graph object identifier.
    /// </summary>
    private Task<AIMS.Models.User?> ResolveUserFromObjectIdAsync(
        string objectId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return Task.FromResult<AIMS.Models.User?>(null);

        return _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.GraphObjectID == objectId, ct);
    }

    /// <summary>
    /// Resolves a user from email and employee number.
    /// Resolution order:
    ///  1) Email match (when present)
    ///  2) Employee number match (when email cannot be used)
    /// </summary>
    private async Task<User?> ResolveUserFromEmailOrEmployeeAsync(
        string? email,
        string? employeeNumber,
        CancellationToken ct)
    {
        // 1) Try email first (when present)
        if (!string.IsNullOrWhiteSpace(email))
        {
            var byEmail = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email, ct);

            if (byEmail is not null)
                return byEmail;
        }

        // 2) Employee number as a secondary identifier
        if (!string.IsNullOrWhiteSpace(employeeNumber))
        {
            var byEmp = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeNumber == employeeNumber, ct);

            return byEmp;
        }

        // 3) No usable identifier
        return null;
    }
}
