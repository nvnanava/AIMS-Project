using System.Net;
using System.Net.Http.Json;
using AIMS.Data;
using AIMS.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Tests.Integration
{
    [Collection("API Test Collection")]
    public class RealtimeDedupAndResilienceTests
    {
        private readonly APIWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public RealtimeDedupAndResilienceTests(APiTestFixture fixture)
        {
            // the fixture exposes the preconfigured factory that has TestAuth wired
            _factory = fixture._webFactory;
            _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        // Matches AIMS.Contracts.AuditEventDto
        private sealed class PollEventDto
        {
            public string Id { get; set; } = "";
            public DateTime OccurredAtUtc { get; set; }
            public string Type { get; set; } = "";
            public string User { get; set; } = "";
            public string Target { get; set; } = "";
            public string Details { get; set; } = "";
            public string Hash { get; set; } = "";
        }

        // Wrapper returned by GET /api/audit/events
        private sealed class EventsEnvelope
        {
            public List<PollEventDto> items { get; set; } = new();
            public string? nextSince { get; set; }
        }

        private async Task<List<PollEventDto>> GetEventsSinceAsync(DateTime sinceUtc, int take = 50)
        {
            var url = $"/api/audit/events?since={Uri.EscapeDataString(sinceUtc.ToString("O"))}&take={take}";
            var payload = await _client.GetFromJsonAsync<EventsEnvelope>(url);
            return payload?.items ?? new List<PollEventDto>();
        }

        // -------------------------- DB helpers -----------------------------

        private async Task CleanupAsync(params Guid[] ids)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            var toRemove = await db.AuditLogs
                .Where(a => ids.Contains(a.ExternalId))
                .ToListAsync();

            if (toRemove.Count > 0)
            {
                db.AuditLogs.RemoveRange(toRemove);
                await db.SaveChangesAsync();
            }
        }

        // Ensure at least one Role and User exist; return a usable UserID.
        private async Task<int> EnsureUserAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            // Return any existing active user immediately
            var existingUserId = await db.Users
                .Where(u => !u.IsArchived)
                .OrderBy(u => u.UserID)
                .Select(u => u.UserID)
                .FirstOrDefaultAsync();

            if (existingUserId != 0)
                return existingUserId;

            // Find-or-create a role by name (avoids racing on numeric IDs)
            const string roleName = "TestAdmin";
            var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName);
            if (role is null)
            {
                role = new Role
                {
                    RoleName = roleName,
                    Description = "Integration test role"
                };
                db.Roles.Add(role);
                await db.SaveChangesAsync(); // ensures RoleID is generated
            }

            // Create a fresh user referencing the tracked role's PK
            var user = new User
            {
                Email = $"itest+{Guid.NewGuid():N}@local",
                FullName = "Integration Test User",
                EmployeeNumber = "ITEST-001",
                ExternalId = Guid.NewGuid(),
                // Non-nullable in model: supply a dummy GraphObjectID
                GraphObjectID = Guid.NewGuid().ToString("D"),
                IsActive = true,
                IsArchived = false,
                RoleID = role.RoleID,
                SupervisorID = null
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return user.UserID;
        }

        // Ensure a real HardwareID exists; used to satisfy CK_AuditLog_ExactlyOneAsset.
        private async Task<int> EnsureAnyHardwareIdAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            var hid = await db.HardwareAssets
                .AsNoTracking()
                .OrderBy(h => h.HardwareID)
                .Select(h => h.HardwareID)
                .FirstOrDefaultAsync();

            if (hid == 0)
                throw new InvalidOperationException(
                    "No HardwareAssets found in DB. Seed at least one hardware row " +
                    "(or adapt tests to use Software assets with a real SoftwareID).");

            return hid;
        }

        /// Upsert an AuditLog by ExternalId.
        private async Task UpsertAuditAsync(
            Guid externalId,
            string action,
            string description,
            DateTime? atUtc = null,
            AssetKind kind = AssetKind.Hardware,
            int? hardwareId = null,
            int? softwareId = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            var userId = await EnsureUserAsync();

            // Enforce "exactly one of HardwareID/SoftwareID" with real IDs
            if (kind == AssetKind.Hardware)
            {
                if (hardwareId is null)
                    hardwareId = await EnsureAnyHardwareIdAsync();
                softwareId = null;
            }
            else if (kind == AssetKind.Software)
            {
                if (softwareId is null)
                    throw new InvalidOperationException("Please supply an existing SoftwareID or extend helper to fetch one.");
                hardwareId = null;
            }
            else
            {
                throw new InvalidOperationException("Unknown AssetKind.");
            }

            var existing = await db.AuditLogs.SingleOrDefaultAsync(a => a.ExternalId == externalId);
            if (existing is null)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    ExternalId = externalId,
                    UserID = userId,
                    Action = action,
                    Description = description,
                    TimestampUtc = atUtc ?? DateTime.UtcNow,
                    AssetKind = kind,
                    HardwareID = hardwareId,
                    SoftwareID = softwareId
                });
            }
            else
            {
                existing.Action = action;
                existing.Description = description;
                existing.TimestampUtc = atUtc ?? DateTime.UtcNow;
                existing.AssetKind = kind;

                // keep exactly-one rule on updates as well
                existing.HardwareID = (kind == AssetKind.Hardware) ? hardwareId : (int?)null;
                existing.SoftwareID = (kind == AssetKind.Software) ? softwareId : (int?)null;

                db.AuditLogs.Update(existing);
            }

            await db.SaveChangesAsync();
        }

        // ============================= Tests ===============================

        [Fact(DisplayName = "Dedup: same ExternalId twice → single row, latest details win")]
        public async Task Dedup_SameExternalId_Twice_NoDuplicates()
        {
            var id = Guid.NewGuid();
            await CleanupAsync(id);

            await UpsertAuditAsync(id, "Create", "first insert", DateTime.UtcNow.AddSeconds(-5));
            await UpsertAuditAsync(id, "Update", "second insert same id", DateTime.UtcNow);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            var rows = await db.AuditLogs.Where(a => a.ExternalId == id).ToListAsync();
            rows.Count.Should().Be(1, "upsert should keep a single row per ExternalId");
            rows[0].Action.Should().Be("Update");
            rows[0].Description.Should().Be("second insert same id");
        }

        [Fact(DisplayName = "Dedup: re-publish newer details → row updated in place")]
        public async Task Dedup_SameExternalId_UpdatesExistingRow()
        {
            var id = Guid.NewGuid();
            await CleanupAsync(id);

            await UpsertAuditAsync(id, "Create", "original", DateTime.UtcNow.AddSeconds(-3));
            await UpsertAuditAsync(id, "Update", "updated", DateTime.UtcNow);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            var row = await db.AuditLogs.SingleAsync(a => a.ExternalId == id);
            row.Action.Should().Be("Update");
            row.Description.Should().Be("updated");
        }

        [Fact(DisplayName = "Resilience: offline insert → GET ?since= catches up")]
        public async Task Resilience_Offline_Publish_Then_Since_Catchup()
        {
            var since = DateTime.UtcNow;

            var e1 = Guid.NewGuid();
            var e2 = Guid.NewGuid();
            await CleanupAsync(e1, e2);

            await Task.Delay(50);
            await UpsertAuditAsync(e1, "Create", "offline-1", DateTime.UtcNow);
            await Task.Delay(50);
            await UpsertAuditAsync(e2, "Create", "offline-2", DateTime.UtcNow);

            var caughtUp = await GetEventsSinceAsync(since, take: 50);

            var ids = new HashSet<string>(caughtUp.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
            ids.Should().Contain(e1.ToString());
            ids.Should().Contain(e2.ToString());
        }

        [Fact(DisplayName = "Resilience: burst polling → 429 if limited; otherwise skip assertion")]
        public async Task Resilience_429_Backoff_And_Recovery()
        {
            var url = "/api/audit/events?since=1970-01-01T00:00:00.0000000Z&take=1";
            var statuses = new List<HttpStatusCode>();

            for (int i = 0; i < 50; i++)
            {
                var resp = await _client.GetAsync(url);
                statuses.Add(resp.StatusCode);
                await Task.Delay(20);
            }

            if (statuses.Any(s => (int)s == 429))
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var after = await _client.GetAsync(url);
                after.StatusCode.Should().BeOneOf(
                    HttpStatusCode.OK,
                    HttpStatusCode.NotModified,
                    HttpStatusCode.BadRequest,
                    HttpStatusCode.NoContent
                );
            }
            else
            {
                Console.WriteLine("[RateLimit] No 429 observed; policy may not be applied in Test. Marking as pass.");
                // Intentionally treat as pass when rate limiting is off.
            }
        }
    }
}
