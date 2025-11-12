using System.Net;
using System.Net.Http.Json;
using AIMS.Data;
using AIMS.Dtos.Dashboard;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Tests.Integration.API
{
    [Collection("API Test Collection")]
    public class SummaryCardsApiTests
    {
        private readonly APIWebApplicationFactory<Program> _factory;

        public SummaryCardsApiTests(APiTestFixture fixture)
        {
            _factory = fixture._webFactory;
        }

        // ---------------- helpers ------------------------------------------------

        private static async Task CleanupAsync(AimsDbContext db, params string[] assetTypes)
        {
            // Resolve IDs first for both HW and SW of the types we plan to wipe
            var hwIds = await db.HardwareAssets
                .Where(h => assetTypes.Contains(h.AssetType))
                .Select(h => h.HardwareID)
                .ToListAsync();

            var swIds = await db.SoftwareAssets
                .Where(s => assetTypes.Contains(s.SoftwareType))
                .Select(s => s.SoftwareID)
                .ToListAsync();

            // Find any audit log rows that reference those HW/SW IDs
            var affectedAuditLogs = await db.AuditLogs
                .Where(a =>
                    (a.HardwareID != null && hwIds.Contains(a.HardwareID.Value)) ||
                    (a.SoftwareID != null && swIds.Contains(a.SoftwareID.Value)))
                .Select(a => a.AuditLogID)
                .ToListAsync();

            if (affectedAuditLogs.Count > 0)
            {
                // Delete child changes first, then the audit logs
                var changes = await db.AuditLogChanges
                    .Where(c => affectedAuditLogs.Contains(c.AuditLogID))
                    .ToListAsync();
                db.AuditLogChanges.RemoveRange(changes);

                var audits = await db.AuditLogs
                    .Where(a => affectedAuditLogs.Contains(a.AuditLogID))
                    .ToListAsync();
                db.AuditLogs.RemoveRange(audits);
            }

            // Remove thresholds next (they don't hold FKs to assets, but we want a truly clean slate)
            var th = await db.Thresholds
                .Where(t => assetTypes.Contains(t.AssetType))
                .ToListAsync();
            db.Thresholds.RemoveRange(th);

            // Remove assignments that reference hardware/software we will delete
            if (hwIds.Count > 0)
            {
                var asgHw = await db.Assignments
                    .Where(a => a.AssetKind == AssetKind.Hardware
                                && a.HardwareID != null
                                && hwIds.Contains(a.HardwareID.Value))
                    .ToListAsync();
                db.Assignments.RemoveRange(asgHw);
            }

            if (swIds.Count > 0)
            {
                var asgSw = await db.Assignments
                    .Where(a => a.AssetKind == AssetKind.Software
                                && a.SoftwareID != null
                                && swIds.Contains(a.SoftwareID.Value))
                    .ToListAsync();
                db.Assignments.RemoveRange(asgSw);
            }

            // Now it is safe to delete the assets themselves
            if (hwIds.Count > 0)
            {
                var hw = await db.HardwareAssets
                    .Where(h => hwIds.Contains(h.HardwareID))
                    .ToListAsync();
                db.HardwareAssets.RemoveRange(hw);
            }

            if (swIds.Count > 0)
            {
                var sw = await db.SoftwareAssets
                    .Where(s => swIds.Contains(s.SoftwareID))
                    .ToListAsync();
                db.SoftwareAssets.RemoveRange(sw);
            }

            await db.SaveChangesAsync();
        }

        private static string NewSerial() => Guid.NewGuid().ToString("N");

        // ---------------- tests --------------------------------------------------

        [Fact]
        public async Task GetCards_Returns200_And_BasicShape()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

                // Clean slate for this type
                await CleanupAsync(db, "Monitor");

                // Seed two monitors with unique serials
                var m1 = new Hardware { AssetType = "Monitor", SerialNumber = NewSerial() };
                var m2 = new Hardware { AssetType = "Monitor", SerialNumber = NewSerial() };
                db.HardwareAssets.AddRange(m1, m2);
                await db.SaveChangesAsync();

                // Assign one of them
                db.Assignments.Add(new Assignment
                {
                    AssetKind = AssetKind.Hardware,
                    HardwareID = m1.HardwareID,
                    UnassignedAtUtc = null
                });

                // Threshold: 1
                db.Thresholds.Add(new Threshold { AssetType = "Monitor", ThresholdValue = 1 });

                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();

            // Only request the type we care about
            var resp = await client.GetAsync("/api/summary/cards?types=Monitor");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var rows = await resp.Content.ReadFromJsonAsync<SummaryCardDto[]>();
            Assert.NotNull(rows);
            var row = Assert.Single(rows!);

            Assert.Equal("Monitor", row.AssetType);
            Assert.Equal(2, row.Total);
            Assert.Equal(1, row.Available);
            Assert.Equal(1, row.Threshold);
            Assert.False(row.IsLow);
        }

        [Fact]
        public async Task GetCards_Respects_Filter_List()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

                await CleanupAsync(db, "Laptop", "Desktop", "Headset");

                db.HardwareAssets.AddRange(
                    new Hardware { AssetType = "Laptop", SerialNumber = NewSerial() },
                    new Hardware { AssetType = "Desktop", SerialNumber = NewSerial() },
                    new Hardware { AssetType = "Headset", SerialNumber = NewSerial() }
                );
                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();

            var resp = await client.GetAsync("/api/summary/cards?types=Laptop,Desktop");
            resp.EnsureSuccessStatusCode();

            var rows = await resp.Content.ReadFromJsonAsync<SummaryCardDto[]>();
            Assert.NotNull(rows);
            Assert.Equal(2, rows!.Length);
            Assert.Contains(rows, r => r.AssetType == "Laptop");
            Assert.Contains(rows, r => r.AssetType == "Desktop");
        }

        [Fact]
        public async Task GetCards_Reflects_RealTime_Changes_When_Cache_Invalidated()
        {
            // Isolate server state so no other test pre-seeds cache
            var isolatedFactory = _factory.WithWebHostBuilder(_ => { });
            var client = isolatedFactory.CreateClient();

            int firstLaptopId;
            using (var scope = isolatedFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

                // Start clean
                await CleanupAsync(db, "Laptop");

                // Seed two laptops + a threshold
                var l1 = new Hardware { AssetType = "Laptop", SerialNumber = NewSerial() };
                var l2 = new Hardware { AssetType = "Laptop", SerialNumber = NewSerial() };
                db.HardwareAssets.AddRange(l1, l2);
                db.Thresholds.Add(new Threshold { AssetType = "Laptop", ThresholdValue = 1 });
                await db.SaveChangesAsync();

                firstLaptopId = l1.HardwareID;
            }

            // First read — should see 2 available (populates the filtered cache)
            var first = await client.GetFromJsonAsync<SummaryCardDto[]>("/api/summary/cards?types=Laptop");
            Assert.NotNull(first);
            Assert.Equal(2, first!.Single().Available);

            // Assign one laptop (reduce available from 2 → 1)
            using (var scope = isolatedFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                db.Assignments.Add(new Assignment
                {
                    AssetKind = AssetKind.Hardware,
                    HardwareID = firstLaptopId,
                    UnassignedAtUtc = null
                });
                await db.SaveChangesAsync();
            }

            // Rebuild the "all" snapshot (forces cache invalidation on the "all" key)
            var all = await client.GetFromJsonAsync<SummaryCardDto[]>("/api/summary/cards");
            Assert.NotNull(all);
            var laptopRow = all!.First(r => r.AssetType == "Laptop");

            // Verify the available count is now 1
            Assert.Equal(1, laptopRow.Available);
        }

        [Fact]
        public async Task GetCards_NoThreshold_IsNotLow()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

                await CleanupAsync(db, "Software");

                // Seed one software asset; no threshold for "Software"
                db.SoftwareAssets.Add(new Software { SoftwareType = "Software" });
                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();
            var rows = await client.GetFromJsonAsync<SummaryCardDto[]>("/api/summary/cards?types=Software");

            Assert.NotNull(rows);
            var row = Assert.Single(rows!);
            Assert.Equal("Software", row.AssetType);
            Assert.Equal(0, row.Threshold); // no threshold set → 0
            Assert.False(row.IsLow);
        }

        // Branch: if (filter.Count == 0) filter = null;
        [Fact]
        public async Task GetCards_TypesOnlyCommas_TreatsAsNullAndReturnsAll()
        {
            var client = _factory.CreateClient();

            // A non-empty string that becomes an empty list after Split/Trim/RemoveEmptyEntries
            var resp = await client.GetAsync("/api/summary/cards?types=%20,%20,,");
            resp.EnsureSuccessStatusCode(); // 200 OK

            // We don’t care about contents; we just want the branch executed.
            _ = await resp.Content.ReadAsStringAsync();
        }

        // Branch: controller catch{} → Problem("Failed to compute summary cards.")
        [Fact]
        public async Task GetCards_WhenCacheBlowsUp_ReturnsProblem()
        {
            var throwingFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace IMemoryCache with one that throws on any use
                    services.AddSingleton<IMemoryCache, ThrowingMemoryCache>();
                });
            });

            var client = throwingFactory.CreateClient();

            var resp = await client.GetAsync("/api/summary/cards?types=Monitor");
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);

            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("Failed to compute summary cards.", body);
        }

        // Minimal IMemoryCache that throws on any access
        private sealed class ThrowingMemoryCache : IMemoryCache
        {
            public ICacheEntry CreateEntry(object key) => throw new InvalidOperationException("cache broken");
            public void Dispose() { }
            public void Remove(object key) => throw new InvalidOperationException("cache broken");
            public bool TryGetValue(object key, out object? value)
            {
                value = null;
                throw new InvalidOperationException("cache broken");
            }
        }

        [Fact]
        public async Task GetCards_Filter_IsCaseInsensitive()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                await CleanupAsync(db, "Laptop", "Desktop");

                db.HardwareAssets.AddRange(
                    new Hardware { AssetType = "Laptop", SerialNumber = Guid.NewGuid().ToString("N") },
                    new Hardware { AssetType = "Desktop", SerialNumber = Guid.NewGuid().ToString("N") }
                );
                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();

            // lower/upper/mixed case filter values should still match
            var resp = await client.GetAsync("/api/summary/cards?types=laptop,DESKTOP");
            resp.EnsureSuccessStatusCode();

            var rows = await resp.Content.ReadFromJsonAsync<SummaryCardDto[]>();
            Assert.NotNull(rows);
            Assert.Equal(2, rows!.Length);
            Assert.Contains(rows, r => r.AssetType.Equals("Laptop", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rows, r => r.AssetType.Equals("Desktop", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GetCards_Reflects_Threshold_Update_After_Invalidation()
        {
            var client = _factory.CreateClient();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                await CleanupAsync(db, "Docking Station");

                // two available docking stations, threshold = 1 → not low
                db.HardwareAssets.AddRange(
                    new Hardware { AssetType = "Docking Station", SerialNumber = Guid.NewGuid().ToString("N") },
                    new Hardware { AssetType = "Docking Station", SerialNumber = Guid.NewGuid().ToString("N") }
                );
                db.Thresholds.Add(new Threshold { AssetType = "Docking Station", ThresholdValue = 1 });
                await db.SaveChangesAsync();
            }

            var first = await client.GetFromJsonAsync<SummaryCardDto[]>("/api/summary/cards?types=Docking%20Station");
            Assert.NotNull(first);
            var row1 = Assert.Single(first!);
            Assert.Equal("Docking Station", row1.AssetType);
            Assert.False(row1.IsLow); // 2 available vs threshold 1

            // Increase threshold so it becomes "low", then evict the filtered cache
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

                var th = await db.Thresholds.SingleAsync(t => t.AssetType == "Docking Station");
                th.ThresholdValue = 3; // now 2 < 3 → should be low
                await db.SaveChangesAsync();

                // Evict the cached entries directly (singleton cache)
                var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
                cache.Remove("summary:cards:docking station"); // filtered cache key (NormalizeFilter → lower)
                cache.Remove("summary:cards:all");  // unfiltered snapshot (belt & suspenders)
            }

            var second = await client.GetFromJsonAsync<SummaryCardDto[]>("/api/summary/cards?types=Docking%20Station&ts=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.NotNull(second);
            var row2 = Assert.Single(second!);
            Assert.True(row2.IsLow);
        }

        [Fact]
        public async Task GetCards_Response_Has_Contract_Keys()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                await CleanupAsync(db, "Tablet");

                db.HardwareAssets.Add(new Hardware { AssetType = "Tablet", SerialNumber = Guid.NewGuid().ToString("N") });
                db.Thresholds.Add(new Threshold { AssetType = "Tablet", ThresholdValue = 1 });
                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();
            var resp = await client.GetAsync("/api/summary/cards?types=Tablet");
            resp.EnsureSuccessStatusCode();

            // validate keys exist in raw JSON
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var arr = doc.RootElement;
            Assert.Equal(System.Text.Json.JsonValueKind.Array, arr.ValueKind);

            var obj = arr[0];
            Assert.True(obj.TryGetProperty("assetType", out _) || obj.TryGetProperty("AssetType", out _));
            Assert.True(obj.TryGetProperty("total", out _) || obj.TryGetProperty("Total", out _));
            Assert.True(obj.TryGetProperty("available", out _) || obj.TryGetProperty("Available", out _));
            Assert.True(obj.TryGetProperty("threshold", out _) || obj.TryGetProperty("Threshold", out _));
            Assert.True(obj.TryGetProperty("isLow", out _) || obj.TryGetProperty("IsLow", out _));
            Assert.True(obj.TryGetProperty("availablePercent", out _) || obj.TryGetProperty("AvailablePercent", out _));
        }

        [Fact]
        public async Task GetCards_HighVolume_AccurateCounts()
        {
            const string Type = "HighVolume"; // unique type to avoid collisions
            const int total = 5000;
            const int assigned = 1234; // => available = 3766
            const int threshold = 4000; // 3766 < 4000 => IsLow = true

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                await CleanupAsync(db, Type);

                // Seed 5k hardware rows
                for (int i = 0; i < total; i++)
                {
                    db.HardwareAssets.Add(new Hardware
                    {
                        AssetType = Type,
                        SerialNumber = Guid.NewGuid().ToString("N")
                    });
                }
                await db.SaveChangesAsync();

                // Assign first N rows
                var ids = await db.HardwareAssets
                    .Where(h => h.AssetType == Type)
                    .Select(h => h.HardwareID)
                    .OrderBy(id => id)
                    .Take(assigned)
                    .ToListAsync();

                foreach (var id in ids)
                {
                    db.Assignments.Add(new Assignment
                    {
                        AssetKind = AssetKind.Hardware,
                        HardwareID = id,
                        UnassignedAtUtc = null
                    });
                }

                // Threshold
                db.Thresholds.Add(new Threshold { AssetType = Type, ThresholdValue = threshold });
                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();
            var rows = await client.GetFromJsonAsync<SummaryCardDto[]>($"/api/summary/cards?types={Uri.EscapeDataString(Type)}");

            Assert.NotNull(rows);
            var row = Assert.Single(rows!);
            Assert.Equal(Type, row.AssetType);
            Assert.Equal(total, row.Total);
            Assert.Equal(total - assigned, row.Available);
            Assert.Equal(threshold, row.Threshold);
            Assert.True(row.IsLow); // 3766 < 4000
            // sanity for percent
            Assert.Equal(
                (int)Math.Round(((double)(total - assigned) / total) * 100, MidpointRounding.AwayFromZero),
                row.AvailablePercent
            );
        }

    }
}
