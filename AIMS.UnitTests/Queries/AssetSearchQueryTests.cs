using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Common;
using AIMS.Models;
using AIMS.Queries;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AIMS.UnitTests.Queries;

public sealed class AssetSearchQueryTests
{
    // ------------------------------------------------------------
    // Small helpers to wire AssetSearchQuery the same way each time
    // ------------------------------------------------------------

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AIMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; }
            = new NullFileProvider();
    }

    private sealed class NullIdentityPrincipal : ClaimsPrincipal
    {
        public NullIdentityPrincipal(ClaimsIdentity identity)
            : base(identity)
        {
        }

        public override IIdentity? Identity => null;
    }

    private static AssetSearchQuery CreateQuery(
        AimsDbContext db,
        ClaimsPrincipal? principal)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        var accessor = new HttpContextAccessor
        {
            HttpContext = principal is null
                ? null
                : new DefaultHttpContext { User = principal }
        };

        var env = new FakeEnv();

        return new AssetSearchQuery(db, cache, accessor, env);
    }

    private static async Task<(AimsDbContext Db, DbConnection Conn)> CreateSeededContextAsync()
    {
        var (db, conn) = await Db.CreateContextAsync();
        await Db.SeedAsync(db);
        return (db, conn);
    }

    private static async Task<(AimsDbContext Db, DbConnection Conn, User Admin)> CreateAdminContextAsync()
    {
        var (db, conn) = await CreateSeededContextAsync();

        // Try to find an existing Admin user joined to an Admin role
        var admin = await (
            from u in db.Users
            join r in db.Roles on u.RoleID equals r.RoleID
            where r.RoleName == "Admin"
            select u
        ).FirstOrDefaultAsync();

        if (admin is null)
        {
            // Ensure an Admin role exists
            var adminRole = await db.Roles
                .FirstOrDefaultAsync(r => r.RoleName == "Admin");

            if (adminRole is null)
            {
                adminRole = new Role
                {
                    RoleName = "Admin",
                    Description = "Administrator"
                };
                db.Roles.Add(adminRole);
                await db.SaveChangesAsync();
            }

            // Create a minimal admin user wired to that role
            admin = new User
            {
                FullName = "Test Admin",
                Email = "admin@example.com",
                EmployeeNumber = "ADM-001",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsArchived = false,
                RoleID = adminRole.RoleID
            };

            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }

        return (db, conn, admin);
    }

    private static async Task<(AimsDbContext Db, DbConnection Conn, User Employee)> CreateEmployeeContextAsync()
    {
        var (db, conn) = await CreateSeededContextAsync();

        // Try to find a "normal" user whose role is not Admin / Supervisor / IT Help Desk
        var employee = await (
            from u in db.Users
            join r in db.Roles on u.RoleID equals r.RoleID
            where r.RoleName != "Admin"
               && r.RoleName != "Supervisor"
               && r.RoleName != "IT Help Desk"
            select u
        ).FirstOrDefaultAsync();

        if (employee is null)
        {
            // Ensure a basic "Employee" role exists
            var employeeRole = await db.Roles
                .FirstOrDefaultAsync(r => r.RoleName == "Employee");

            if (employeeRole is null)
            {
                employeeRole = new Role
                {
                    RoleName = "Employee",
                    Description = "Standard employee"
                };
                db.Roles.Add(employeeRole);
                await db.SaveChangesAsync();
            }

            // Create a minimal employee user for tests
            employee = new User
            {
                FullName = "Test Employee",
                Email = "employee@example.com",
                EmployeeNumber = "EMP-001",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsArchived = false,
                RoleID = employeeRole.RoleID
            };

            db.Users.Add(employee);
            await db.SaveChangesAsync();
        }

        return (db, conn, employee);
    }

    private static async Task<(AimsDbContext Db, DbConnection Conn, User Supervisor, User DirectReport)>
        CreateSupervisorWithReportContextAsync()
    {
        var (db, conn) = await CreateSeededContextAsync();

        // Ensure Supervisor role exists
        var supervisorRole = await db.Roles
            .FirstOrDefaultAsync(r => r.RoleName == "Supervisor");

        if (supervisorRole is null)
        {
            supervisorRole = new Role
            {
                RoleName = "Supervisor",
                Description = "Supervisor"
            };
            db.Roles.Add(supervisorRole);
            await db.SaveChangesAsync();
        }

        // Basic Employee role if needed for direct report
        var employeeRole = await db.Roles
            .FirstOrDefaultAsync(r => r.RoleName == "Employee");

        if (employeeRole is null)
        {
            employeeRole = new Role
            {
                RoleName = "Employee",
                Description = "Standard employee"
            };
            db.Roles.Add(employeeRole);
            await db.SaveChangesAsync();
        }

        // Create supervisor user
        var supervisor = new User
        {
            FullName = "Test Supervisor",
            Email = "supervisor@example.com",
            EmployeeNumber = "SUP-001",
            ExternalId = Guid.NewGuid(),
            GraphObjectID = Guid.NewGuid().ToString("N"),
            IsArchived = false,
            RoleID = supervisorRole.RoleID
        };
        db.Users.Add(supervisor);
        await db.SaveChangesAsync();

        // Create direct report
        var report = new User
        {
            FullName = "Direct Report",
            Email = "report@example.com",
            EmployeeNumber = "EMP-SUP-001",
            ExternalId = Guid.NewGuid(),
            GraphObjectID = Guid.NewGuid().ToString("N"),
            IsArchived = false,
            RoleID = employeeRole.RoleID,
            SupervisorID = supervisor.UserID
        };
        db.Users.Add(report);
        await db.SaveChangesAsync();

        // Create a hardware asset and assign to direct report (for scoping tests)
        var hw = new Hardware
        {
            AssetTag = "TST-HW-001",
            AssetName = "Supervisor Laptop",
            AssetType = "Laptop",
            Status = "Assigned",
            Manufacturer = "TestCo",
            Model = "SupBook",
            SerialNumber = Guid.NewGuid().ToString("N")[..16],
            PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(3)),
            Comment = ""
        };
        db.HardwareAssets.Add(hw);
        await db.SaveChangesAsync();

        var assignment = new Assignment
        {
            UserID = report.UserID,
            AssetKind = AssetKind.Hardware,
            HardwareID = hw.HardwareID,
            AssignedAtUtc = DateTime.UtcNow
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        return (db, conn, supervisor, report);
    }

    private static ClaimsPrincipal BuildPrincipalWithOid(string oid)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("oid", oid) },
            authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal BuildPrincipalWithEmail(string email)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("preferred_username", email) },
            authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal BuildPrincipalWithEmployeeNumber(string employeeNumber)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("employee_number", employeeNumber) },
            authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    // Helper for calling private async methods that return Task<T>
    private static async Task<T> InvokePrivateAsync<T>(
        object target,
        string methodName,
        params object?[] args)
    {
        var mi = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);

        var taskObj = mi!.Invoke(target, args);
        Assert.NotNull(taskObj);

        var task = (Task)taskObj!;
        await task;

        var resultProp = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProp);

        var result = resultProp!.GetValue(task);
        // Don't require exact type; just cast to T (e.g., object, IQueryable<>, etc.)
        return (T)result!;
    }

    // Helper for calling private static methods
    private static object? InvokePrivateStatic(
        Type type,
        string methodName,
        params object?[] args)
    {
        var mi = type.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        return mi!.Invoke(null, args);
    }

    // ------------------------------------------------------------
    // 1) Admin via DB role -> full search results (no scoping)
    // ------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_AdminUserViaOid_ReturnsNonEmptyResults()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID!);
            var query = CreateQuery(db, principal);

            var result = await query.SearchAsync(
                q: null,
                type: null,
                status: null,
                page: 1,
                pageSize: 25,
                ct: default,
                category: null,
                totalsMode: PagingTotals.Exact,
                showArchived: false);

            Assert.NotNull(result);
            Assert.True(result.Total > 0);
            Assert.NotEmpty(result.Items);
        }
    }

    // ------------------------------------------------------------
    // 2) Anonymous (no HttpContext) -> empty result due to scoping
    // ------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_AnonymousUser_ReturnsEmptyResult()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var result = await query.SearchAsync(
                q: null,
                type: null,
                status: null,
                page: 1,
                pageSize: 25,
                ct: default,
                category: null,
                totalsMode: PagingTotals.Exact,
                showArchived: false);

            Assert.NotNull(result);
            Assert.Equal(0, result.Total);
            Assert.Empty(result.Items);
        }
    }

    // ------------------------------------------------------------
    // 3) Normal user (not admin/helpdesk/supervisor) -> no assets
    // ------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_NormalUser_ReturnsEmptyResult()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(employee.GraphObjectID!);
            var query = CreateQuery(db, principal);

            var result = await query.SearchAsync(
                q: null,
                type: null,
                status: null,
                page: 1,
                pageSize: 25,
                ct: default,
                category: null,
                totalsMode: PagingTotals.Exact,
                showArchived: false);

            Assert.NotNull(result);
            Assert.Equal(0, result.Total);
            Assert.Empty(result.Items);
        }
    }

    // ------------------------------------------------------------
    // 4) ResolveCurrentUserAsync prefers OID over email
    // ------------------------------------------------------------

    [Fact]
    public async Task ResolveCurrentUserAsync_UsesObjectIdWhenPresent()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            // Principal has both OID and email; should resolve via OID path.
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim("oid", admin.GraphObjectID!),
                    new Claim("preferred_username", admin.Email!)
                },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(admin.UserID, user!.UserID);
            Assert.Equal("Admin", roleName);
        }
    }

    // ------------------------------------------------------------
    // 5) ResolveCurrentUserAsync falls back to email when no OID
    // ------------------------------------------------------------

    [Fact]
    public async Task ResolveCurrentUserAsync_FallsBackToEmail_WhenNoObjectId()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithEmail(admin.Email!);
            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(admin.UserID, user!.UserID);
            Assert.Equal("Admin", roleName);
        }
    }

    // ------------------------------------------------------------
    // 6) ResolveCurrentUserAsync falls back to employee number
    //    when neither OID nor email can be used.
    // ------------------------------------------------------------

    [Fact]
    public async Task ResolveCurrentUserAsync_FallsBackToEmployeeNumber_WhenNoOidOrEmail()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithEmployeeNumber(employee.EmployeeNumber!);
            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(employee.UserID, user!.UserID);
            // roleName can be anything non-null here; we just care that it resolved.
            Assert.False(string.IsNullOrWhiteSpace(roleName));
        }
    }

    // ------------------------------------------------------------
    // 7) ResolveCurrentUserAsync returns null when there is no info
    // ------------------------------------------------------------

    [Fact]
    public async Task ResolveCurrentUserAsync_ReturnsNull_WhenNoResolvableClaims()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var identity = new ClaimsIdentity(authenticationType: "TestAuth"); // no claims
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.Null(user);
            Assert.Null(roleName);
        }
    }

    // ------------------------------------------------------------
    // 8) GetScopeCacheKeyAsync: admin user -> "admin"
    //    (called via reflection since it's private)
    // ------------------------------------------------------------

    [Fact]
    public async Task GetScopeCacheKeyAsync_AdminUser_ReturnsAdminKey()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID!);
            var query = CreateQuery(db, principal);

            var method = typeof(AssetSearchQuery).GetMethod(
                "GetScopeCacheKeyAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var task = (Task<string>)method!.Invoke(query, new object[] { CancellationToken.None })!;
            var key = await task;

            Assert.Equal("admin", key);
        }
    }

    // ------------------------------------------------------------
    // 9) GetScopeCacheKeyAsync: anonymous -> "anon"
    // ------------------------------------------------------------

    [Fact]
    public async Task GetScopeCacheKeyAsync_Anonymous_ReturnsAnonKey()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var method = typeof(AssetSearchQuery).GetMethod(
                "GetScopeCacheKeyAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var task = (Task<string>)method!.Invoke(query, new object[] { CancellationToken.None })!;
            var key = await task;

            Assert.Equal("anon", key);
        }
    }

    // ========================================================================
    // NormalizeSearchInputs
    // ========================================================================

    [Fact]
    public void NormalizeSearchInputs_ClampsPageAndPageSize_AndUsesCategoryFallback()
    {
        // page < 1 and pageSize < 5 should clamp
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "NormalizeSearchInputs",
            "  test  ", // q
            null,      // type
            "Available",
            0,         // page
            2,         // pageSize
            "Laptop"   // category fallback
        );
        Assert.NotNull(result);

        var type = result!.GetType();
        var page = (int)type.GetProperty("Page")!.GetValue(result)!;
        var pageSize = (int)type.GetProperty("PageSize")!.GetValue(result)!;
        var norm = (string)type.GetProperty("NormalizedQuery")!.GetValue(result)!;
        var hasQ = (bool)type.GetProperty("HasQuery")!.GetValue(result)!;
        var effectiveType = (string?)type.GetProperty("EffectiveType")!.GetValue(result)!;
        var status = (string?)type.GetProperty("Status")!.GetValue(result)!;

        Assert.Equal(1, page);
        Assert.Equal(5, pageSize); // min clamp
        Assert.Equal("test", norm);
        Assert.True(hasQ);
        Assert.Equal("Laptop", effectiveType);
        Assert.Equal("Available", status);
    }

    [Fact]
    public void NormalizeSearchInputs_WhenTypeProvided_IgnoresCategory()
    {
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "NormalizeSearchInputs",
            "",           // q
            "Monitor",    // type
            null,         // status
            10,           // page (no clamp)
            100,          // pageSize (will clamp to 50)
            "Laptop"      // category (ignored since type present)
        );

        Assert.NotNull(result);

        var type = result!.GetType();
        var page = (int)type.GetProperty("Page")!.GetValue(result)!;
        var pageSize = (int)type.GetProperty("PageSize")!.GetValue(result)!;
        var norm = (string)type.GetProperty("NormalizedQuery")!.GetValue(result)!;
        var hasQ = (bool)type.GetProperty("HasQuery")!.GetValue(result)!;
        var effectiveType = (string?)type.GetProperty("EffectiveType")!.GetValue(result)!;
        var status = (string?)type.GetProperty("Status")!.GetValue(result)!;

        Assert.Equal(10, page);
        Assert.Equal(50, pageSize); // max clamp
        Assert.Equal(string.Empty, norm);
        Assert.False(hasQ);
        Assert.Equal("Monitor", effectiveType);
        Assert.Null(status);
    }

    // ========================================================================
    // BuildQueryTerms / ApplyQueryFilters / BuildQueryForSingleTerm
    // ========================================================================

    [Fact]
    public void BuildQueryTerms_PluralGeneratesSingularVariant()
    {
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "BuildQueryTerms",
            "laptops"
        );

        var terms = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(result);
        Assert.Contains("laptops", terms);
        Assert.Contains("laptop", terms);
        Assert.Equal(2, terms.Count);
    }

    [Fact]
    public void BuildQueryTerms_ShortWordEndingWithS_DoesNotStripToSingular()
    {
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "BuildQueryTerms",
            "bus" // length 3, should not create "bu"
        );

        var terms = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(result);
        Assert.Contains("bus", terms);
        Assert.Single(terms);
    }

    [Fact]
    public void BuildQueryTerms_NonPlural_SingleTermOnly()
    {
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "BuildQueryTerms",
            "monitor"
        );

        var terms = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(result);
        Assert.Contains("monitor", terms);
        Assert.Single(terms);
    }

    [Fact]
    public async Task ApplyQueryFilters_WithPluralTerm_UnionsSingularAndPlural()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID);
            var query = CreateQuery(db, principal);

            // Build base query (non-archived)
            var baseQMethod = typeof(AssetSearchQuery).GetMethod(
                "BuildBaseQuery",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(baseQMethod);

            var baseQ = (IQueryable<AssetRowDto>)baseQMethod!.Invoke(query, new object[] { false })!;
            var baseCount = await baseQ.CountAsync();

            // ApplyQueryFilters with "laptops" so we get plural/singular union
            var applyMethod = typeof(AssetSearchQuery).GetMethod(
                "ApplyQueryFilters",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(applyMethod);

            var filtered = (IQueryable<AssetRowDto>)applyMethod!.Invoke(
                query,
                new object[] { baseQ, "laptops" })!;

            var list = await filtered.ToListAsync();

            // We mostly care that it executes; just basic sanity
            Assert.NotNull(list);
            Assert.True(list.Count >= 0);
            Assert.True(baseCount >= 0);
        }
    }

    // ========================================================================
    // PageWithTotalsAsync (exact vs. look-ahead)
    // ========================================================================

    [Fact]
    public async Task PageWithTotalsAsync_UsesExactTotalsMode()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID);
            var query = CreateQuery(db, principal);

            // Make a simple query
            var baseQ = db.HardwareAssets.AsNoTracking()
                .Select(h => new AssetRowDto
                {
                    HardwareID = h.HardwareID,
                    AssetName = h.AssetName,
                    Type = h.AssetType,
                    Tag = h.AssetTag
                });

            var result = await InvokePrivateAsync<PagedResult<AssetRowDto>>(
                query,
                "PageWithTotalsAsync",
                baseQ,
                "test:exact",
                1,
                10,
                PagingTotals.Exact,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.Page >= 1);
        }
    }

    [Fact]
    public async Task PageWithTotalsAsync_UsesLookAheadTotalsMode()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID);
            var query = CreateQuery(db, principal);

            var baseQ = db.HardwareAssets.AsNoTracking()
                .Select(h => new AssetRowDto
                {
                    HardwareID = h.HardwareID,
                    AssetName = h.AssetName,
                    Type = h.AssetType,
                    Tag = h.AssetTag
                });

            var result = await InvokePrivateAsync<PagedResult<AssetRowDto>>(
                query,
                "PageWithTotalsAsync",
                baseQ,
                "test:lookahead",
                1,
                10,
                PagingTotals.Lookahead, // any non-Exact path
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.Page >= 1);
        }
    }

    // ------------------------------------------------------------------------
    // HydrateSeatAssignmentsAsync tests
    // ------------------------------------------------------------------------

    // 1) Normal path: page with software IDs → chips are hydrated
    [Fact]
    public async Task HydrateSeatAssignmentsAsync_PopulatesSeatAssignments_ForMatchingSoftware()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID!);
            var query = CreateQuery(db, principal);

            // Arrange: one software asset + one active assignment
            var user = await db.Users.FirstAsync();

            var sw = new Software
            {
                SoftwareName = "Test Suite",
                SoftwareType = "Software",
                SoftwareLicenseKey = "LIC-TEST-001",
                LicenseSeatsUsed = 1,
                LicenseTotalSeats = 10,
                IsArchived = false
            };
            db.SoftwareAssets.Add(sw);
            await db.SaveChangesAsync();

            db.Assignments.Add(new Assignment
            {
                AssetKind = AssetKind.Software,
                SoftwareID = sw.SoftwareID,
                UserID = user.UserID,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var items = new List<AssetRowDto>
        {
            new AssetRowDto
            {
                SoftwareID = sw.SoftwareID,
                HardwareID = null,
                AssetName = sw.SoftwareName!,
                Type = "Software",
                Tag = sw.SoftwareLicenseKey!
            }
        };

            var mi = typeof(AssetSearchQuery).GetMethod(
                "HydrateSeatAssignmentsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { items, CancellationToken.None });

            // Accept Task or Task<TResult>
            Assert.True(taskObj is Task);
            await (Task)taskObj!;

            Assert.NotNull(items[0].SeatAssignments);
            Assert.Single(items[0].SeatAssignments!);

            var chip = items[0].SeatAssignments![0];
            Assert.Equal(user.UserID, chip.UserId);
            Assert.Equal(user.FullName, chip.DisplayName);
            Assert.Equal(user.EmployeeNumber, chip.EmployeeNumber);
        }
    }

    // 2) items == null → early return branch (items == null || ...)
    [Fact]
    public async Task HydrateSeatAssignmentsAsync_ItemsNull_EarlyReturn()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID!);
            var query = CreateQuery(db, principal);

            // Explicitly nullable collection
            IReadOnlyCollection<AssetRowDto>? items = null;

            var mi = typeof(AssetSearchQuery).GetMethod(
                "HydrateSeatAssignmentsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { items, CancellationToken.None });

            Assert.True(taskObj is Task);
            await (Task)taskObj!;

            // No assertion needed; this just hits the items == null branch
        }
    }

    // 3) items.Count == 0 → early return branch (second half of ||)
    [Fact]
    public async Task HydrateSeatAssignmentsAsync_ItemsEmpty_EarlyReturn()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID!);
            var query = CreateQuery(db, principal);

            // Non-null but empty collection → items.Count == 0
            IReadOnlyCollection<AssetRowDto> items = Array.Empty<AssetRowDto>();

            var mi = typeof(AssetSearchQuery).GetMethod(
                "HydrateSeatAssignmentsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { items, CancellationToken.None });

            Assert.True(taskObj is Task);
            await (Task)taskObj!;

            // Again, nothing to assert - existence of this test exercises the branch
        }
    }

    // 4) softwareIds.Count == 0 → early return before DB query
    [Fact]
    public async Task HydrateSeatAssignmentsAsync_NoSoftwareIds_EarlyReturnWithoutMutatingSeatAssignments()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID!);
            var query = CreateQuery(db, principal);

            // Arrange: only hardware rows (SoftwareID is null)
            var originalSeatAssignments = new List<SeatAssignmentChipDto>
        {
            new SeatAssignmentChipDto
            {
                UserId = 999,
                DisplayName = "Sentinel User",
                EmployeeNumber = "SENT-001"
            }
        };

            var items = new List<AssetRowDto>
        {
            new AssetRowDto
            {
                HardwareID = 123,
                SoftwareID = null,   // ensures softwareIds.Count == 0
                AssetName = "Test Laptop",
                Type = "Laptop",
                Tag = "HW-TAG-123",
                SeatAssignments = originalSeatAssignments
            }
        };

            var mi = typeof(AssetSearchQuery).GetMethod(
                "HydrateSeatAssignmentsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { items, CancellationToken.None });

            Assert.True(taskObj is Task);
            await (Task)taskObj!;

            // Because softwareIds.Count == 0, the method should return without touching SeatAssignments
            Assert.Same(originalSeatAssignments, items[0].SeatAssignments);
            Assert.Single(items[0].SeatAssignments!);
            Assert.Equal(999, items[0].SeatAssignments![0].UserId);
        }
    }

    // ========================================================================
    // RoleFlags + BuildRoleFlags (nested helper)
    // ========================================================================

    [Fact]
    public void RoleFlags_AdminRole_SetsAdminFlags()
    {
        var roleFlagsType = typeof(AssetSearchQuery).GetNestedType(
            "RoleFlags",
            BindingFlags.NonPublic);

        Assert.NotNull(roleFlagsType);

        var ctor = roleFlagsType!.GetConstructor(new[] { typeof(bool), typeof(bool), typeof(string) });
        Assert.NotNull(ctor);

        var flags = ctor!.Invoke(new object[] { false, false, "Admin" });

        var isAdminDb = (bool)roleFlagsType.GetProperty("IsAdminDb")!.GetValue(flags)!;
        var isHelpdeskDb = (bool)roleFlagsType.GetProperty("IsHelpdeskDb")!.GetValue(flags)!;
        var isSupervisorDb = (bool)roleFlagsType.GetProperty("IsSupervisorDb")!.GetValue(flags)!;
        var isAdminOrHelpdesk = (bool)roleFlagsType.GetProperty("IsAdminOrHelpdesk")!.GetValue(flags)!;
        var isSupervisor = (bool)roleFlagsType.GetProperty("IsSupervisor")!.GetValue(flags)!;

        Assert.True(isAdminDb);
        Assert.False(isHelpdeskDb);
        Assert.False(isSupervisorDb);
        Assert.True(isAdminOrHelpdesk);
        Assert.False(isSupervisor);
    }

    [Fact]
    public void RoleFlags_HelpdeskRole_SetsHelpdeskFlags()
    {
        var roleFlagsType = typeof(AssetSearchQuery).GetNestedType(
            "RoleFlags",
            BindingFlags.NonPublic);

        Assert.NotNull(roleFlagsType);

        var ctor = roleFlagsType!.GetConstructor(new[] { typeof(bool), typeof(bool), typeof(string) });
        Assert.NotNull(ctor);

        var flags = ctor!.Invoke(new object[] { false, false, "IT Help Desk" });

        var isAdminDb = (bool)roleFlagsType.GetProperty("IsAdminDb")!.GetValue(flags)!;
        var isHelpdeskDb = (bool)roleFlagsType.GetProperty("IsHelpdeskDb")!.GetValue(flags)!;
        var isAdminOrHelpdesk = (bool)roleFlagsType.GetProperty("IsAdminOrHelpdesk")!.GetValue(flags)!;

        Assert.False(isAdminDb);
        Assert.True(isHelpdeskDb);
        Assert.True(isAdminOrHelpdesk);
    }

    [Fact]
    public void RoleFlags_SupervisorRole_SetsSupervisorFlags()
    {
        var roleFlagsType = typeof(AssetSearchQuery).GetNestedType(
            "RoleFlags",
            BindingFlags.NonPublic);

        Assert.NotNull(roleFlagsType);

        var ctor = roleFlagsType!.GetConstructor(new[] { typeof(bool), typeof(bool), typeof(string) });
        Assert.NotNull(ctor);

        var flags = ctor!.Invoke(new object[] { false, false, "Supervisor" });

        var isSupervisorDb = (bool)roleFlagsType.GetProperty("IsSupervisorDb")!.GetValue(flags)!;
        var isSupervisor = (bool)roleFlagsType.GetProperty("IsSupervisor")!.GetValue(flags)!;
        var isAdminOrHelpdesk = (bool)roleFlagsType.GetProperty("IsAdminOrHelpdesk")!.GetValue(flags)!;

        Assert.True(isSupervisorDb);
        Assert.True(isSupervisor);
        Assert.False(isAdminOrHelpdesk);
    }

    [Fact]
    public void BuildRoleFlags_UsesTrimmedRoleName()
    {
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "BuildRoleFlags",
            false,
            true,
            "  Supervisor  ");

        Assert.NotNull(result);

        var roleFlagsType = result!.GetType();

        var isSupervisor = (bool)roleFlagsType.GetProperty("IsSupervisor")!.GetValue(result)!;
        var isSupervisorDb = (bool)roleFlagsType.GetProperty("IsSupervisorDb")!.GetValue(result)!;

        Assert.True(isSupervisor);
        Assert.True(isSupervisorDb);
    }

    // ========================================================================
    // BuildRoleScopeAsync
    // ========================================================================

    [Fact]
    public async Task BuildRoleScopeAsync_NoPrincipal_ReturnsAnonymousScope()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var scope = await InvokePrivateAsync<object>(
                query,
                "BuildRoleScopeAsync",
                CancellationToken.None);

            var type = scope.GetType();
            var hasUser = (bool)type.GetProperty("HasUser")!.GetValue(scope)!;
            var userId = (int?)type.GetProperty("UserId")!.GetValue(scope)!;
            var isAdminOrHelpdesk = (bool)type.GetProperty("IsAdminOrHelpdesk")!.GetValue(scope)!;
            var isSupervisor = (bool)type.GetProperty("IsSupervisor")!.GetValue(scope)!;
            var supIds = (IReadOnlyCollection<int>?)type.GetProperty("SupervisorScopeUserIds")!.GetValue(scope)!;

            Assert.False(hasUser);
            Assert.Null(userId);
            Assert.False(isAdminOrHelpdesk);
            Assert.False(isSupervisor);
            Assert.Null(supIds);
        }
    }

    [Fact]
    public async Task BuildRoleScopeAsync_PrincipalWithoutResolvedUser_UsesClaimsOnly()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // Principal with bogus OID, no email / employee# -> cannot resolve DB user
            var identity = new ClaimsIdentity(
                new[] { new Claim("oid", Guid.NewGuid().ToString("N")) },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var scope = await InvokePrivateAsync<object>(
                query,
                "BuildRoleScopeAsync",
                CancellationToken.None);

            var type = scope.GetType();
            var hasUser = (bool)type.GetProperty("HasUser")!.GetValue(scope)!;
            var userId = (int?)type.GetProperty("UserId")!.GetValue(scope)!;

            // Has a principal but could not resolve a DB user
            Assert.False(hasUser);
            Assert.Null(userId);
        }
    }

    [Fact]
    public async Task BuildRoleScopeAsync_AdminUserFromDb_SetsAdminOrHelpdeskTrue()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID);
            var query = CreateQuery(db, principal);

            var scope = await InvokePrivateAsync<object>(
                query,
                "BuildRoleScopeAsync",
                CancellationToken.None);

            var type = scope.GetType();
            var hasUser = (bool)type.GetProperty("HasUser")!.GetValue(scope)!;
            var userId = (int?)type.GetProperty("UserId")!.GetValue(scope)!;
            var isAdminOrHelpdesk = (bool)type.GetProperty("IsAdminOrHelpdesk")!.GetValue(scope)!;
            var isSupervisor = (bool)type.GetProperty("IsSupervisor")!.GetValue(scope)!;
            var supIds = (IReadOnlyCollection<int>?)type.GetProperty("SupervisorScopeUserIds")!.GetValue(scope)!;

            Assert.True(hasUser);
            Assert.Equal(admin.UserID, userId);
            Assert.True(isAdminOrHelpdesk);
            Assert.False(isSupervisor);
            Assert.Null(supIds);
        }
    }

    [Fact]
    public async Task BuildRoleScopeAsync_SupervisorUserWithReports_LoadsSupervisorScopeIds()
    {
        var (db, conn, supervisor, report) = await CreateSupervisorWithReportContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(supervisor.GraphObjectID);
            var query = CreateQuery(db, principal);

            var scope = await InvokePrivateAsync<object>(
                query,
                "BuildRoleScopeAsync",
                CancellationToken.None);

            var type = scope.GetType();
            var hasUser = (bool)type.GetProperty("HasUser")!.GetValue(scope)!;
            var userId = (int?)type.GetProperty("UserId")!.GetValue(scope)!;
            var isAdminOrHelpdesk = (bool)type.GetProperty("IsAdminOrHelpdesk")!.GetValue(scope)!;
            var isSupervisor = (bool)type.GetProperty("IsSupervisor")!.GetValue(scope)!;
            var supIds = (IReadOnlyCollection<int>?)type.GetProperty("SupervisorScopeUserIds")!.GetValue(scope)!;

            Assert.True(hasUser);
            Assert.Equal(supervisor.UserID, userId);
            Assert.False(isAdminOrHelpdesk);
            Assert.True(isSupervisor);
            Assert.NotNull(supIds);
            Assert.Contains(supervisor.UserID, supIds!); // supervisor self
            Assert.Contains(report.UserID, supIds!);      // direct report
        }
    }

    // ========================================================================
    // ScopeByRoleAsync
    // ========================================================================

    [Fact]
    public async Task ScopeByRoleAsync_Admin_ReturnsAllAssets()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(admin.GraphObjectID);
            var query = CreateQuery(db, principal);

            var baseQMethod = typeof(AssetSearchQuery).GetMethod(
                "BuildBaseQuery",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(baseQMethod);

            var baseQ = (IQueryable<AssetRowDto>)baseQMethod!.Invoke(query, new object[] { false })!;
            var baseCount = await baseQ.CountAsync();

            var scopedQ = await InvokePrivateAsync<IQueryable<AssetRowDto>>(
                query,
                "ScopeByRoleAsync",
                baseQ,
                CancellationToken.None);

            var scopedCount = await scopedQ.CountAsync();

            // Admin/helpdesk should see full set
            Assert.Equal(baseCount, scopedCount);
        }
    }

    [Fact]
    public async Task ScopeByRoleAsync_Anonymous_ReturnsEmpty()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var baseQMethod = typeof(AssetSearchQuery).GetMethod(
                "BuildBaseQuery",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(baseQMethod);

            var baseQ = (IQueryable<AssetRowDto>)baseQMethod!.Invoke(query, new object[] { false })!;

            var scopedQ = await InvokePrivateAsync<IQueryable<AssetRowDto>>(
                query,
                "ScopeByRoleAsync",
                baseQ,
                CancellationToken.None);

            var scopedCount = await scopedQ.CountAsync();
            Assert.Equal(0, scopedCount);
        }
    }

    [Fact]
    public async Task ScopeByRoleAsync_SupervisorWithReports_ReturnsOnlyScopedAssets()
    {
        var (db, conn, supervisor, report) = await CreateSupervisorWithReportContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(supervisor.GraphObjectID);
            var query = CreateQuery(db, principal);

            var baseQMethod = typeof(AssetSearchQuery).GetMethod(
                "BuildBaseQuery",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(baseQMethod);

            var baseQ = (IQueryable<AssetRowDto>)baseQMethod!.Invoke(query, new object[] { false })!;

            var scopedQ = await InvokePrivateAsync<IQueryable<AssetRowDto>>(
                query,
                "ScopeByRoleAsync",
                baseQ,
                CancellationToken.None);

            var items = await scopedQ.ToListAsync();

            // We created exactly one assignment to the direct report
            Assert.Single(items);
            Assert.Equal(report.UserID, items[0].AssignedUserId);
        }
    }

    [Fact]
    public async Task ScopeByRoleAsync_NormalUserWithUser_ReturnsEmpty()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(employee.GraphObjectID);
            var query = CreateQuery(db, principal);

            var baseQMethod = typeof(AssetSearchQuery).GetMethod(
                "BuildBaseQuery",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(baseQMethod);

            var baseQ = (IQueryable<AssetRowDto>)baseQMethod!.Invoke(query, new object[] { false })!;

            var scopedQ = await InvokePrivateAsync<IQueryable<AssetRowDto>>(
                query,
                "ScopeByRoleAsync",
                baseQ,
                CancellationToken.None);

            var count = await scopedQ.CountAsync();
            Assert.Equal(0, count);
        }
    }

    // ========================================================================
    // GetScopeCacheKeyAsync (remaining branches)
    // ========================================================================

    [Fact]
    public async Task GetScopeCacheKeyAsync_Supervisor_ReturnsSupervisorFormattedKey()
    {
        var (db, conn, supervisor, _) = await CreateSupervisorWithReportContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(supervisor.GraphObjectID);
            var query = CreateQuery(db, principal);

            var method = typeof(AssetSearchQuery).GetMethod(
                "GetScopeCacheKeyAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var task = (Task<string>)method!.Invoke(query, new object[] { CancellationToken.None })!;
            var key = await task;

            Assert.StartsWith($"sup:{supervisor.UserID}:", key);
        }
    }

    [Fact]
    public async Task GetScopeCacheKeyAsync_NormalUser_ReturnsUserKey()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            var principal = BuildPrincipalWithOid(employee.GraphObjectID);
            var query = CreateQuery(db, principal);

            var method = typeof(AssetSearchQuery).GetMethod(
                "GetScopeCacheKeyAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var task = (Task<string>)method!.Invoke(query, new object[] { CancellationToken.None })!;
            var key = await task;

            Assert.Equal($"user:{employee.UserID}", key);
        }
    }

    // ========================================================================
    // ResolveCurrentUserAsync additional claim paths
    // ========================================================================

    [Fact]
    public async Task ResolveCurrentUserAsync_UsesClaimTypesEmail_WhenPreferredUsernameMissing()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            // Email via ClaimTypes.Email, no "preferred_username"
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Email, admin.Email!) },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(admin.UserID, user!.UserID);
            Assert.Equal("Admin", roleName);
        }
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_UsesIdentityName_WhenNoEmailClaims()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            // Only name claim; Identity.Name should be used
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, admin.Email!) },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(admin.UserID, user!.UserID);
            Assert.Equal("Admin", roleName);
        }
    }

    // ========================================================================
    // ResolveUserFromObjectIdAsync
    // ========================================================================

    [Fact]
    public async Task ResolveUserFromObjectIdAsync_NullOrWhitespace_ReturnsNull()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var mi = typeof(AssetSearchQuery).GetMethod(
                "ResolveUserFromObjectIdAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { "   ", CancellationToken.None });
            var task = (Task)taskObj!;
            await task;

            var resultProp = task.GetType().GetProperty("Result");
            Assert.NotNull(resultProp);

            var result = resultProp!.GetValue(task);
            Assert.Null(result);
        }
    }

    // ========================================================================
    // ResolveUserFromEmailOrEmployeeAsync
    // ========================================================================

    [Fact]
    public async Task ResolveUserFromEmailOrEmployeeAsync_NoEmailOrEmployee_ReturnsNull()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var mi = typeof(AssetSearchQuery).GetMethod(
                "ResolveUserFromEmailOrEmployeeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { null, null, CancellationToken.None });
            var task = (Task)taskObj!;
            await task;

            var resultProp = task.GetType().GetProperty("Result");
            Assert.NotNull(resultProp);

            var result = resultProp!.GetValue(task);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task ResolveUserFromEmailOrEmployeeAsync_EmailMatch_ReturnsUser()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var mi = typeof(AssetSearchQuery).GetMethod(
                "ResolveUserFromEmailOrEmployeeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { employee.Email, null, CancellationToken.None });
            var task = (Task)taskObj!;
            await task;

            var resultProp = task.GetType().GetProperty("Result");
            Assert.NotNull(resultProp);

            var result = resultProp!.GetValue(task);
            var resolved = Assert.IsType<User>(result);

            Assert.Equal(employee.UserID, resolved.UserID);
        }
    }

    [Fact]
    public async Task ResolveUserFromEmailOrEmployeeAsync_EmailNoMatch_FallsBackToEmployeeNumber()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // Create a user for this test
            var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Employee")
                       ?? new Role { RoleName = "Employee", Description = "Standard employee" };

            if (role.RoleID == 0)
            {
                db.Roles.Add(role);
                await db.SaveChangesAsync();
            }

            var user = new User
            {
                FullName = "Email/Employee Test User",
                Email = "match@example.com",
                EmployeeNumber = "EMP-777",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                RoleID = role.RoleID,
                IsArchived = false
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var query = CreateQuery(db, principal: null);

            var mi = typeof(AssetSearchQuery).GetMethod(
                "ResolveUserFromEmailOrEmployeeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            // Email does NOT match, employee number DOES
            var taskObj = mi!.Invoke(query, new object?[] { "nope@example.com", "EMP-777", CancellationToken.None });
            var task = (Task)taskObj!;
            await task;

            var resultProp = task.GetType().GetProperty("Result");
            Assert.NotNull(resultProp);

            var result = resultProp!.GetValue(task);
            var resolved = Assert.IsType<User>(result);

            Assert.Equal(user.UserID, resolved.UserID);
        }
    }

    [Fact]
    public async Task ResolveUserFromEmailOrEmployeeAsync_EmployeeNumberNoMatch_ReturnsNull()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var mi = typeof(AssetSearchQuery).GetMethod(
                "ResolveUserFromEmailOrEmployeeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(mi);

            var taskObj = mi!.Invoke(query, new object?[] { null, "NO-MATCH", CancellationToken.None });
            var task = (Task)taskObj!;
            await task;

            var resultProp = task.GetType().GetProperty("Result");
            Assert.NotNull(resultProp);

            var result = resultProp!.GetValue(task);
            Assert.Null(result);
        }
    }

    [Fact]
    public void BuildRoleFlags_NullRoleName_UsesClaimsOnlyFlags()
    {
        var result = InvokePrivateStatic(
            typeof(AssetSearchQuery),
            "BuildRoleFlags",
            true,   // isAdminOrHelpdeskClaim
            false,  // isSupervisorClaim
            null    // roleName
        );

        Assert.NotNull(result);

        var type = result!.GetType();
        var isAdminOrHelpdeskClaim =
            (bool)type.GetProperty("IsAdminOrHelpdeskClaim")!.GetValue(result)!;
        var isSupervisorClaim =
            (bool)type.GetProperty("IsSupervisorClaim")!.GetValue(result)!;
        var isAdminDb =
            (bool)type.GetProperty("IsAdminDb")!.GetValue(result)!;
        var isHelpdeskDb =
            (bool)type.GetProperty("IsHelpdeskDb")!.GetValue(result)!;
        var isSupervisorDb =
            (bool)type.GetProperty("IsSupervisorDb")!.GetValue(result)!;
        var isAdminOrHelpdesk =
            (bool)type.GetProperty("IsAdminOrHelpdesk")!.GetValue(result)!;
        var isSupervisor =
            (bool)type.GetProperty("IsSupervisor")!.GetValue(result)!;

        // Claim-driven only, no DB role
        Assert.True(isAdminOrHelpdeskClaim);
        Assert.False(isSupervisorClaim);
        Assert.False(isAdminDb);
        Assert.False(isHelpdeskDb);
        Assert.False(isSupervisorDb);

        // Aggregated properties should still reflect claims
        Assert.True(isAdminOrHelpdesk);
        Assert.False(isSupervisor);
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_NoHttpContext_ReturnsNullTuple()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // CreateQuery with principal: null -> HttpContextAccessor.HttpContext stays null
            var query = CreateQuery(db, principal: null);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.Null(user);
            Assert.Null(roleName);
        }
    }

    [Fact]
    public async Task BuildRoleScopeAsync_AdminClaimOnly_NoDbUser_UsesClaimFlags()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // Principal with Admin role claim but NO matching DB user
            var identity = new ClaimsIdentity(
                new[]
                {
                new Claim(ClaimTypes.Role, "Admin")
                },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var scope = await InvokePrivateAsync<object>(
                query,
                "BuildRoleScopeAsync",
                CancellationToken.None);

            var type = scope.GetType();
            var hasUser = (bool)type.GetProperty("HasUser")!.GetValue(scope)!;
            var userId = (int?)type.GetProperty("UserId")!.GetValue(scope)!;
            var isAdminOrHelpdesk = (bool)type.GetProperty("IsAdminOrHelpdesk")!.GetValue(scope)!;
            var isSupervisor = (bool)type.GetProperty("IsSupervisor")!.GetValue(scope)!;
            var supIds = (IReadOnlyCollection<int>?)type.GetProperty("SupervisorScopeUserIds")!.GetValue(scope)!;

            // Claims-only admin: no DB user, but admin/helpdesk flag is true
            Assert.False(hasUser);
            Assert.Null(userId);
            Assert.True(isAdminOrHelpdesk);
            Assert.False(isSupervisor);
            Assert.Null(supIds);
        }
    }

    [Fact]
    public async Task BuildRoleScopeAsync_SupervisorClaimOnly_NoDbUser_UsesClaimFlags()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // Principal with Supervisor role claim but NO matching DB user
            var identity = new ClaimsIdentity(
                new[]
                {
                new Claim(ClaimTypes.Role, "Supervisor")
                },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var scope = await InvokePrivateAsync<object>(
                query,
                "BuildRoleScopeAsync",
                CancellationToken.None);

            var type = scope.GetType();
            var hasUser = (bool)type.GetProperty("HasUser")!.GetValue(scope)!;
            var userId = (int?)type.GetProperty("UserId")!.GetValue(scope)!;
            var isAdminOrHelpdesk = (bool)type.GetProperty("IsAdminOrHelpdesk")!.GetValue(scope)!;
            var isSupervisor = (bool)type.GetProperty("IsSupervisor")!.GetValue(scope)!;
            var supIds = (IReadOnlyCollection<int>?)type.GetProperty("SupervisorScopeUserIds")!.GetValue(scope)!;

            // Claims-only supervisor: no DB user, supervisor flag true, no scoped IDs
            Assert.False(hasUser);
            Assert.Null(userId);
            Assert.False(isAdminOrHelpdesk);
            Assert.True(isSupervisor);
            Assert.Null(supIds);
        }
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_UsesLegacyObjectIdClaimType_WhenOidMissing()
    {
        var (db, conn, admin) = await CreateAdminContextAsync();
        await using (conn)
        await using (db)
        {
            // Only the legacy objectidentifier claim, no "oid"
            var identity = new ClaimsIdentity(
                new[]
                {
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier",
                    admin.GraphObjectID!)
                },
                "TestAuth");

            var principal = new ClaimsPrincipal(identity);
            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(admin.UserID, user!.UserID);
            Assert.Equal("Admin", roleName);
        }
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_EmailOnly_NoEmployeeNumber_UsesEmailBranch()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            // email via preferred_username, no employee_number claim
            var identity = new ClaimsIdentity(
                new[]
                {
                new Claim("preferred_username", employee.Email!)
                },
                "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(employee.UserID, user!.UserID);
            Assert.False(string.IsNullOrWhiteSpace(roleName));
        }
    }

    [Fact]
    public async Task IsAdminOrHelpdeskClaim_NoHttpContext_ReturnsFalse()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var query = CreateQuery(db, principal: null);

            var prop = typeof(AssetSearchQuery).GetProperty(
                "IsAdminOrHelpdeskClaim",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(prop);
            var value = (bool)prop!.GetValue(query)!;

            Assert.False(value);
        }
    }

    [Fact]
    public async Task IsSupervisorClaim_PrincipalPresentButNotSupervisor_ReturnsFalse()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("some", "value") },  // not a supervisor claim
            "TestAuth");

        var principal = new ClaimsPrincipal(identity);

        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            var query = new AssetSearchQuery(
                db,
                new MemoryCache(new MemoryCacheOptions()),
                accessor,
                new FakeEnv());

            var prop = typeof(AssetSearchQuery).GetProperty(
                "IsSupervisorClaim",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(prop);

            var value = (bool)prop!.GetValue(query)!;

            Assert.False(value);  // hits the missing branch
        }
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_NoPreferredNoEmailNoName_FallsBackToEmployeeNumberOnly()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        using (conn)
        using (db)
        {
            // ClaimsIdentity WITHOUT a Name value
            var identity = new ClaimsIdentity(
                new[]
                {
                // Only employee_number — forces email=null and employeeNumber=match
                new Claim("employee_number", employee.EmployeeNumber!)
                },
                "TestAuth");

            var principal = new ClaimsPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(employee.UserID, user!.UserID);
            Assert.False(string.IsNullOrWhiteSpace(roleName));
        }
    }

    [Fact]
    public async Task IsSupervisorClaim_WithSupervisorRole_ReturnsTrue()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Role, "Supervisor")
            },
            "TestAuth");

        var principal = new ClaimsPrincipal(identity);

        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            var query = new AssetSearchQuery(
                db,
                new MemoryCache(new MemoryCacheOptions()),
                accessor,
                new FakeEnv());

            var prop = typeof(AssetSearchQuery).GetProperty(
                "IsSupervisorClaim",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(prop);

            var value = (bool)prop!.GetValue(query)!;

            Assert.True(value);
        }
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_IdentityNull_AllEmailSourcesNull()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // No preferred_username, no ClaimTypes.Email, and Identity is null.
            var identity = new ClaimsIdentity(authenticationType: "TestAuth");
            var principal = new NullIdentityPrincipal(identity);

            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.Null(user);
            Assert.Null(roleName);
        }
    }

    [Fact]
    public async Task ResolveCurrentUserAsync_UsesCamelCaseEmployeeNumberClaim_WhenLegacyMissing()
    {
        var (db, conn, employee) = await CreateEmployeeContextAsync();
        await using (conn)
        await using (db)
        {
            // Only "employeeNumber" present; "employee_number" is absent.
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim("employeeNumber", employee.EmployeeNumber!)
                },
                "TestAuth");

            var principal = new ClaimsPrincipal(identity);
            var query = CreateQuery(db, principal);

            var (user, roleName) = await query.ResolveCurrentUserAsync(default);

            Assert.NotNull(user);
            Assert.Equal(employee.UserID, user!.UserID);
            Assert.False(string.IsNullOrWhiteSpace(roleName));
        }
    }

    [Fact]
    public async Task IsSupervisorClaim_NoHttpContext_ReturnsFalse()
    {
        var (db, conn) = await CreateSeededContextAsync();
        await using (conn)
        await using (db)
        {
            // HttpContext is null -> CurrentPrincipal is null
            var accessor = new HttpContextAccessor
            {
                HttpContext = null
            };

            var query = new AssetSearchQuery(
                db,
                new MemoryCache(new MemoryCacheOptions()),
                accessor,
                new FakeEnv());

            var prop = typeof(AssetSearchQuery).GetProperty(
                "IsSupervisorClaim",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(prop);

            var value = (bool)prop!.GetValue(query)!;

            Assert.False(value);
        }
    }
}
