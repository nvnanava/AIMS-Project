using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace AIMS.UnitTests.Services
{
    public class SoftwareSeatServiceTests
    {
        // --- Test doubles ----------------------------------------------------

        private sealed class ThrowOnceConcurrencyInterceptor : SaveChangesInterceptor
        {
            private int _throwsRemaining;
            public ThrowOnceConcurrencyInterceptor(int throws) => _throwsRemaining = throws;

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                if (_throwsRemaining > 0)
                {
                    _throwsRemaining--;
                    throw new DbUpdateConcurrencyException("Simulated concurrency conflict (interceptor).");
                }
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        /// <summary>Always throw concurrency to exhaust retry loop.</summary>
        private sealed class ThrowAlwaysConcurrencyInterceptor : SaveChangesInterceptor
        {
            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
                => throw new DbUpdateConcurrencyException("Simulated hard conflict (always).");
        }

        private sealed class StubBroadcaster : IAuditEventBroadcaster
        {
            public int BroadcastCount { get; private set; }
            public Task BroadcastAsync(AIMS.Contracts.AuditEventDto evt)
            {
                BroadcastCount++;
                return Task.CompletedTask;
            }
        }

        // --- Helpers ---------------------------------------------------------

        private static DbContextOptions<AimsDbContext> NewDbOptions(string name, params IInterceptor[] interceptors)
        {
            var b = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(name)
                .EnableSensitiveDataLogging();

            if (interceptors is { Length: > 0 }) b.AddInterceptors(interceptors);
            return b.Options;
        }

        private static async Task<(AimsDbContext Ctx, int SoftwareId, int UserId)> SeedAsync(
            string dbName,
            int totalSeats = 3,
            int usedSeats = 0,
            bool archived = false,
            params IInterceptor[] interceptors)
        {
            var ctx = new AimsDbContext(NewDbOptions(dbName, interceptors));
            await ctx.Database.EnsureCreatedAsync();

            var user = new User
            {
                FullName = "Test User",
                Email = "user@example.com",
                EmployeeNumber = "U-001",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsActive = true,
                IsArchived = false,
                RoleID = 0
            };

            var sw = new Software
            {
                SoftwareName = "Photoshop",
                SoftwareType = "Design",
                SoftwareVersion = "2025.1",
                SoftwareLicenseKey = "ABC-DEF-123",
                SoftwareLicenseExpiration = null,
                SoftwareUsageData = 0,
                SoftwareCost = 10m,
                LicenseTotalSeats = totalSeats,
                LicenseSeatsUsed = usedSeats,
                Comment = "seed",
                IsArchived = archived
            };

            ctx.Users.Add(user);
            ctx.SoftwareAssets.Add(sw);
            await ctx.SaveChangesAsync();

            return (ctx, sw.SoftwareID, user.UserID);
        }

        /// <summary>Overload that customizes user name/employee number for ternary branches.</summary>
        private static async Task<(AimsDbContext Ctx, int SoftwareId, int UserId)> SeedAsync(
            string dbName,
            int totalSeats,
            int usedSeats,
            bool archived,
            string userFullName,
            string employeeNumber,
            params IInterceptor[] interceptors)
        {
            var ctx = new AimsDbContext(NewDbOptions(dbName, interceptors));
            await ctx.Database.EnsureCreatedAsync();

            var user = new User
            {
                FullName = userFullName,
                Email = "user@example.com",
                EmployeeNumber = employeeNumber,
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsActive = true,
                IsArchived = false,
                RoleID = 0
            };

            var sw = new Software
            {
                SoftwareName = "Photoshop",
                SoftwareType = "Design",
                SoftwareVersion = "2025.1",
                SoftwareLicenseKey = "ABC-DEF-123",
                SoftwareLicenseExpiration = null,
                SoftwareUsageData = 0,
                SoftwareCost = 10m,
                LicenseTotalSeats = totalSeats,
                LicenseSeatsUsed = usedSeats,
                Comment = "seed",
                IsArchived = archived
            };

            ctx.Users.Add(user);
            ctx.SoftwareAssets.Add(sw);
            await ctx.SaveChangesAsync();

            return (ctx, sw.SoftwareID, user.UserID);
        }

        // Extra seeders to cover not-found branches
        private static async Task<(AimsDbContext Ctx, int SoftwareId)> SeedSoftwareOnlyAsync(
            string dbName,
            int totalSeats = 3,
            int usedSeats = 0,
            bool archived = false,
            params IInterceptor[] interceptors)
        {
            var ctx = new AimsDbContext(NewDbOptions(dbName, interceptors));
            await ctx.Database.EnsureCreatedAsync();

            var sw = new Software
            {
                SoftwareName = "SoloSoft",
                SoftwareType = "Tooling",
                SoftwareVersion = "1.0",
                SoftwareLicenseKey = Guid.NewGuid().ToString("N"),
                SoftwareLicenseExpiration = null,
                SoftwareUsageData = 0,
                SoftwareCost = 0m,
                LicenseTotalSeats = totalSeats,
                LicenseSeatsUsed = usedSeats,
                Comment = "seed",
                IsArchived = archived
            };
            ctx.SoftwareAssets.Add(sw);
            await ctx.SaveChangesAsync();

            return (ctx, sw.SoftwareID);
        }

        private static async Task<(AimsDbContext Ctx, int UserId)> SeedUserOnlyAsync(
            string dbName,
            string? employeeNumber = "U-NA",
            params IInterceptor[] interceptors)
        {
            var ctx = new AimsDbContext(NewDbOptions(dbName, interceptors));
            await ctx.Database.EnsureCreatedAsync();

            var user = new User
            {
                FullName = "Only User",
                Email = "only@user",
                EmployeeNumber = employeeNumber ?? "",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsActive = true,
                IsArchived = false,
                RoleID = 0
            };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();

            return (ctx, user.UserID);
        }

        private static SoftwareSeatService NewService(AimsDbContext ctx, out StubBroadcaster stubBroadcaster)
        {
            stubBroadcaster = new StubBroadcaster();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var audit = new AuditLogQuery(ctx, stubBroadcaster, cache);
            return new SoftwareSeatService(ctx, audit);
        }

        private static async Task<(int used, int total)> ReadSeatCounts(AimsDbContext ctx, int softwareId)
        {
            var row = await ctx.SoftwareAssets
                .Where(s => s.SoftwareID == softwareId)
                .Select(s => new { s.LicenseSeatsUsed, s.LicenseTotalSeats })
                .SingleAsync();
            return (row.LicenseSeatsUsed, row.LicenseTotalSeats);
        }

        private static string? LastAuditDescription(AimsDbContext ctx)
            => ctx.AuditLogs
                .OrderByDescending(a => a.AuditLogID)
                .Select(a => a.Description)
                .FirstOrDefault();

        // --- Assign (existing) -------------------------------------------------

        [Fact]
        public async Task AssignSeat_Succeeds_Increments_And_WritesAudit()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            var svc = NewService(ctx, out var bc);

            await svc.AssignSeatAsync(swId, userId);

            var (used, total) = await ReadSeatCounts(ctx, swId);
            Assert.Equal(1, used);
            Assert.Equal(2, total);

            var audit = await ctx.AuditLogs
                .Where(a => a.SoftwareID == swId && a.Action == "Assign")
                .SingleOrDefaultAsync();
            Assert.NotNull(audit);

            var change = await ctx.AuditLogChanges
                .Where(c => c.AuditLogID == audit!.AuditLogID && c.Field == "Seats")
                .SingleOrDefaultAsync();

            Assert.Equal("0/2", change!.OldValue);
            Assert.Equal("1/2", change.NewValue);
            Assert.True(bc.BroadcastCount >= 1);
        }

        [Fact]
        public async Task AssignSeat_Idempotent_When_UserAlreadyAssigned_NoDuplicate_NoExtraIncrement()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 5, usedSeats: 0);
            var svc = NewService(ctx, out _);

            await svc.AssignSeatAsync(swId, userId);
            await svc.AssignSeatAsync(swId, userId); // no-op

            var openAssignments = await ctx.Assignments
                .CountAsync(a => a.SoftwareID == swId && a.UserID == userId && a.UnassignedAtUtc == null);
            var (used, _) = await ReadSeatCounts(ctx, swId);

            Assert.Equal(1, openAssignments);
            Assert.Equal(1, used);
        }

        [Fact]
        public async Task AssignSeat_Throws_When_LicenseUsed_Equals_Total()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 1, usedSeats: 1);
            var svc = NewService(ctx, out _);

            await Assert.ThrowsAsync<SeatCapacityException>(() => svc.AssignSeatAsync(swId, userId));
        }

        [Fact]
        public async Task AssignSeat_Throws_When_OpenAssignments_Reaches_Total()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, user1) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            var svc = NewService(ctx, out _);

            // Seed two open assignments to hit capacity via openCount guard
            var u2 = new User
            {
                FullName = "U2",
                Email = "u2@x",
                EmployeeNumber = "U-002",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsActive = true,
                RoleID = 0
            };
            var u3 = new User
            {
                FullName = "U3",
                Email = "u3@x",
                EmployeeNumber = "U-003",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsActive = true,
                RoleID = 0
            };
            ctx.Users.AddRange(u2, u3);
            await ctx.SaveChangesAsync();

            ctx.Assignments.AddRange(
                new Assignment { AssetKind = AssetKind.Software, SoftwareID = swId, UserID = u2.UserID, AssignedAtUtc = DateTime.UtcNow },
                new Assignment { AssetKind = AssetKind.Software, SoftwareID = swId, UserID = u3.UserID, AssignedAtUtc = DateTime.UtcNow }
            );
            await ctx.SaveChangesAsync();

            await Assert.ThrowsAsync<SeatCapacityException>(() => svc.AssignSeatAsync(swId, user1));
        }

        [Fact]
        public async Task AssignSeat_Throws_When_Software_Archived()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0, archived: true);
            var svc = NewService(ctx, out _);

            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AssignSeatAsync(swId, userId));
        }

        [Fact]
        public async Task AssignSeat_Retries_On_Concurrency_And_Succeeds()
        {
            var dbName = Guid.NewGuid().ToString("N");

            // Seed WITHOUT interceptor
            var (seedCtx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            await seedCtx.DisposeAsync();

            // New context WITH interceptor, same DB name
            var interceptor = new ThrowOnceConcurrencyInterceptor(1);
            var ctx = new AimsDbContext(NewDbOptions(dbName, interceptor));
            var svc = NewService(ctx, out var bc);

            await svc.AssignSeatAsync(swId, userId); // should succeed after retry

            var (used, total) = await ReadSeatCounts(ctx, swId);
            Assert.Equal(1, used);
            Assert.Equal(2, total);

            var audit = await ctx.AuditLogs
                .Where(a => a.SoftwareID == swId && a.Action == "Assign")
                .SingleOrDefaultAsync();
            Assert.NotNull(audit);
            Assert.True(bc.BroadcastCount >= 1);
        }

        // --- Assign (extra branch coverage) -----------------------------------

        [Fact]
        public async Task AssignSeat_Uses_EmployeeNumber_When_Present()
        {
            var dbName = Guid.NewGuid().ToString("N");

            var (seedCtx, swId, userId) = await SeedAsync(
                dbName, totalSeats: 2, usedSeats: 0,
                archived: false,
                userFullName: "Ada Lovelace",
                employeeNumber: "12345"
            );
            await seedCtx.DisposeAsync();

            using var ctx = new AimsDbContext(NewDbOptions(dbName));
            var svc = NewService(ctx, out _);
            await svc.AssignSeatAsync(swId, userId);

            var desc = LastAuditDescription(ctx);
            Assert.Contains("Ada Lovelace (Emp #12345)", desc);
        }

        [Fact]
        public async Task AssignSeat_Uses_NA_When_EmployeeNumber_Blank()
        {
            var dbName = Guid.NewGuid().ToString("N");

            var (seedCtx, swId, userId) = await SeedAsync(
                dbName, totalSeats: 1, usedSeats: 0,
                archived: false,
                userFullName: "Grace Hopper",
                employeeNumber: "   " // whitespace → N/A
            );
            await seedCtx.DisposeAsync();

            using var ctx = new AimsDbContext(NewDbOptions(dbName));
            var svc = NewService(ctx, out _);
            await svc.AssignSeatAsync(swId, userId);

            var desc = LastAuditDescription(ctx);
            Assert.Contains("Grace Hopper (Emp #N/A)", desc);
        }

        [Fact]
        public async Task AssignSeat_Exhausts_Retries_And_Throws_Final_ConcurrencyException()
        {
            var dbName = Guid.NewGuid().ToString("N");

            // Seed without interceptor
            var (seedCtx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            await seedCtx.DisposeAsync();

            // Always-throw interceptor to exhaust loop
            using var ctx = new AimsDbContext(NewDbOptions(dbName, new ThrowAlwaysConcurrencyInterceptor()));
            var svc = NewService(ctx, out _);

            var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => svc.AssignSeatAsync(swId, userId));
            Assert.True(
                ex.Message.Contains("Failed to assign seat after retries.") ||
                ex.Message.Contains("Simulated hard conflict"),
                $"Unexpected message: {ex.Message}");
        }

        [Fact]
        public async Task AssignSeat_Throws_When_User_NotFound()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId) = await SeedSoftwareOnlyAsync(dbName); // no users at all
            var svc = NewService(ctx, out _);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.AssignSeatAsync(swId, userId: 999_999));
        }

        [Fact]
        public async Task AssignSeat_Throws_When_Software_NotFound()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, userId) = await SeedUserOnlyAsync(dbName);
            var svc = NewService(ctx, out _);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.AssignSeatAsync(softwareId: 999_999, userId));
        }

        // --- Release (existing) ------------------------------------------------

        [Fact]
        public async Task ReleaseSeat_Succeeds_Decrements_And_WritesAudit()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            var svc = NewService(ctx, out var bc);

            await svc.AssignSeatAsync(swId, userId);
            await svc.ReleaseSeatAsync(swId, userId);

            var (used, total) = await ReadSeatCounts(ctx, swId);
            Assert.Equal(0, used);
            Assert.Equal(2, total);

            var audit = await ctx.AuditLogs
                .Where(a => a.SoftwareID == swId && a.Action == "Unassign")
                .SingleOrDefaultAsync();
            Assert.NotNull(audit);

            var change = await ctx.AuditLogChanges
                .Where(c => c.AuditLogID == audit!.AuditLogID && c.Field == "Seats")
                .SingleOrDefaultAsync();

            Assert.Equal("1/2", change!.OldValue);
            Assert.Equal("0/2", change.NewValue);
            Assert.True(bc.BroadcastCount >= 2); // assign + unassign
        }

        [Fact]
        public async Task ReleaseSeat_Idempotent_When_NoOpenAssignment_NoThrow_NoDecrement()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            var svc = NewService(ctx, out var bc);

            await svc.ReleaseSeatAsync(swId, userId); // no-op

            var (used, total) = await ReadSeatCounts(ctx, swId);
            Assert.Equal(0, used);
            Assert.Equal(2, total);

            var unassignCount = await ctx.AuditLogs
                .CountAsync(a => a.SoftwareID == swId && a.Action == "Unassign");
            Assert.Equal(0, unassignCount);
            Assert.True(bc.BroadcastCount == 0);
        }

        // --- Release (extra branch coverage) ----------------------------------

        [Fact]
        public async Task ReleaseSeat_Retries_On_Concurrency_And_Succeeds()
        {
            var dbName = Guid.NewGuid().ToString("N");

            // Seed with an OPEN assignment for the user
            var (seedCtx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 1);
            seedCtx.Assignments.Add(new Assignment
            {
                AssetKind = AssetKind.Software,
                SoftwareID = swId,
                UserID = userId,
                AssignedAtUtc = DateTime.UtcNow,
                UnassignedAtUtc = null
            });
            await seedCtx.SaveChangesAsync();
            await seedCtx.DisposeAsync();

            using var ctx = new AimsDbContext(NewDbOptions(dbName, new ThrowOnceConcurrencyInterceptor(1)));
            var svc = NewService(ctx, out var bc);

            await svc.ReleaseSeatAsync(swId, userId); // should succeed after retry

            var sw = await ctx.SoftwareAssets.SingleAsync(s => s.SoftwareID == swId);
            Assert.Equal(0, sw.LicenseSeatsUsed);
            Assert.Contains("Released seat for", LastAuditDescription(ctx));
            Assert.True(bc.BroadcastCount >= 1);
        }

        [Fact]
        public async Task ReleaseSeat_Exhausts_Retries_And_Throws_Final_ConcurrencyException()
        {
            var dbName = Guid.NewGuid().ToString("N");

            // Seed + open assignment so release attempts real writes
            var (seedCtx, swId, userId) = await SeedAsync(dbName, totalSeats: 1, usedSeats: 1);
            seedCtx.Assignments.Add(new Assignment
            {
                AssetKind = AssetKind.Software,
                SoftwareID = swId,
                UserID = userId,
                AssignedAtUtc = DateTime.UtcNow
            });
            await seedCtx.SaveChangesAsync();
            await seedCtx.DisposeAsync();

            using var ctx = new AimsDbContext(NewDbOptions(dbName, new ThrowAlwaysConcurrencyInterceptor()));
            var svc = NewService(ctx, out _);

            var ex2 = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => svc.ReleaseSeatAsync(swId, userId));
            Assert.True(
                ex2.Message.Contains("Failed to release seat after retries.") ||
                ex2.Message.Contains("Simulated hard conflict"),
                $"Unexpected message: {ex2.Message}");
        }

        [Fact]
        public async Task ReleaseSeat_Uses_NA_When_EmployeeNumber_Blank()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (seedCtx, swId, userId) = await SeedAsync(
                dbName, totalSeats: 2, usedSeats: 0,
                archived: false,
                userFullName: "No Emp",
                employeeNumber: ""
            );
            await seedCtx.DisposeAsync();

            using var ctx = new AimsDbContext(NewDbOptions(dbName));
            var svc = NewService(ctx, out var bc);

            await svc.AssignSeatAsync(swId, userId);
            await svc.ReleaseSeatAsync(swId, userId);

            var desc = LastAuditDescription(ctx);
            Assert.Contains("No Emp (Emp #N/A)", desc);
            Assert.True(bc.BroadcastCount >= 2);
        }

        [Fact]
        public async Task ReleaseSeat_Throws_When_User_NotFound()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId) = await SeedSoftwareOnlyAsync(dbName);
            var svc = NewService(ctx, out _);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ReleaseSeatAsync(swId, userId: 999_999));
        }

        [Fact]
        public async Task ReleaseSeat_Throws_When_Software_NotFound()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, userId) = await SeedUserOnlyAsync(dbName);
            var svc = NewService(ctx, out _);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ReleaseSeatAsync(softwareId: 999_999, userId));
        }

