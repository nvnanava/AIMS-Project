using AIMS.Data;
using AIMS.Models;
using AIMS.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.UnitTests.Services
{
    public class SummaryCardServiceTests
    {
        private static AimsDbContext NewDb(string name)
        {
            var opts = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(name)
                .EnableSensitiveDataLogging()
                .Options;
            return new AimsDbContext(opts);
        }

        private static SummaryCardService NewSvc(AimsDbContext db)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new SummaryCardService(db, cache);
        }

        private static async Task SeedAsync(AimsDbContext db, Action<AimsDbContext> seed)
        {
            seed(db);
            await db.SaveChangesAsync();
        }

        // ---------- NormalizeFilter branches ----------

        [Fact]
        public async Task NormalizeFilter_TypesNull_TreatedAsAll()
        {
            using var db = NewDb(nameof(NormalizeFilter_TypesNull_TreatedAsAll));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.Add(new Hardware { HardwareID = 1, AssetType = "Laptop" });
                d.SoftwareAssets.Add(new Software { SoftwareID = 1, SoftwareType = "Software" });
            });

            var svc = NewSvc(db);
            var rows = await svc.GetSummaryAsync(null); // types == null → "all"
            Assert.Contains(rows, r => r.AssetType.Equals("Laptop", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rows, r => r.AssetType.Equals("Software", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task NormalizeFilter_TrimsToEmpty_TreatedAsAll()
        {
            using var db = NewDb(nameof(NormalizeFilter_TrimsToEmpty_TreatedAsAll));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Laptop" },
                    new Hardware { HardwareID = 2, AssetType = "Desktop" }
                );
            });

            var svc = NewSvc(db);
            // non-null IEnumerable<string> that trims down to empty → treated as "all"
            var rows = await svc.GetSummaryAsync(new[] { "   ", "\t", "" });
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public async Task NormalizeFilter_Distinct_And_CaseInsensitive()
        {
            using var db = NewDb(nameof(NormalizeFilter_Distinct_And_CaseInsensitive));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.Add(new Hardware { HardwareID = 1, AssetType = "Laptop" });
                d.HardwareAssets.Add(new Hardware { HardwareID = 2, AssetType = "Desktop" });
                d.HardwareAssets.Add(new Hardware { HardwareID = 3, AssetType = "Monitor" });
            });

            var svc = NewSvc(db);
            // IMPORTANT: no stray spaces here; ComputeAsync does not Trim()
            var rows = await svc.GetSummaryAsync(new[] { "laptop", "LAPTOP", "desktop", "MONITOR", "monitor" });
            Assert.Equal(3, rows.Count);
        }

        // ---------- Thresholds GroupBy(x.AssetType ?? "") branch ----------

        [Fact]
        public async Task Threshold_WithEmpty_AssetType_Hits_NullCoalesce_Branch()
        {
            using var db = NewDb(nameof(Threshold_WithEmpty_AssetType_Hits_NullCoalesce_Branch));
            await SeedAsync(db, d =>
            {
                // Use empty string instead of null to satisfy [Required] while hitting the ?? "" branch.
                d.Thresholds.Add(new Threshold { ThresholdID = 1, AssetType = string.Empty, ThresholdValue = 5 });

                // Add at least one asset so the computation runs
                d.HardwareAssets.Add(new Hardware { HardwareID = 1, AssetType = "Laptop" });
            });

            var svc = NewSvc(db);
            var rows = await svc.GetSummaryAsync(new[] { "Laptop" });

            // We only care that it executed successfully (branch covered)
            var row = Assert.Single(rows);
            Assert.Equal("Laptop", row.AssetType);
        }

        // ---------- IsLow decision table ----------

        [Fact]
        public async Task BelowThreshold_IsLow_True()
        {
            using var db = NewDb(nameof(BelowThreshold_IsLow_True));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Monitor" },
                    new Hardware { HardwareID = 2, AssetType = "Monitor" },
                    new Hardware { HardwareID = 3, AssetType = "Monitor" }
                );
                d.Assignments.AddRange(
                    new Assignment { AssignmentID = 10, AssetKind = AssetKind.Hardware, HardwareID = 1, UnassignedAtUtc = null },
                    new Assignment { AssignmentID = 11, AssetKind = AssetKind.Hardware, HardwareID = 2, UnassignedAtUtc = null }
                );
                d.Thresholds.Add(new Threshold { AssetType = "Monitor", ThresholdValue = 2 });
            });

            var svc = NewSvc(db);
            var row = Assert.Single(await svc.GetSummaryAsync(new[] { "Monitor" }));
            Assert.True(row.IsLow);
        }

        [Fact]
        public async Task EqualThreshold_IsLow_False()
        {
            using var db = NewDb(nameof(EqualThreshold_IsLow_False));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Laptop" },
                    new Hardware { HardwareID = 2, AssetType = "Laptop" },
                    new Hardware { HardwareID = 3, AssetType = "Laptop" }
                );
                d.Assignments.Add(
                    new Assignment { AssignmentID = 1, AssetKind = AssetKind.Hardware, HardwareID = 1, UnassignedAtUtc = null });
                d.Thresholds.Add(new Threshold { AssetType = "Laptop", ThresholdValue = 2 });
            });

            var svc = NewSvc(db);
            var row = Assert.Single(await svc.GetSummaryAsync(new[] { "Laptop" }));
            Assert.False(row.IsLow);
        }

        [Fact]
        public async Task NoThreshold_DefaultsZero_IsLow_False()
        {
            using var db = NewDb(nameof(NoThreshold_DefaultsZero_IsLow_False));
            await SeedAsync(db, d =>
            {
                d.SoftwareAssets.AddRange(
                    new Software { SoftwareID = 1, SoftwareType = "Software" },
                    new Software { SoftwareID = 2, SoftwareType = "Software" }
                );
            });

            var svc = NewSvc(db);
            var row = Assert.Single(await svc.GetSummaryAsync(new[] { "Software" }));
            Assert.Equal(0, row.Threshold);
            Assert.False(row.IsLow);
            Assert.Equal(100, row.AvailablePercent);
        }

        // ---------- Cache behavior: hit & invalidation ----------

        [Fact]
        public async Task Cache_Hit_Returns_Cached_Result()
        {
            using var db = NewDb(nameof(Cache_Hit_Returns_Cached_Result));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Headset" },
                    new Hardware { HardwareID = 2, AssetType = "Headset" }
                );
            });

            var svc = NewSvc(db);
            var first = await svc.GetSummaryAsync(new[] { "Headset" }); // populates cache
            Assert.Equal(2, first.Single().Total);

            // Change DB AFTER caching
            db.HardwareAssets.Add(new Hardware { HardwareID = 3, AssetType = "Headset" });
            await db.SaveChangesAsync();

            // Still returns cached (Total=2) if TTL not expired and not invalidated
            var second = await svc.GetSummaryAsync(new[] { "Headset" });
            Assert.Equal(2, second.Single().Total);
        }

        [Fact]
        public async Task InvalidateSummaryCache_Forces_Fresh_Read()
        {
            using var db = NewDb(nameof(InvalidateSummaryCache_Forces_Fresh_Read));
            var svc = NewSvc(db);

            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Headset" },
                    new Hardware { HardwareID = 2, AssetType = "Headset" }
                );
                d.Thresholds.Add(new Threshold { AssetType = "Headset", ThresholdValue = 1 });
            });

            var first = await svc.GetSummaryAsync(new[] { "Headset" });
            Assert.Equal(2, first.Single().Available);

            db.Assignments.Add(new Assignment
            {
                AssignmentID = 99,
                AssetKind = AssetKind.Hardware,
                HardwareID = 1,
                UnassignedAtUtc = null
            });
            await db.SaveChangesAsync();

            // still cached
            var cached = await svc.GetSummaryAsync(new[] { "Headset" });
            Assert.Equal(2, cached.Single().Available);

            // now invalidate
            svc.InvalidateSummaryCache();
            var fresh = await svc.GetSummaryAsync(new[] { "Headset" });
            Assert.Equal(1, fresh.Single().Available);
        }

        [Fact]
        public async Task NormalizeFilter_Produces_Lowercase_Sorted_Stable_CacheKey()
        {
            using var db = NewDb(nameof(NormalizeFilter_Produces_Lowercase_Sorted_Stable_CacheKey));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Laptop" },
                    new Hardware { HardwareID = 2, AssetType = "Monitor" }
                );
            });

            var svc = NewSvc(db);

            // Prime cache with a noisy, unsorted, mixed-case list (with duplicates & spaces)
            var first = await svc.GetSummaryAsync(new[] { "  LAPTOP ", "Monitor", "laptop", "MONITOR " });
            Assert.NotNull(first);

            // Same logical set, but different order/casing/whitespace → should hit SAME cache entry
            var second = await svc.GetSummaryAsync(new[] { "monitor", "LAPTOP" });
            Assert.Same(first, second); // same reference => same normalized cache key

            // Another permutation with more noise → still the same cache entry
            var third = await svc.GetSummaryAsync(new[] { "  monitor", " laptop  ", "LAPTOP", "MONITOR" });
            Assert.Same(first, third);

            // Control: a truly different set should create a different cache entry (different reference)
            var different = await svc.GetSummaryAsync(new[] { "Laptop" }); // not the same set as {Laptop,Monitor}
            Assert.NotSame(first, different);
        }

        [Fact]
        public async Task HighVolume_Computation_IsAccurate_And_Cacheable()
        {
            using var db = NewDb(nameof(HighVolume_Computation_IsAccurate_And_Cacheable));
            var svc = NewSvc(db);

            const string Type = "HighVolume-Unit";
            const int total = 8000;
            const int assigned = 2500; // => available = 5500
            const int threshold = 5000; // 5500 >= 5000 => IsLow = false

            await SeedAsync(db, d =>
            {
                // Seed N assets
                for (int i = 0; i < total; i++)
                {
                    d.HardwareAssets.Add(new Hardware { HardwareID = i + 1, AssetType = Type });
                }

                // Assign first K
                for (int i = 1; i <= assigned; i++)
                {
                    d.Assignments.Add(new Assignment
                    {
                        AssignmentID = i,
                        AssetKind = AssetKind.Hardware,
                        HardwareID = i,
                        UnassignedAtUtc = null
                    });
                }

                d.Thresholds.Add(new Threshold { AssetType = Type, ThresholdValue = threshold });
            });

            // First call (fills cache)
            var first = await svc.GetSummaryAsync(new[] { Type });
            var r1 = Assert.Single(first);
            Assert.Equal(total, r1.Total);
            Assert.Equal(total - assigned, r1.Available);
            Assert.Equal(threshold, r1.Threshold);
            Assert.False(r1.IsLow); // 5500 >= 5000

            // Second call should hit cache (same reference proves same cache key)
            var second = await svc.GetSummaryAsync(new[] { "  " + Type.ToUpperInvariant() + "  " });
            Assert.Same(first, second);
        }

        [Fact]
        public async Task Software_Threshold_Percentage_IsLow_True_When_UsedPct_GreaterOrEqual()
        {
            using var db = NewDb(nameof(Software_Threshold_Percentage_IsLow_True_When_UsedPct_GreaterOrEqual));
            await SeedAsync(db, d =>
            {
                // 3 software assets (titles), each with 2 seats
                d.SoftwareAssets.AddRange(
                    new Software { SoftwareID = 1, SoftwareType = "Software", LicenseTotalSeats = 2 },
                    new Software { SoftwareID = 2, SoftwareType = "Software", LicenseTotalSeats = 2 },
                    new Software { SoftwareID = 3, SoftwareType = "Software", LicenseTotalSeats = 2 }
                );

                // Make 2 out of 3 fully assigned (2 open assignments each)
                d.Assignments.AddRange(
                    // SoftwareID = 1 (full)
                    new Assignment { AssignmentID = 101, AssetKind = AssetKind.Software, SoftwareID = 1, UnassignedAtUtc = null },
                    new Assignment { AssignmentID = 102, AssetKind = AssetKind.Software, SoftwareID = 1, UnassignedAtUtc = null },

                    // SoftwareID = 2 (full)
                    new Assignment { AssignmentID = 201, AssetKind = AssetKind.Software, SoftwareID = 2, UnassignedAtUtc = null },
                    new Assignment { AssignmentID = 202, AssetKind = AssetKind.Software, SoftwareID = 2, UnassignedAtUtc = null }

                // SoftwareID = 3 has 0 → available
                );

                // 2 of 3 full => 66.7% ~ 67% used. Threshold 67 → should mark low.
                d.Thresholds.Add(new Threshold { AssetType = "Software", ThresholdValue = 67 });
            });

            var svc = NewSvc(db);
            var row = Assert.Single(await svc.GetSummaryAsync(new[] { "Software" }));

            Assert.Equal(3, row.Total);               // three titles
            Assert.Equal(1, row.Available);           // only ID=3 is available
            Assert.Equal(67, row.Threshold);
            Assert.True(row.IsLow);                   // 67% used >= 67% → low
        }

        [Fact]
        public async Task Software_Threshold_Percentage_IsLow_False_When_UsedPct_Less()
        {
            using var db = NewDb(nameof(Software_Threshold_Percentage_IsLow_False_When_UsedPct_Less));
            await SeedAsync(db, d =>
            {
                // 3 software assets (titles), each with 2 seats
                d.SoftwareAssets.AddRange(
                    new Software { SoftwareID = 11, SoftwareType = "Software", LicenseTotalSeats = 2 },
                    new Software { SoftwareID = 12, SoftwareType = "Software", LicenseTotalSeats = 2 },
                    new Software { SoftwareID = 13, SoftwareType = "Software", LicenseTotalSeats = 2 }
                );

                // Make only 1 out of 3 fully assigned (2 open assignments)
                d.Assignments.AddRange(
                    new Assignment { AssignmentID = 301, AssetKind = AssetKind.Software, SoftwareID = 11, UnassignedAtUtc = null },
                    new Assignment { AssignmentID = 302, AssetKind = AssetKind.Software, SoftwareID = 11, UnassignedAtUtc = null }

                // IDs 12,13 remain available
                );

                // 1 of 3 full => 33% used. Threshold 67 → NOT low.
                d.Thresholds.Add(new Threshold { AssetType = "Software", ThresholdValue = 67 });
            });

            var svc = NewSvc(db);
            var row = Assert.Single(await svc.GetSummaryAsync(new[] { "Software" }));

            Assert.Equal(3, row.Total);
            Assert.Equal(2, row.Available);           // two titles still available
            Assert.Equal(67, row.Threshold);
            Assert.False(row.IsLow);                  // 33% used < 67% → not low
        }
    }
}
