using AIMS.Data;
using AIMS.Models;
using AIMS.Services;
using Microsoft.EntityFrameworkCore;

namespace AIMS.UnitTests.Services
{
    public class SummaryCardSnapshotTests
    {
        // ---------- Local helpers so this file compiles standalone ----------

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
            var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
            return new SummaryCardService(db, cache);
        }

        private static async Task SeedAsync(AimsDbContext db, System.Action<AimsDbContext> seed)
        {
            seed(db);
            await db.SaveChangesAsync();
        }

        // -------------------------------------------------------------------
        // 1) Software usage levels: 0%, 49%, 50%, 100% (effectively: per-asset full/not-full rollup)
        //    Threshold 50% => LOW when >= half of software assets are FULL.
        // -------------------------------------------------------------------
        [Fact]
        public async Task Software_UsageLevels_Snapshot()
        {
            using var db = NewDb(nameof(Software_UsageLevels_Snapshot));
            await SeedAsync(db, d =>
            {
                // Four software assets (all grouped under "Software" type)
                d.SoftwareAssets.AddRange(
                    new Software { SoftwareID = 1, SoftwareType = "Software", LicenseTotalSeats = 2 }, // not full
                    new Software { SoftwareID = 2, SoftwareType = "Software", LicenseTotalSeats = 2 }, // not full
                    new Software { SoftwareID = 3, SoftwareType = "Software", LicenseTotalSeats = 1 }, // will be full
                    new Software { SoftwareID = 4, SoftwareType = "Software", LicenseTotalSeats = 1 }  // will be full
                );

                // Make #3 full
                d.Assignments.Add(new Assignment
                {
                    AssignmentID = 1,
                    AssetKind = AssetKind.Software,
                    SoftwareID = 3,
                    UnassignedAtUtc = null
                });

                // Make #4 full
                d.Assignments.Add(new Assignment
                {
                    AssignmentID = 2,
                    AssetKind = AssetKind.Software,
                    SoftwareID = 4,
                    UnassignedAtUtc = null
                });

                // Threshold 50% => LOW when >= half of the software assets are full
                d.Thresholds.Add(new Threshold { AssetType = "Software", ThresholdValue = 50 });
            });

            var svc = NewSvc(db);
            var rows = await svc.GetSummaryAsync(new[] { "Software" });
            var row = Assert.Single(rows);

            // sanity asserts
            Assert.Equal("Software", row.AssetType);
            Assert.Equal(4, row.Total);
            Assert.Equal(2, row.Available);           // 2 full, 2 not full => available=2
            Assert.Equal(50, row.AvailablePercent);   // 2 / 4 = 50%
            Assert.True(row.IsLow);                   // 2/4 full = 50% >= threshold 50

            await Verifier.Verify(new
            {
                row.AssetType,
                row.Total,
                row.Available,
                row.AvailablePercent,
                row.Threshold,
                row.IsLow
            });
        }

        // -------------------------------------------------------------------
        // 2) Hardware tri-state around threshold: available <, =, > threshold
        //    Here we pin available == threshold to ensure IsLow=false at equality.
        // -------------------------------------------------------------------
        [Fact]
        public async Task Hardware_Threshold_TriState_Snapshot()
        {
            using var db = NewDb(nameof(Hardware_Threshold_TriState_Snapshot));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Laptop" },
                    new Hardware { HardwareID = 2, AssetType = "Laptop" },
                    new Hardware { HardwareID = 3, AssetType = "Laptop" }
                );
                // Assign one => available becomes 2
                d.Assignments.Add(new Assignment
                {
                    AssignmentID = 10,
                    AssetKind = AssetKind.Hardware,
                    HardwareID = 1,
                    UnassignedAtUtc = null
                });

                // Threshold=2 => IsLow iff available < 2. Here available == 2 => not low.
                d.Thresholds.Add(new Threshold { AssetType = "Laptop", ThresholdValue = 2 });
            });

            var svc = NewSvc(db);
            var rows = await svc.GetSummaryAsync(new[] { "Laptop" });
            var row = Assert.Single(rows);

            Assert.Equal(3, row.Total);
            Assert.Equal(2, row.Available);
            Assert.False(row.IsLow);

            await Verifier.Verify(new
            {
                row.AssetType,
                row.Total,
                row.Available,
                row.AvailablePercent,
                row.Threshold,
                row.IsLow
            });
        }

        // -------------------------------------------------------------------
        // 3) Mixed categories + filter: ensure grouping, ordering, and filter behavior.
        // -------------------------------------------------------------------
        [Fact]
        public async Task Mixed_Types_Filtered_Snapshot()
        {
            using var db = NewDb(nameof(Mixed_Types_Filtered_Snapshot));
            await SeedAsync(db, d =>
            {
                d.HardwareAssets.AddRange(
                    new Hardware { HardwareID = 1, AssetType = "Monitor" },
                    new Hardware { HardwareID = 2, AssetType = "Laptop" }
                );

                d.SoftwareAssets.Add(
                    new Software { SoftwareID = 1, SoftwareType = "Software", LicenseTotalSeats = 2 }
                );

                d.Thresholds.AddRange(
                    new Threshold { AssetType = "Monitor", ThresholdValue = 1 },
                    new Threshold { AssetType = "Laptop", ThresholdValue = 5 },
                    new Threshold { AssetType = "Software", ThresholdValue = 50 }
                );

                // Make Monitor unavailable (assigned)
                d.Assignments.Add(new Assignment
                {
                    AssignmentID = 1,
                    AssetKind = AssetKind.Hardware,
                    HardwareID = 1,
                    UnassignedAtUtc = null
                });
            });

            var svc = NewSvc(db);

            // Filter: exclude Monitor; only Laptop & Software should appear.
            var rows = await svc.GetSummaryAsync(new[] { "Laptop", "Software" });
            Assert.Equal(2, rows.Count);

            // Snapshot minimal deterministic payload in sorted order (service orders by AssetType)
            var payload = rows.Select(r => new
            {
                r.AssetType,
                r.Total,
                r.Available,
                r.AvailablePercent,
                r.Threshold,
                r.IsLow
            });

            await Verifier.Verify(payload);
        }

        // -------------------------------------------------------------------
        // 4) Seat-cap edge: cap==0 must be treated as 1 (so a single assignment fills it)
        // -------------------------------------------------------------------
        [Fact]
        public async Task Software_SeatCapZero_TreatedAsOne_Snapshot()
        {
            using var db = NewDb(nameof(Software_SeatCapZero_TreatedAsOne_Snapshot));
            await SeedAsync(db, d =>
            {
                d.SoftwareAssets.Add(new Software
                {
                    SoftwareID = 1,
                    SoftwareType = "Software",
                    LicenseTotalSeats = 0 // edge: treat as 1
                });

                // One assignment => becomes FULL
                d.Assignments.Add(new Assignment
                {
                    AssignmentID = 1,
                    AssetKind = AssetKind.Software,
                    SoftwareID = 1,
                    UnassignedAtUtc = null
                });

                d.Thresholds.Add(new Threshold { AssetType = "Software", ThresholdValue = 100 });
            });

            var svc = NewSvc(db);
            var rows = await svc.GetSummaryAsync(new[] { "Software" });
            var row = Assert.Single(rows);

            // math sanity
            Assert.Equal(1, row.Total);
            Assert.Equal(0, row.Available);
            Assert.True(row.IsLow); // 100% full vs threshold 100

            await Verifier.Verify(new
            {
                row.AssetType,
                row.Total,
                row.Available,
                row.AvailablePercent,
                row.Threshold,
                row.IsLow
            });
        }
    }
}
