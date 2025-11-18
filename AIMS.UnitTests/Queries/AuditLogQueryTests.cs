using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using AIMS.UnitTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Integration-style and unit-style tests that exercise <c>AuditLogQuery</c> read models,
/// paged search, upsert operations, validation branches, and broadcast event projections.
/// </summary>
/// <remarks>
/// Organized with regions for maintainability and to surface clear topic groupings in DocFX.
/// The tests intentionally seed an in-memory EF Core database to validate ordering, filters,
/// branch coverage, and event payload shapes under a variety of inputs.
/// </remarks>
public sealed class AuditLogQueryTests
{
    // =====================================================================
    // Test Bootstrapping
    // =====================================================================

    #region TEST BOOTSTRAPPING

    /// <summary>
    /// Creates a new in-memory database with minimal user/role and hardware/software entities,
    /// returning IDs via <paramref name="u1"/>, <paramref name="u2"/>, <paramref name="hw1"/>,
    /// and <paramref name="sw1"/>.
    /// </summary>
    /// <param name="u1">Identifier of the first user (output).</param>
    /// <param name="u2">Identifier of the second user (output).</param>
    /// <param name="hw1">Identifier of the seeded hardware asset (output).</param>
    /// <param name="sw1">Identifier of the seeded software asset (output).</param>
    /// <returns>
    /// Tuple containing an initialized <see cref="AimsDbContext"/> and a fake
    /// <see cref="IAuditEventBroadcaster"/> for event assertions.
    /// </returns>
    private static (AimsDbContext db, IAuditEventBroadcaster bus) SeedBasic(
        out int u1, out int u2, out int hw1, out int sw1)
    {
        var db = TestDb.Create();
        var bus = new FakeAuditEventBroadcaster();

        // Role
        var role = new Role { RoleName = "Admin" };
        db.Roles.Add(role);
        db.SaveChanges();

        // Users
        var user1 = new User { FullName = "Alice Admin", Email = "alice@ex.com", RoleID = role.RoleID };
        var user2 = new User { FullName = "Bob Builder", Email = "bob@ex.com", RoleID = role.RoleID };
        db.Users.AddRange(user1, user2);
        db.SaveChanges();

        // Assets
        var hw = new Hardware { AssetName = "Laptop X", SerialNumber = "SN-001", AssetType = "Laptop", Status = "Available" };
        var sw = new Software { SoftwareName = "Office", SoftwareType = "Suite", SoftwareLicenseKey = "LIC-AAA" };
        db.HardwareAssets.Add(hw);
        db.SoftwareAssets.Add(sw);
        db.SaveChanges();

        u1 = user1.UserID; u2 = user2.UserID;
        hw1 = hw.HardwareID; sw1 = sw.SoftwareID;
        return (db, bus);
    }