#if DEBUG
        [Fact]
        public async Task AssignSeat_FinalThrow_Reached_When_MaxRetries_Zero()
        {
            var dbName = Guid.NewGuid().ToString("N");
            // Normal seed; the loop will be skipped by the override, so capacity etc. is irrelevant
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 1, usedSeats: 1);
            var svc = NewService(ctx, out _);

            var saved = SoftwareSeatService.RetryOverride;
            SoftwareSeatService.RetryOverride = 0; // force: for-loop does not execute

            try
            {
                var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => svc.AssignSeatAsync(swId, userId));
                Assert.Contains("Failed to assign seat after retries.", ex.Message);
            }
            finally
            {
                SoftwareSeatService.RetryOverride = saved;
            }
        }

        [Fact]
        public async Task ReleaseSeat_FinalThrow_Reached_When_MaxRetries_Zero()
        {
            var dbName = Guid.NewGuid().ToString("N");

            // Seed with an open assignment so that (if loop ran) it would try to write;
            // but we skip the loop entirely via RetryOverride = 0 to hit the final throw line.
            var (seedCtx, swId, userId) = await SeedAsync(dbName, totalSeats: 1, usedSeats: 1);
            seedCtx.Assignments.Add(new Assignment
            {
                AssetKind = AssetKind.Software,
                SoftwareID = swId,
                UserID = userId,
                AssignedAtUtc = DateTime.UtcNow,
                UnassignedAtUtc = null
            });
            await seedCtx.SaveChangesAsync();
            await seedCtx.DisposeAsync();

            using var ctx = new AimsDbContext(NewDbOptions(dbName));
            var svc = NewService(ctx, out _);

            var saved = SoftwareSeatService.RetryOverride;
            SoftwareSeatService.RetryOverride = 0;

            try
            {
                var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => svc.ReleaseSeatAsync(swId, userId));
                Assert.Contains("Failed to release seat after retries.", ex.Message);
            }
            finally
            {
                SoftwareSeatService.RetryOverride = saved;
            }
        }
#endif
    }
}
