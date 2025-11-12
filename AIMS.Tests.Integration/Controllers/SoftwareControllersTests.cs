using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Controllers.Api;
using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Dtos.Software;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace AIMS.Tests.Integration.Controllers
{
    public class SoftwareControllerTests
    {
        // ---------- Minimal doubles for AuditLogQuery deps ----------
        private sealed class StubBroadcaster : IAuditEventBroadcaster
        {
            public int Broadcasts { get; private set; }
            public Task BroadcastAsync(AIMS.Contracts.AuditEventDto evt)
            {
                Broadcasts++;
                return Task.CompletedTask;
            }
        }

        /// <summary>Always throws concurrency to exhaust retry loops.</summary>
        private sealed class ThrowAlwaysConcurrencyInterceptor : SaveChangesInterceptor
        {
            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
                => throw new DbUpdateConcurrencyException("Simulated hard conflict (always).");
        }

        // ---------- EF helpers ----------
        private static DbContextOptions<AimsDbContext> NewDbOptions(string name, params IInterceptor[] interceptors)
        {
            var b = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(name)
                .EnableSensitiveDataLogging();

            if (interceptors is { Length: > 0 }) b.AddInterceptors(interceptors);
            return b.Options;
        }

        private static async Task<(AimsDbContext Ctx, int SoftwareId, int UserId)> SeedAsync(
            string dbName, int totalSeats, int usedSeats, bool archived = false)
        {
            var ctx = new AimsDbContext(NewDbOptions(dbName));
            await ctx.Database.EnsureCreatedAsync();

            var user = new User
            {
                FullName = "Test User",
                Email = "user@example.com",
                EmployeeNumber = "U-001",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsArchived = false,
                RoleID = 0
            };

            var sw = new Software
            {
                SoftwareName = "Photoshop",
                SoftwareType = "Design",
                SoftwareVersion = "2025.1",
                SoftwareLicenseKey = "ABC-DEF-123",
                LicenseTotalSeats = totalSeats,
                LicenseSeatsUsed = usedSeats,
                SoftwareCost = 10m,
                Comment = "seed",
                IsArchived = archived
            };

            ctx.Users.Add(user);
            ctx.SoftwareAssets.Add(sw);
            await ctx.SaveChangesAsync();
            return (ctx, sw.SoftwareID, user.UserID);
        }

        private static SoftwareController NewController(AimsDbContext ctx)
            => new SoftwareController(ctx, new SoftwareQuery(ctx));

        private static SoftwareSeatService NewService(AimsDbContext ctx, out StubBroadcaster bc)
        {
            bc = new StubBroadcaster();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var audit = new AuditLogQuery(ctx, bc, cache);
            return new SoftwareSeatService(ctx, audit);
        }

        private static (int used, int total) ReadCounts(AimsDbContext ctx, int softwareId)
        {
            var row = ctx.SoftwareAssets
                .Where(s => s.SoftwareID == softwareId)
                .Select(s => new { s.LicenseSeatsUsed, s.LicenseTotalSeats })
                .Single();
            return (row.LicenseSeatsUsed, row.LicenseTotalSeats);
        }

        // ---------- Tests ----------

        [Fact]
        public async Task Assign_Returns201_And_IncrementsCounts_Then_IdempotentStill201()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            var controller = NewController(ctx);
            var svc = NewService(ctx, out _);

            // First assign
            var result1 = await controller.AssignSeat(
                new AssignSeatRequestDto { SoftwareID = swId, UserID = userId }, svc);

            var created1 = Assert.IsType<CreatedResult>(result1);
            Assert.Equal(201, created1.StatusCode);

            // Inspect anonymous payload via reflection
            var body1 = created1.Value!;
            int used1 = (int)body1.GetType().GetProperty("LicenseSeatsUsed")!.GetValue(body1)!;
            int total1 = (int)body1.GetType().GetProperty("LicenseTotalSeats")!.GetValue(body1)!;
            Assert.Equal(1, used1);
            Assert.Equal(2, total1);

            // Second assign (idempotent)
            var result2 = await controller.AssignSeat(
                new AssignSeatRequestDto { SoftwareID = swId, UserID = userId }, svc);

            var created2 = Assert.IsType<CreatedResult>(result2);
            var body2 = created2.Value!;
            int used2 = (int)body2.GetType().GetProperty("LicenseSeatsUsed")!.GetValue(body2)!;
            int total2 = (int)body2.GetType().GetProperty("LicenseTotalSeats")!.GetValue(body2)!;

            // Still one open seat used; unchanged total
            Assert.Equal(1, used2);
            Assert.Equal(2, total2);
        }

        [Fact]
        public async Task Assign_Returns404_When_Software_NotFound()
        {
            var dbName = Guid.NewGuid().ToString("N");
            // Seed user only (no software)
            var ctx = new AimsDbContext(NewDbOptions(dbName));
            await ctx.Database.EnsureCreatedAsync();
            var u = new User
            {
                FullName = "Only User",
                Email = "u@x",
                EmployeeNumber = "U-NA",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsArchived = false
            };
            ctx.Users.Add(u);
            await ctx.SaveChangesAsync();

            var controller = NewController(ctx);
            var svc = NewService(ctx, out _);

            var res = await controller.AssignSeat(
                new AssignSeatRequestDto { SoftwareID = 999_999, UserID = u.UserID }, svc);

            Assert.IsType<NotFoundResult>(res);
        }

        [Fact]
        public async Task Assign_Returns409_When_Capacity_Exhausted()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 1, usedSeats: 1);
            var controller = NewController(ctx);
            var svc = NewService(ctx, out _);

            var res = await controller.AssignSeat(
                new AssignSeatRequestDto { SoftwareID = swId, UserID = userId }, svc);

            var conflict = Assert.IsType<ConflictObjectResult>(res);
            Assert.Equal(409, conflict.StatusCode);
            var msg = conflict.Value!.GetType().GetProperty("message")!.GetValue(conflict.Value) as string;
            Assert.Contains("No available seats", msg!);
        }

        [Fact]
        public async Task Release_Returns200_And_DecrementsCounts()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var (ctx, swId, userId) = await SeedAsync(dbName, totalSeats: 2, usedSeats: 0);
            var controller = NewController(ctx);
            var svc = NewService(ctx, out _);

            // Create an open assignment first
            var assign = await controller.AssignSeat(
                new AssignSeatRequestDto { SoftwareID = swId, UserID = userId }, svc);
            Assert.IsType<CreatedResult>(assign);

            var res = await controller.ReleaseSeat(
                new ReleaseSeatRequestDto { SoftwareID = swId, UserID = userId }, svc);

            var ok = Assert.IsType<OkObjectResult>(res);
            Assert.Equal(200, ok.StatusCode);

            var body = ok.Value!;
            int used = (int)body.GetType().GetProperty("LicenseSeatsUsed")!.GetValue(body)!;
            int total = (int)body.GetType().GetProperty("LicenseTotalSeats")!.GetValue(body)!;

            Assert.Equal(0, used);
            Assert.Equal(2, total);

            // Also verify DB reflects counts
            var (dbUsed, dbTotal) = ReadCounts(ctx, swId);
            Assert.Equal(0, dbUsed);
            Assert.Equal(2, dbTotal);
        }

        [Fact]
        public async Task Release_Returns409_On_Concurrency_Failure()
        {
            var dbName = Guid.NewGuid().ToString("N");

            // Seed with an OPEN assignment so release attempts to write
            var baseCtx = new AimsDbContext(NewDbOptions(dbName));
            await baseCtx.Database.EnsureCreatedAsync();

            var u = new User
            {
                FullName = "User",
                Email = "u@x",
                EmployeeNumber = "U-1",
                ExternalId = Guid.NewGuid(),
                GraphObjectID = Guid.NewGuid().ToString("N"),
                IsArchived = false
            };
            var sw = new Software
            {
                SoftwareName = "Tool",
                SoftwareType = "Util",
                SoftwareVersion = "1.0",
                SoftwareLicenseKey = "K-1",
                LicenseTotalSeats = 1,
                LicenseSeatsUsed = 1
            };
            baseCtx.Users.Add(u);
            baseCtx.SoftwareAssets.Add(sw);
            await baseCtx.SaveChangesAsync();

            baseCtx.Assignments.Add(new Assignment
            {
                AssetKind = AssetKind.Software,
                SoftwareID = sw.SoftwareID,
                UserID = u.UserID,
                AssignedAtUtc = DateTime.UtcNow,
                UnassignedAtUtc = null
            });
            await baseCtx.SaveChangesAsync();
            await baseCtx.DisposeAsync();

            // New context that always throws on SaveChanges
            var throwingCtx = new AimsDbContext(NewDbOptions(dbName, new ThrowAlwaysConcurrencyInterceptor()));
            var controller = NewController(throwingCtx);
            var svc = NewService(throwingCtx, out _);

            var res = await controller.ReleaseSeat(
                new ReleaseSeatRequestDto { SoftwareID = sw.SoftwareID, UserID = u.UserID }, svc);

            var conflict = Assert.IsType<ConflictObjectResult>(res);
            Assert.Equal(409, conflict.StatusCode);
            var msg = conflict.Value!.GetType().GetProperty("message")!.GetValue(conflict.Value) as string;
            Assert.Contains("Concurrency conflict", msg!);
        }
    }
}
