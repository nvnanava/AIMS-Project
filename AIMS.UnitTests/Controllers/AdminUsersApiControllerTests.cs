using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Contracts;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using AIMS.UnitTests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.Tests.Api
{
    public sealed class AdminUsersApiControllerTests : IDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly DbContextOptions<AimsDbContext> _options;

        public AdminUsersApiControllerTests()
        {
            // Shared in-memory SQLite connection for relational behavior
            _conn = new SqliteConnection("Filename=:memory:");
            _conn.Open();

            _options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseSqlite(_conn)
                .Options;

            // Create schema
            using var db = new TestAimsDbContext(_options);
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }

        private static AdminUsersApiController MakeController(AIMS.Data.AimsDbContext db)
        {
            return new AdminUsersApiController(
                svc: new FakeUpsertService(), // not used in these tests
                db: db,
                officeQuery: new OfficeQuery(db)
            );
        }

        private static IAuditEventBroadcaster NoopBroadcaster() => new NoopAuditBroadcaster();

        private async Task<int> SeedActiveUserAsync()
        {
            using var db = new TestAimsDbContext(_options);

            // Minimal required related data
            var role = new Role { RoleName = "User", Description = "User role" };
            db.Roles.Add(role);

            var office = new Office { OfficeName = "HQ – Sacramento", Location = "Sacramento" };
            db.Offices.Add(office);
            await db.SaveChangesAsync();

            var user = new User
            {
                FullName = "Test Person",
                Email = "test.person@example.com",
                EmployeeNumber = "E123",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                RoleID = role.RoleID,
                OfficeID = office.OfficeID,
                IsArchived = false,
                ArchivedAtUtc = null
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.UserID;
        }

        [Fact]
        public async Task Archive_SetsFlags_And_Writes_AuditLog()
        {
            var userId = await SeedActiveUserAsync();

            await using (var db = new TestAimsDbContext(_options))
            {
                var sut = MakeController(db);

                var result = await sut.Archive(userId, NoopBroadcaster(), CancellationToken.None);
                Assert.IsType<NoContentResult>(result);
            }

            await using (var verifyDb = new TestAimsDbContext(_options))
            {
                var u = await verifyDb.Users.IgnoreQueryFilters().FirstAsync(x => x.UserID == userId);
                Assert.True(u.IsArchived);
                Assert.NotNull(u.ArchivedAtUtc);

                var log = await verifyDb.AuditLogs.OrderByDescending(a => a.AuditLogID).FirstOrDefaultAsync();
                Assert.NotNull(log);
                // If Action is a string column:
                Assert.Equal("Archive User", log!.Action);
                // If Action is an enum-backed int in your DB, use:
                // Assert.Equal((int)AuditAction.ArchiveUser, log!.Action);

                Assert.Equal(AssetKind.User, log.AssetKind);
                Assert.Equal(userId, log.UserID);
                Assert.Null(log.HardwareID);
                Assert.Null(log.SoftwareID);
                Assert.True(log.TimestampUtc <= DateTime.UtcNow.AddSeconds(2));
            }
        }

        [Fact]
        public async Task Unarchive_ClearsFlags_And_Writes_AuditLog()
        {
            var userId = await SeedActiveUserAsync();

            // First archive, then unarchive
            await using (var db = new TestAimsDbContext(_options))
            {
                var sut = MakeController(db);
                await sut.Archive(userId, NoopBroadcaster(), CancellationToken.None);
            }

            await using (var db = new TestAimsDbContext(_options))
            {
                var sut = MakeController(db);
                var result = await sut.Unarchive(userId, NoopBroadcaster(), CancellationToken.None);
                Assert.IsType<NoContentResult>(result);
            }

            await using (var verifyDb = new TestAimsDbContext(_options))
            {
                var u = await verifyDb.Users.IgnoreQueryFilters().FirstAsync(x => x.UserID == userId);
                Assert.False(u.IsArchived);
                Assert.Null(u.ArchivedAtUtc);

                var log = await verifyDb.AuditLogs
                    .OrderByDescending(a => a.AuditLogID)
                    .FirstOrDefaultAsync();
                Assert.NotNull(log);
                // If Action is a string column:
                Assert.Equal("Unarchive User", log!.Action);
                // Else enum-backed:
                // Assert.Equal((int)AuditAction.UnarchiveUser, log!.Action);

                Assert.Equal(AssetKind.User, log.AssetKind);
                Assert.Equal(userId, log.UserID);
            }
        }

        [Fact]
        public async Task List_Respects_IncludeArchived_Flag()
        {
            var userId = await SeedActiveUserAsync();

            // Archive the user so it's excluded by default filter
            await using (var db = new TestAimsDbContext(_options))
            {
                var sut = MakeController(db);
                await sut.Archive(userId, NoopBroadcaster(), CancellationToken.None);
            }

            // includeArchived = false → should NOT include the user
            await using (var db = new TestAimsDbContext(_options))
            {
                var sut = MakeController(db);
                var resp = await sut.List(includeArchived: false, CancellationToken.None);
                var ok = Assert.IsType<OkObjectResult>(resp);
                var json = JsonSerializer.Serialize(ok.Value);
                Assert.DoesNotContain($"\"userID\":{userId}", json);
            }

            // includeArchived = true → should include the user
            await using (var db = new TestAimsDbContext(_options))
            {
                var sut = MakeController(db);
                var resp = await sut.List(includeArchived: true, CancellationToken.None);
                var ok = Assert.IsType<OkObjectResult>(resp);

                // Deserialize to check the flags
                var arr = JsonSerializer.Deserialize<List<UserRow>>(JsonSerializer.Serialize(ok.Value))!;
                Assert.Contains(arr, r => r.userID == userId && r.isArchived);
            }
        }

        // Minimal DTO to read List() result shape
        private record UserRow(
            int userID,
            string name,
            string email,
            string officeName,
            bool isArchived,
            DateTime? archivedAtUtc
        );

        // --- test doubles -------------------------------------------------

        private sealed class FakeUpsertService : IAdminUserUpsertService
        {
            // Match your actual signature; this method is not called in these tests.
            public Task<User> UpsertAdminUserAsync(string graphObjectId, int? roleId, int? supervisorId, int? officeId, CancellationToken ct)
                => throw new NotImplementedException();
        }

        private sealed class NoopAuditBroadcaster : IAuditEventBroadcaster
        {
            public Task BroadcastAsync(AuditEventDto dto) => Task.CompletedTask;
        }
    }
}