    /// <summary>
    /// Adds three representative audit rows with distinct actions and kinds.
    /// </summary>
    /// <param name="db">Context to mutate.</param>
    /// <param name="u1">Existing user identifier.</param>
    /// <param name="u2">Existing user identifier.</param>
    /// <param name="hw1">Existing hardware identifier.</param>
    /// <param name="sw1">Existing software identifier.</param>
    private static void SeedAuditRows(AimsDbContext db, int u1, int u2, int hw1, int sw1)
    {
        var now = DateTime.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog
            {
                TimestampUtc = now.AddMinutes(-10),
                UserID = u1,
                Action = "Create",
                Description = "created hardware",
                AssetKind = AssetKind.Hardware,
                HardwareID = hw1
            },
            new AuditLog
            {
                TimestampUtc = now.AddMinutes(-5),
                UserID = u2,
                Action = "Assign",
                Description = "assigned to Bob",
                AssetKind = AssetKind.Hardware,
                HardwareID = hw1
            },
            new AuditLog
            {
                TimestampUtc = now.AddMinutes(-1),
                UserID = u1,
                Action = "Update",
                Description = "updated license",
                AssetKind = AssetKind.Software,
                SoftwareID = sw1
            }
        );
        db.SaveChanges();
    }

    /// <summary>
    /// Constructs the <see cref="AuditLogQuery"/> using the shared fake broadcaster and cache factory.
    /// </summary>
    /// <param name="db">Context instance.</param>
    /// <param name="bus">Broadcaster instance.</param>
    /// <returns>Configured <see cref="AuditLogQuery"/>.</returns>
    private static AuditLogQuery CreateQuery(AimsDbContext db, IAuditEventBroadcaster bus)
        => new AuditLogQuery(db, bus, TestDb.CreateCache());

    #endregion

    // =====================================================================
    // READ tests
    // =====================================================================

    #region READ TESTS

    /// <summary>
    /// Verifies <see cref="AuditLogQuery.GetAllAuditRecordsAsync(System.Threading.CancellationToken)"/>
    /// returns records ordered newest-first and includes child change rows.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task GetAllAuditRecords_ReturnsOrderedWithChanges()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);

        // Add a change row to the oldest entry
        var target = await db.AuditLogs.OrderBy(a => a.AuditLogID).FirstAsync();
        target.Changes.Add(new AuditLogChange { Field = "Status", OldValue = "Available", NewValue = "Assigned" });
        db.SaveChanges();

        var q = CreateQuery(db, bus);
        var rows = await q.GetAllAuditRecordsAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal("Update", rows[0].Action);
        Assert.Equal("Assign", rows[1].Action);
        Assert.Equal("Create", rows[2].Action);
        Assert.Single(rows[2].Changes);
    }

    /// <summary>
    /// Ensures a specific record is retrieved by identifier and projected to DTO.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task GetAuditRecord_ById_ReturnsDto()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);
        var id = await db.AuditLogs.Select(a => a.AuditLogID).FirstAsync();

        var q = CreateQuery(db, bus);
        var dto = await q.GetAuditRecordAsync(id);

        Assert.NotNull(dto);
        Assert.Equal(id, dto!.AuditLogID);
    }

    /// <summary>
    /// Exercises recent events by asset type and validates kind segregation.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task GetRecentAuditRecords_ByHardware_And_Software()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);

        var q = CreateQuery(db, bus);

        var hwRows = await q.GetRecentAuditRecordsAsync("Hardware", hw1, 5);
        Assert.All(hwRows, r => Assert.Equal(AssetKind.Hardware, r.AssetKind));

        var swRows = await q.GetRecentAuditRecordsAsync("Software", sw1, 5);
        Assert.All(swRows, r => Assert.Equal(AssetKind.Software, r.AssetKind));
    }

    /// <summary>
    /// Confirms invalid asset kind input defaults to <see cref="AssetKind.Hardware"/>.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task GetRecentAuditRecords_InvalidKind_DefaultsToHardware()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);

        var q = CreateQuery(db, bus);
        var rows = await q.GetRecentAuditRecordsAsync("NOT_A_KIND", hw1, 5);

        Assert.All(rows, r => Assert.Equal(AssetKind.Hardware, r.AssetKind));
    }

    #endregion

    // =====================================================================
    // SEARCH tests
    // =====================================================================

    #region SEARCH TESTS

    /// <summary>
    /// Validates that an unfiltered search returns all rows in deterministic, newest-first order.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task Search_NoFilters_ReturnsAllOrdered_Paged()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);

        var q = CreateQuery(db, bus);
        var result = await q.SearchAsync(
            page: 1, pageSize: 10, q: null,
            fromUtc: null, toUtc: null,
            actor: null, action: null,
            kind: null, hardwareId: null, softwareId: null);

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal("Update", result.Items[0].Action);
        Assert.Equal("Assign", result.Items[1].Action);
        Assert.Equal("Create", result.Items[2].Action);
    }

    /// <summary>
    /// Exercises free-text filter across description, action, and user name fields.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task Search_TextFilter_MatchesDescription_Action_User()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);

        var q = CreateQuery(db, bus);

        var byDesc = await q.SearchAsync(1, 10, "license", null, null, null, null, null, null, null);
        Assert.Single(byDesc.Items);
        Assert.Equal("Update", byDesc.Items[0].Action);

        var byAction = await q.SearchAsync(1, 10, "Assign", null, null, null, null, null, null, null);
        Assert.Single(byAction.Items);
        Assert.Equal("Assign", byAction.Items[0].Action);

        var byUser = await q.SearchAsync(1, 10, "Alice", null, null, null, null, null, null, null);
        Assert.Equal(2, byUser.Items.Count);
    }

    /// <summary>
    /// Covers date range filtering, actor filter, action filter, kind filter,
    /// and target-specific filters for hardware and software identifiers.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task Search_DateRange_Actor_Action_Kind_Targets_AllFilterPathsCovered()
    {
        var (db, bus) = SeedBasic(out var u1, out var u2, out var hw1, out var sw1);
        SeedAuditRows(db, u1, u2, hw1, sw1);

        var min = await db.AuditLogs.MinAsync(a => a.TimestampUtc);
        var max = await db.AuditLogs.MaxAsync(a => a.TimestampUtc);

        var q = CreateQuery(db, bus);

        var byRange = await q.SearchAsync(1, 10, null, min.AddMinutes(-1), max.AddMinutes(1), null, null, null, null, null);
        Assert.Equal(3, byRange.Items.Count);

        var byActor = await q.SearchAsync(1, 10, null, null, null, "Bob Builder", null, null, null, null);
        Assert.Single(byActor.Items);
        Assert.Equal("Assign", byActor.Items[0].Action);

        var byActorId = await q.SearchAsync(1, 10, null, null, null, u2.ToString(), null, null, null, null);
        Assert.Single(byActorId.Items);
        Assert.Equal("Assign", byActorId.Items[0].Action);

        var byActorIdMissing = await q.SearchAsync(1, 10, null, null, null, "999999", null, null, null, null);
        Assert.Empty(byActorIdMissing.Items);

        var byAction = await q.SearchAsync(1, 10, null, null, null, null, "Create", null, null, null);
        Assert.Single(byAction.Items);
        Assert.Equal("Create", byAction.Items[0].Action);

        var byHwKind = await q.SearchAsync(1, 10, null, null, null, null, null, AssetKind.Hardware, null, null);
        Assert.Equal(2, byHwKind.Items.Count);

        var byHwId = await q.SearchAsync(1, 10, null, null, null, null, null, null, hw1, null);
        Assert.Equal(2, byHwId.Items.Count);

        var bySwId = await q.SearchAsync(1, 10, null, null, null, null, null, null, null, sw1);
        Assert.Single(bySwId.Items);
        Assert.Equal(AssetKind.Software, bySwId.Items[0].AssetKind);
    }

    #endregion

    // =====================================================================
    // CREATE / UPDATE + VALIDATION
    // =====================================================================

    #region UPSERT & VALIDATION TESTS

    /// <summary>
    /// Inserts a hardware audit record with snapshot and change rows, and verifies that a
    /// broadcast event is emitted with expected fields.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Insert_Hardware_WithSnapshot_AndChanges_Broadcasted()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out var hw1, out _);
        var broadcaster = (FakeAuditEventBroadcaster)bus;
        var q = CreateQuery(db, bus);

        var dto = new CreateAuditRecordDto
        {
            ExternalId = null,
            UserID = u1,
            Action = "Assign",
            Description = "Assigned laptop",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1,
            SoftwareID = null,
            SnapshotJson = "{\"k\":\"v\"}",
            Changes = new List<CreateAuditLogChangeDto>
            {
                new() { Field = "Status", OldValue = "Available", NewValue = "Assigned" }
            }
        };

        var id = await q.CreateAuditRecordAsync(dto);
        var saved = await db.AuditLogs.Include(a => a.Changes).FirstOrDefaultAsync(a => a.AuditLogID == id);

        Assert.NotNull(saved);
        Assert.Equal(AssetKind.Hardware, saved!.AssetKind);
        Assert.Null(saved.SoftwareID);
        Assert.Equal(hw1, saved.HardwareID);
        Assert.Single(saved.Changes);
        Assert.Single(broadcaster.Events);
        Assert.Equal("Assign", broadcaster.Events[0].Type);
        Assert.Contains("Hardware", broadcaster.Events[0].Target);
    }

    /// <summary>
    /// Inserts a software audit record and validates target population and broadcast result.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Insert_Software_SetsTargets_AndBroadcasts()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out var sw1);
        var broadcaster = (FakeAuditEventBroadcaster)bus;
        var q = CreateQuery(db, bus);

        var dto = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "Create",
            Description = "Created license",
            AssetKind = AssetKind.Software,
            SoftwareID = sw1
        };

        var id = await q.CreateAuditRecordAsync(dto);
        var saved = await db.AuditLogs.FirstAsync(a => a.AuditLogID == id);

        Assert.Equal(AssetKind.Software, saved.AssetKind);
        Assert.Null(saved.HardwareID);
        Assert.Equal(sw1, saved.SoftwareID);
        Assert.Single(((FakeAuditEventBroadcaster)bus).Events);
        Assert.Contains("Software", ((FakeAuditEventBroadcaster)bus).Events[0].Target);
    }

    /// <summary>
    /// Demonstrates the ExternalId upsert path by creating and then updating the same logical record,
    /// and ensures change rows are appended and a subsequent broadcast is emitted.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Update_ByExternalId_AppendsChanges_AndBroadcasts()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out var hw1, out _);
        var broadcaster = (FakeAuditEventBroadcaster)bus;
        var q = CreateQuery(db, bus);

        var ext = Guid.NewGuid();

        var first = new CreateAuditRecordDto
        {
            ExternalId = ext,
            UserID = u1,
            Action = "Create",
            Description = "Initial create",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1,
            SnapshotJson = null
        };
        var id1 = await q.CreateAuditRecordAsync(first);

        var second = new CreateAuditRecordDto
        {
            ExternalId = ext,
            UserID = u1,
            Action = "Update",
            Description = "Edit description",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1,
            SnapshotJson = "{\"after\":1}",
            Changes = new List<CreateAuditLogChangeDto>
            {
                new() { Field = "Desc", OldValue = "Initial create", NewValue = "Edit description" }
            }
        };
        var id2 = await q.CreateAuditRecordAsync(second);

        Assert.Equal(id1, id2);

        var saved = await db.AuditLogs.Include(a => a.Changes).FirstAsync(a => a.AuditLogID == id1);
        Assert.Contains(saved.Changes, c => c.Field == "Desc");

        Assert.Equal(2, broadcaster.Events.Count);
        Assert.Equal("Update", broadcaster.Events.Last().Type);
    }

    /// <summary>
    /// Starts with a hardware row then flips to software on the same ExternalId,
    /// covering both branches of target-setting code paths.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_SetTargets_BranchCoverage_InsertThenFlipKindOnUpdate()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out var hw1, out var sw1);
        var q = CreateQuery(db, bus);
        var ext = Guid.NewGuid();

        // Insert as Hardware
        var dtoH = new CreateAuditRecordDto
        {
            ExternalId = ext,
            UserID = u1,
            Action = "Create",
            Description = "seed",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1
        };
        var id = await q.CreateAuditRecordAsync(dtoH);
        var row1 = await db.AuditLogs.FirstAsync(a => a.AuditLogID == id);
        Assert.Equal(hw1, row1.HardwareID);
        Assert.Null(row1.SoftwareID);

        // Flip to Software
        var dtoS = new CreateAuditRecordDto
        {
            ExternalId = ext,
            UserID = u1,
            Action = "Update",
            Description = "flip",
            AssetKind = AssetKind.Software,
            SoftwareID = sw1
        };
        await q.CreateAuditRecordAsync(dtoS);
        var row2 = await db.AuditLogs.FirstAsync(a => a.AuditLogID == id);
        Assert.Null(row2.HardwareID);
        Assert.Equal(sw1, row2.SoftwareID);
    }

    /// <summary>
    /// Validates actor existence guard by passing a non-existent user ID.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Validate_UserNotExists_Throws()
    {
        var (db, bus) = SeedBasic(out _, out _, out var hw1, out _);
        var q = CreateQuery(db, bus);

        var dto = new CreateAuditRecordDto
        {
            UserID = 99999,
            Action = "X",
            Description = "Y",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(dto));
    }

    /// <summary>
    /// Verifies the hardware target validation including missing, invalid, and conflicting IDs.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Validate_Hardware_Missing_Invalid_Or_WithSoftwareId_Throws()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out var hw1, out _);
        var q = CreateQuery(db, bus);

        var missing = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = AssetKind.Hardware,
            HardwareID = null
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(missing));

        var invalid = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = AssetKind.Hardware,
            HardwareID = 123456
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(invalid));

        var wrong = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1,
            SoftwareID = 42
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(wrong));
    }

    /// <summary>
    /// Verifies the software target validation including missing, invalid, and conflicting IDs.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Validate_Software_Missing_Invalid_Or_WithHardwareId_Throws()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out var sw1);
        var q = CreateQuery(db, bus);

        var missing = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = AssetKind.Software,
            SoftwareID = null
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(missing));

        var invalid = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = AssetKind.Software,
            SoftwareID = 987654
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(invalid));

        var wrong = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = AssetKind.Software,
            SoftwareID = sw1,
            HardwareID = 1
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(wrong));
    }

    /// <summary>
    /// Confirms basic input guards for required <c>Action</c> and <c>Description</c> fields.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Guards_Action_And_Description_Required()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out var hw1, out _);
        var q = CreateQuery(db, bus);

        var noAction = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "   ",
            Description = "D",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1
        };
        await Assert.ThrowsAsync<ArgumentException>(() => q.CreateAuditRecordAsync(noAction));

        var noDesc = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "Create",
            Description = "",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1
        };
        await Assert.ThrowsAsync<ArgumentException>(() => q.CreateAuditRecordAsync(noDesc));
    }

    /// <summary>
    /// Ensures null DTO input is rejected with <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Guard_NullDto_Throws()
    {
        var (db, bus) = SeedBasic(out _, out _, out _, out _);
        var q = CreateQuery(db, bus);
        await Assert.ThrowsAsync<ArgumentNullException>(() => q.CreateAuditRecordAsync(null!));
    }

    /// <summary>
    /// Ensures unsupported <see cref="AssetKind"/> is rejected.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task CreateAudit_Unknown_AssetKind_Throws()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out _);
        var q = CreateQuery(db, bus);

        var dto = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "A",
            Description = "D",
            AssetKind = (AssetKind)999
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => q.CreateAuditRecordAsync(dto));
    }

    #endregion

    // =====================================================================
    // BROADCAST event payload tests
    // =====================================================================

    #region BROADCAST PAYLOAD TESTS

    /// <summary>
    /// Confirms event payload fields after a successful insert include expected
    /// type, target family, user label, and hash, and that ID falls back to AuditLogID
    /// when ExternalId is empty.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task Broadcast_Event_Fields_Are_WellFormed()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out var hw1, out _);
        var broadcaster = (FakeAuditEventBroadcaster)bus;
        var q = CreateQuery(db, bus);

        var dto = new CreateAuditRecordDto
        {
            UserID = u1,
            Action = "Create",
            Description = "hello",
            AssetKind = AssetKind.Hardware,
            HardwareID = hw1
        };

        var id = await q.CreateAuditRecordAsync(dto);
        Assert.True(id > 0);

        var ev = broadcaster.Events.LastOrDefault();
        Assert.NotNull(ev);
        Assert.Equal("Create", ev!.Type);
        Assert.Contains("Hardware", ev.Target);
        Assert.Contains("Alice Admin", ev.User);
        Assert.False(string.IsNullOrWhiteSpace(ev.Hash));
        Assert.Equal(id.ToString(), ev.Id);
    }

    /// <summary>
    /// Ensures user-name lookup fallback is used when the user FullName becomes null
    /// and validates ExternalId passthrough to the event Id.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task Broadcast_Event_UserNameNull_Fallback_And_ExternalId_NonEmpty()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out var sw1);
        var broadcaster = (FakeAuditEventBroadcaster)bus;
        var q = CreateQuery(db, bus);

        var ext = Guid.NewGuid();
        var dto1 = new CreateAuditRecordDto
        {
            ExternalId = ext,
            UserID = u1,
            Action = "Create",
            Description = "software init",
            AssetKind = AssetKind.Software,
            SoftwareID = sw1
        };
        var id = await q.CreateAuditRecordAsync(dto1);

        var user = await db.Users.FindAsync(u1);
        user!.FullName = null!;
        await db.SaveChangesAsync();

        var dto2 = new CreateAuditRecordDto
        {
            ExternalId = ext,
            UserID = u1,
            Action = "Update",
            Description = "after",
            AssetKind = AssetKind.Software,
            SoftwareID = sw1
        };
        await q.CreateAuditRecordAsync(dto2);

        var ev = broadcaster.Events.Last();
        Assert.Contains("Software", ev.Target);
        Assert.Contains($"User#{u1}", ev.User);
        Assert.Equal("Update", ev.Type);
        Assert.Equal(ext.ToString(), ev.Id);
    }

    #endregion

    // =====================================================================
    // Private-method branches (reflection helpers + tests)
    // =====================================================================

    #region PRIVATE BRANCHES (REFLECTION)

    /// <summary>
    /// Invokes the private <c>BuildEventDtoAsync</c> method using reflection to cover
    /// target-label and user-label branches that are not otherwise reachable via the public surface.
    /// </summary>
    /// <param name="q">The <see cref="AuditLogQuery"/> instance.</param>
    /// <param name="db">Database context for optional persistence.</param>
    /// <param name="row">Audit log entity used to build the event DTO.</param>
    /// <param name="save">
    /// When <see langword="true"/>, the entity is persisted prior to invocation;
    /// otherwise the projection occurs on a transient instance.
    /// </param>
    /// <returns>The constructed <see cref="AIMS.Contracts.AuditEventDto"/>.</returns>
    private static async Task<AIMS.Contracts.AuditEventDto> InvokeBuildEventAsync(
        AuditLogQuery q,
        AimsDbContext db,
        AuditLog row,
        bool save = true)
    {
        if (save)
        {
            db.AuditLogs.Add(row);
            await db.SaveChangesAsync();
        }

        var mi = typeof(AuditLogQuery).GetMethod("BuildEventDtoAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);

        var task = (Task)mi!.Invoke(q, new object[] { row, default(System.Threading.CancellationToken) })!;
        await task.ConfigureAwait(false);

        var resultProp = task.GetType().GetProperty("Result");
        return (AIMS.Contracts.AuditEventDto)resultProp!.GetValue(task)!;
    }

    /// <summary>
    /// Covers the hardware generic-target branch where <c>HardwareID</c> is missing,
    /// ExternalId is empty, and a standard user name is present.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task BuildEvent_TargetHardware_GenericLabel_WhenHardwareIdMissing()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out _);
        var q = CreateQuery(db, bus);

        var row = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserID = u1,
            Action = "X",
            Description = "Y",
            AssetKind = AssetKind.Hardware,
            HardwareID = null,   // intentionally missing
            ExternalId = Guid.Empty
        };

        var ev = await InvokeBuildEventAsync(q, db, row, save: true);

        Assert.Equal("X", ev.Type);
        Assert.Equal("Hardware", ev.Target);
        Assert.Equal(row.AuditLogID.ToString(), ev.Id);
        Assert.Contains("Alice Admin", ev.User);
    }

    /// <summary>
    /// Covers the software generic-target branch with a non-empty ExternalId and null description
    /// to ensure details are coalesced to an empty string.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task BuildEvent_TargetSoftware_GenericLabel_WhenSoftwareIdMissing_And_DescriptionNull()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out _);
        var q = CreateQuery(db, bus);

        // Not saved to avoid model nullability constraints for Description.
        var row = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserID = u1,
            Action = "X",
            Description = null!,
            AssetKind = AssetKind.Software,
            SoftwareID = null,
            ExternalId = Guid.NewGuid()
        };

        var ev = await InvokeBuildEventAsync(q, db, row, save: false);

        Assert.Equal("X", ev.Type);
        Assert.Equal("Software", ev.Target);
        Assert.Equal(row.ExternalId.ToString(), ev.Id);
        Assert.Equal(string.Empty, ev.Details);
    }

    /// <summary>
    /// Forces user-name resolution to fall back to <c>User#&lt;id&gt;</c> and confirms target behavior without IDs.
    /// </summary>
    /// <returns>A task that completes when the assertion is finished.</returns>
    [Fact]
    public async Task BuildEvent_UserNameNull_Fallback_OnDirectRow()
    {
        var (db, bus) = SeedBasic(out var u1, out _, out _, out _);
        var u = await db.Users.FindAsync(u1);
        u!.FullName = null!;
        await db.SaveChangesAsync();

        var q = CreateQuery(db, bus);

        var row = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserID = u1,
            Action = "Ping",
            Description = "D",
            AssetKind = AssetKind.Hardware,
            ExternalId = Guid.Empty
        };

        var ev = await InvokeBuildEventAsync(q, db, row, save: true);

        Assert.Contains($"User#{u1}", ev.User);
        Assert.Equal("Hardware", ev.Target);
    }

    #endregion
}
