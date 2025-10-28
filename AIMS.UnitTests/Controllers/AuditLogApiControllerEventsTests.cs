using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Contracts;
using AIMS.Controllers.Api;
using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Dtos.Common;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace AIMS.UnitTests.Controllers
{
    /// <summary>
    /// Unit tests for <see cref="AuditLogApiController"/> focused on:
    /// - CRUD surface correctness
    /// - Paged search contract
    /// - Polling endpoint behavior (projection, dedupe, ETag, caching)
    /// </summary>
    public sealed class AuditLogApiControllerTests
    {
        /// <summary>
        /// Reads a named property from an anonymous payload in a type-safe manner to avoid dynamic binder usage.
        /// </summary>
        /// <typeparam name="T">Expected property type.</typeparam>
        /// <param name="anon">Anonymous object returned by the controller.</param>
        /// <param name="name">Property name to read (e.g., "items", "nextSince").</param>
        /// <returns>The property value cast to <typeparamref name="T"/>.</returns>
        private static T GetProp<T>(object anon, string name)
        {
            var p = anon.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(p);
            var val = p!.GetValue(anon);
            if (val is null) return default!;
            return (T)val;
        }

        /// <summary>
        /// Creates an isolated in-memory <see cref="AimsDbContext"/> and seeds:
        /// - Two users
        /// - A mix of hardware/software audit logs within the last 24h
        /// - A very old audit log (outside 24h) to exercise default window behavior
        /// - Cases to cover generic target labels and deduplication by ExternalId
        /// </summary>
        /// <param name="name">Unique database name for test isolation.</param>
        /// <returns>An initialized <see cref="AimsDbContext"/> ready for tests.</returns>
        private static AimsDbContext MakeDb(string name)
        {
            var opts = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(name)
                .EnableSensitiveDataLogging()
                .Options;

            var db = new AimsDbContext(opts);
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            // Roles/Users
            db.Roles.Add(new Role { RoleID = 1, RoleName = "Admin" });
            db.Users.Add(new User { UserID = 1, FullName = "Alice Admin", Email = "alice@example.com", RoleID = 1 });
            db.Users.Add(new User { UserID = 2, FullName = "Bob User", Email = "bob@example.com", RoleID = 1 });

            var now = DateTime.UtcNow;

            // Within 24h
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 1,
                ExternalId = Guid.Empty,                  // exercises Id fallback to AuditLogID
                TimestampUtc = now.AddMinutes(-60),
                UserID = 1,
                Action = "Create",
                Description = "created",
                AssetKind = AssetKind.Hardware,
                HardwareID = 101
            });

            // Two entries sharing the same ExternalId (dedupe: newest wins)
            var shared = Guid.NewGuid();
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 2,
                ExternalId = shared,
                TimestampUtc = now.AddMinutes(-40),
                UserID = 2,
                Action = "Update",
                Description = "older-update",
                AssetKind = AssetKind.Software,
                SoftwareID = 201
            });
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 3,
                ExternalId = shared,                 // same logical id, newer
                TimestampUtc = now.AddMinutes(-5),
                UserID = 2,
                Action = "Update",
                Description = "newer-update",
                AssetKind = AssetKind.Software,
                SoftwareID = 201
            });

            // Very old: outside default 24h window
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 4,
                ExternalId = Guid.NewGuid(),
                TimestampUtc = now.AddDays(-2),
                UserID = 1,
                Action = "Delete",
                Description = "very-old",
                AssetKind = AssetKind.Hardware,
                HardwareID = 102
            });

            // Hardware with NO HardwareID -> "Hardware" target label branch
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 5,
                ExternalId = Guid.NewGuid(),
                TimestampUtc = now.AddMinutes(-10),
                UserID = 1,
                Action = "Ping",
                Description = "hw-generic",
                AssetKind = AssetKind.Hardware,
                HardwareID = null
            });

            // Software with NO SoftwareID -> "Software" target label branch
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 6,
                ExternalId = Guid.Empty,
                TimestampUtc = now.AddMinutes(-8),
                UserID = 1,
                Action = "Ping2",
                Description = "sw-generic",
                AssetKind = AssetKind.Software,
                SoftwareID = null
            });

            db.SaveChanges();
            return db;
        }

        /// <summary>
        /// Creates an <see cref="AuditLogQuery"/> with mocked broadcaster and real <see cref="IMemoryCache"/>.
        /// </summary>
        /// <param name="db">Seeded context for the query.</param>
        /// <returns>Configured <see cref="AuditLogQuery"/> instance.</returns>
        private static AuditLogQuery MakeQuery(AimsDbContext db)
        {
            var broadcaster = new Mock<IAuditEventBroadcaster>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new AuditLogQuery(db, broadcaster.Object, cache);
        }

        /// <summary>
        /// Creates an <see cref="AuditLogApiController"/> with a fresh <see cref="DefaultHttpContext"/>.
        /// </summary>
        /// <param name="query">Query dependency.</param>
        /// <returns>Configured controller instance.</returns>
        private static AuditLogApiController MakeController(AuditLogQuery query)
        {
            var ctrl = new AuditLogApiController(query)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            return ctrl;
        }

        // ---------------- basic CRUD/search endpoints -----------------------------------

        /// <summary>
        /// Ensures <c>GET /api/audit/get</c> returns 404 for a non-existent audit ID.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when a <see cref="NotFoundResult"/> is returned.
        /// </returns>
        [Fact]
        public async Task GetAuditRecord_NotFound_Returns404()
        {
            using var db = MakeDb(nameof(GetAuditRecord_NotFound_Returns404));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.GetAuditRecord(999, CancellationToken.None);
            Assert.IsType<NotFoundResult>(res);
        }

        /// <summary>
        /// Ensures <c>GET /api/audit/get</c> returns 200 and the expected DTO for a valid ID.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when an <see cref="OkObjectResult"/> with the expected <see cref="GetAuditRecordDto"/> is returned.
        /// </returns>
        [Fact]
        public async Task GetAuditRecord_Found_ReturnsOk()
        {
            using var db = MakeDb(nameof(GetAuditRecord_Found_ReturnsOk));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.GetAuditRecord(1, CancellationToken.None) as OkObjectResult;
            Assert.NotNull(res);
            var dto = Assert.IsType<GetAuditRecordDto>(res!.Value);
            Assert.Equal(1, dto.AuditLogID);
        }

        /// <summary>
        /// Ensures <c>GET /api/audit/list</c> returns a collection of records.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when an <see cref="OkObjectResult"/> containing a non-empty <see cref="List{T}"/> is returned.
        /// </returns>
        [Fact]
        public async Task GetAllRecords_Ok()
        {
            using var db = MakeDb(nameof(GetAllRecords_Ok));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.GetAllRecords(CancellationToken.None) as OkObjectResult;
            Assert.NotNull(res);
            var list = Assert.IsAssignableFrom<List<GetAuditRecordDto>>(res!.Value);
            Assert.True(list.Count >= 3);
        }

        /// <summary>
        /// Ensures <c>GET /api/audit/recent</c> returns recent records for a given asset.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when an <see cref="OkObjectResult"/> containing at least one record is returned.
        /// </returns>
        [Fact]
        public async Task GetRecentLogs_Ok()
        {
            using var db = MakeDb(nameof(GetRecentLogs_Ok));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.GetRecentLogs("Hardware", 101, 5, CancellationToken.None) as OkObjectResult;
            Assert.NotNull(res);
            var list = Assert.IsAssignableFrom<List<GetAuditRecordDto>>(res!.Value);
            Assert.True(list.Count >= 1);
        }

        /// <summary>
        /// Ensures <c>GET /api/auditlog</c> returns a paged result with items.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when an <see cref="OkObjectResult"/> with a <see cref="PagedResult{T}"/> is returned.
        /// </returns>
        [Fact]
        public async Task Search_Ok()
        {
            using var db = MakeDb(nameof(Search_Ok));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.Search(1, 10, null, null, null, null, null, null, null, null, CancellationToken.None)
                as OkObjectResult;
            Assert.NotNull(res);
            var page = Assert.IsType<PagedResult<AuditLogRowDto>>(res!.Value);
            Assert.True(page.Items.Count >= 1);
        }

        /// <summary>
        /// Ensures <c>POST /api/audit/create</c> returns 400 when the user does not exist.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when a <see cref="BadRequestObjectResult"/> with a suitable message is returned.
        /// </returns>
        [Fact]
        public async Task CreateRecord_BadRequest_WhenUserMissing()
        {
            using var db = MakeDb(nameof(CreateRecord_BadRequest_WhenUserMissing));
            var ctrl = MakeController(MakeQuery(db));

            var req = new CreateAuditRecordDto
            {
                UserID = 999, // invalid user
                Action = "X",
                Description = "Y",
                AssetKind = AssetKind.Hardware,
                HardwareID = 101
            };

            var resp = await ctrl.CreateRecord(req, CancellationToken.None);
            var bad = Assert.IsType<BadRequestObjectResult>(resp);
            Assert.Contains("does not exist", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ensures <c>POST /api/audit/create</c> returns 201 (CreatedAtAction) on success.
        /// Covers the CreatedAtAction line in the controller.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when a <see cref="CreatedAtActionResult"/> is returned and includes <c>auditLogId</c>.
        /// </returns>
        [Fact]
        public async Task CreateRecord_Success_ReturnsCreatedAtAction()
        {
            var opts = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(nameof(CreateRecord_Success_ReturnsCreatedAtAction))
                .EnableSensitiveDataLogging()
                .Options;
            using var db = new AimsDbContext(opts);
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            // minimal seed for validation
            db.Roles.Add(new Role { RoleID = 1, RoleName = "Admin" });
            db.Users.Add(new User { UserID = 1, FullName = "Alice Admin", Email = "alice@example.com", RoleID = 1 });
            db.HardwareAssets.Add(new Hardware { HardwareID = 101, AssetName = "HW-101", AssetType = "Laptop" });
            db.SaveChanges();

            var ctrl = MakeController(MakeQuery(db));

            var req = new CreateAuditRecordDto
            {
                UserID = 1,
                Action = "Assign",
                Description = "ok",
                AssetKind = AssetKind.Hardware,
                HardwareID = 101
            };

            var resp = await ctrl.CreateRecord(req, CancellationToken.None);
            var created = Assert.IsType<CreatedAtActionResult>(resp);
            Assert.Equal(nameof(AuditLogApiController.GetAuditRecord), created.ActionName);
            Assert.True(created.RouteValues!.ContainsKey("auditLogId"));
        }

        // ---------------- /api/audit/events ---------------------------------------------

        /// <summary>
        /// Ensures invalid <c>since</c> uses the default 24h window and excludes very old items.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when all items in the result are newer than 24h and the seeded "very-old" item is excluded.
        /// </returns>
        [Fact]
        public async Task Events_InvalidSince_UsesDefault24h_ExcludesVeryOld()
        {
            using var db = MakeDb(nameof(Events_InvalidSince_UsesDefault24h_ExcludesVeryOld));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.GetEventsSince("not-a-date", CancellationToken.None) as OkObjectResult;
            Assert.NotNull(res);

            var payload = res!.Value!;
            var items = GetProp<IEnumerable<AuditEventDto>>(payload, "items").ToList();

            Assert.True(items.All(i => i.OccurredAtUtc > DateTime.UtcNow.AddDays(-1)));
            Assert.DoesNotContain(items, i => i.Type == "Delete" && i.Details == "very-old");
        }

        /// <summary>
        /// Verifies take clamping logic: values below 1 clamp to 1; values above 200 clamp to 200.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when page counts reflect clamped take values.
        /// </returns>
        [Fact]
        public async Task Events_TakeClamp_MinAndMax_Branches()
        {
            using var db = MakeDb(nameof(Events_TakeClamp_MinAndMax_Branches));
            var ctrl = MakeController(MakeQuery(db));

            // take=-10 -> clamp to 1
            ctrl.ControllerContext.HttpContext.Request.QueryString = new QueryString("?take=-10");
            var res1 = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None) as OkObjectResult;
            var page1 = GetProp<IEnumerable<AuditEventDto>>(res1!.Value!, "items").ToList();
            Assert.Equal(1, page1.Count);

            // take=999 -> clamp to 200 (dataset smaller)
            ctrl.ControllerContext.HttpContext = new DefaultHttpContext();
            ctrl.ControllerContext.HttpContext.Request.QueryString = new QueryString("?take=999");
            var res2 = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None) as OkObjectResult;
            var page2 = GetProp<IEnumerable<AuditEventDto>>(res2!.Value!, "items").ToList();
            Assert.True(page2.Count >= 2);
        }

        /// <summary>
        /// Ensures projection + deduplication works as intended and covers generic target labels.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when only the newest of duplicated ExternalId entries is present and generic targets are covered.
        /// </returns>
        [Fact]
        public async Task Events_Projects_Dedupes_Newest_Wins_And_GenericTargetsCovered()
        {
            using var db = MakeDb(nameof(Events_Projects_Dedupes_Newest_Wins_And_GenericTargetsCovered));
            var ctrl = MakeController(MakeQuery(db));

            var res = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None) as OkObjectResult;
            var page = GetProp<IEnumerable<AuditEventDto>>(res!.Value!, "items").ToList();

            // Dedupe: only one "Update" (newest wins)
            Assert.Single(page.Where(i => i.Type == "Update"));
            Assert.Contains(page, i => i.Details == "newer-update");
            Assert.DoesNotContain(page, i => i.Details == "older-update");

            // Generic target branches were included by seed:
            Assert.Contains(page, i => i.Details == "hw-generic" && i.Target == "Hardware");
            Assert.Contains(page, i => i.Details == "sw-generic" && i.Target == "Software");
        }

        /// <summary>
        /// Covers the remaining projection branch:
        /// ExternalId == Guid.Empty (ID fallback) and SoftwareID.HasValue == true (target label).
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when the projected event shows <c>Id == AuditLogID</c> and <c>Target == "Software#{id}"</c>.
        /// </returns>
        [Fact]
        public async Task Events_Projects_Covers_Software_WithId_And_EmptyExternalId()
        {
            var opts = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(nameof(Events_Projects_Covers_Software_WithId_And_EmptyExternalId))
                .EnableSensitiveDataLogging()
                .Options;
            using var db = new AimsDbContext(opts);
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            db.Roles.Add(new Role { RoleID = 1, RoleName = "Admin" });
            db.Users.Add(new User { UserID = 1, FullName = "Alice Admin", Email = "alice@example.com", RoleID = 1 });
            db.SoftwareAssets.Add(new Software { SoftwareID = 222, SoftwareName = "FooApp" });

            db.AuditLogs.Add(new AuditLog
            {
                AuditLogID = 50,
                ExternalId = Guid.Empty,                    // forces Id fallback to AuditLogID
                TimestampUtc = DateTime.UtcNow.AddMinutes(-1),
                UserID = 1,
                Action = "SoftIdNoExt",
                Description = "combo",
                AssetKind = AssetKind.Software,
                SoftwareID = 222
            });
            db.SaveChanges();

            var ctrl = MakeController(MakeQuery(db));
            var ok = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None) as OkObjectResult;
            var page = GetProp<IEnumerable<AuditEventDto>>(ok!.Value!, "items").ToList();

            var ev = Assert.Single(page.Where(i => i.Type == "SoftIdNoExt"));
            Assert.Equal("Software#222", ev.Target);
            Assert.Equal("50", ev.Id);
        }

        /// <summary>
        /// Ensures an empty page produces a distinct ETag and the <c>nextSince</c> value is equal to the requested <c>since</c>.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when ETags differ across calls and <c>nextSince</c> (as an instant) equals the requested instant.
        /// </returns>
        [Fact]
        public async Task Events_EmptyPage_Produces_DistinctEtag_And_NextSinceEqualsSince()
        {
            using var db = MakeDb(nameof(Events_EmptyPage_Produces_DistinctEtag_And_NextSinceEqualsSince));
            var ctrl = MakeController(MakeQuery(db));

            // First: non-empty
            var ok1 = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None) as OkObjectResult;
            var etag1 = ctrl.HttpContext.Response.Headers.ETag.ToString();
            Assert.False(string.IsNullOrWhiteSpace(etag1));

            // Second: far future => empty page
            ctrl.ControllerContext.HttpContext = new DefaultHttpContext();
            var since = "2100-01-01T00:00:00Z";
            var ok2 = await ctrl.GetEventsSince(since, CancellationToken.None) as OkObjectResult;
            var etag2 = ctrl.HttpContext.Response.Headers.ETag.ToString();
            Assert.False(string.IsNullOrWhiteSpace(etag2));
            Assert.NotEqual(etag1, etag2);

            var payload = ok2!.Value!;
            var items = GetProp<IEnumerable<AuditEventDto>>(payload, "items");
            var nextSince = GetProp<string>(payload, "nextSince");

            Assert.Empty(items);

            // Compare as instants to ignore fractional seconds formatting differences
            var expected = DateTimeOffset.Parse(since).UtcDateTime;
            var actual = DateTimeOffset.Parse(nextSince).UtcDateTime;
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Validates 304 (Not Modified) is returned when the request includes a matching ETag.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous test execution. The task completes successfully
        /// when <see cref="StatusCodes.Status304NotModified"/> is returned.
        /// </returns>
        [Fact]
        public async Task Events_IfNoneMatch_Returns_304()
        {
            using var db = MakeDb(nameof(Events_IfNoneMatch_Returns_304));
            var ctrl = MakeController(MakeQuery(db));

            // Prime ETag
            var first = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None) as OkObjectResult;
            var etag = ctrl.HttpContext.Response.Headers.ETag.ToString();
            Assert.False(string.IsNullOrWhiteSpace(etag));

            // Request with same ETag -> 304
            var http = new DefaultHttpContext();
            http.Request.Headers["If-None-Match"] = etag;
            ctrl.ControllerContext.HttpContext = http;

            var second = await ctrl.GetEventsSince(DateTime.UtcNow.AddHours(-3).ToString("O"), CancellationToken.None);
            var status = Assert.IsType<StatusCodeResult>(second);
            Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        }

        /// <summary>
        /// Covers the branch inside the projection where <c>Description</c> is null,
        /// which must be mapped to an empty string in <c>Details</c>.
        /// </summary>
        /// <remarks>Invokes the private static helper via reflection to keep the test focused.</remarks>
        /// <returns>
        /// Nothing. This test is synchronous and will complete after assertions pass.
        /// </returns>
        [Fact]
        public void Project_Select_Maps_NullDescription_To_EmptyString_Branch()
        {
            // Arrange: make a DTO that is within the since window, with Description = null
            var since = DateTime.UtcNow.AddHours(-3);
            var dto = new GetAuditRecordDto
            {
                AuditLogID = 999,
                ExternalId = Guid.Empty,                 // also exercises Id-fallback path
                TimestampUtc = DateTime.UtcNow.AddMinutes(-1),
                UserID = 42,
                UserName = "Unit Tester",
                Action = "NullDesc",
                Description = null,                       // <-- forces the ?? "" branch
                AssetKind = AssetKind.Hardware,
                HardwareID = null,
                SoftwareID = null
            };

            var events = new List<GetAuditRecordDto> { dto };

            // Act: invoke private static ProjectDedupAndPage(...) via reflection
            var mi = typeof(AIMS.Controllers.Api.AuditLogApiController)
                .GetMethod("ProjectDedupAndPage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mi);

            var page = (List<AuditEventDto>)mi!.Invoke(null, new object[] { events, since, 50 })!;

            // Assert: single projected item, Details == "" (from ?? ""), Id fell back to AuditLogID, generic "Hardware"
            var ev = Assert.Single(page);
            Assert.Equal("NullDesc", ev.Type);
            Assert.Equal(string.Empty, ev.Details);
            Assert.Equal("999", ev.Id);
            Assert.Equal("Hardware", ev.Target);
        }
    }
}
