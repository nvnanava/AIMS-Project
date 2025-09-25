using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Tests.Integration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.Tests.Integration.AssetQuerySpecs;

public sealed class AssetQueryIntegrationTests : IClassFixture<DbTestHarness>
{
    private readonly DbTestHarness _harness;

    public AssetQueryIntegrationTests(DbTestHarness harness) => _harness = harness;

    private static async Task<AimsDbContext> CreateContextAsync(string cs)
    {
        var ctx = MigrateDb.CreateContext(cs);
        // Start each test from a clean state (harness already does this per test class);
        // if you need per-test isolation, you can also call the harness.Reset* via public method.
        await Task.CompletedTask;
        return ctx;
    }

    private static async Task SeedBasicAsync(AimsDbContext db)
    {
        db.HardwareAssets.AddRange(
            new Hardware { AssetName = "ThinkPad T14", AssetType = "Laptop", SerialNumber = "SN-AAA-001" },
            new Hardware { AssetName = "MacBook Pro", AssetType = "Laptop", SerialNumber = "MBP-XYZ-999" },
            new Hardware { AssetName = "Dell U2720Q", AssetType = "Monitor", SerialNumber = "MON-123456" }
        );

        db.SoftwareAssets.AddRange(
            new Software { SoftwareName = "Visual Studio", SoftwareType = "IDE", SoftwareLicenseKey = "VS-123-KEY" },
            new Software { SoftwareName = "Slack", SoftwareType = "Collaboration", SoftwareLicenseKey = "SLK-KEY-777" },
            new Software { SoftwareName = "Adobe Photoshop", SoftwareType = "Graphics", SoftwareLicenseKey = "PH-ABC-123" },
            new Software { SoftwareName = "thinktool", SoftwareType = "Utility", SoftwareLicenseKey = "SN-AAA-001" } // same tag as hardware to test scoring
        );

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Unique_ReturnsDistinctSorted_TypesAcrossHardwareAndSoftware()
    {
        await using var db = await CreateContextAsync(_harness.ConnectionString);
        await SeedBasicAsync(db);
        var sut = new AssetQuery(db);

        var list = await sut.unique();

        // Expect union of: Laptop, Monitor, Mouse, IDE, Collaboration, Graphics, Utility
        Assert.Contains("Laptop", list);
        Assert.Contains("Monitor", list);
        Assert.Contains("IDE", list);
        Assert.Contains("Collaboration", list);
        Assert.Contains("Graphics", list);
        Assert.Contains("Utility", list);

        var ordered = list.OrderBy(x => x).ToList();
        Assert.Equal(ordered, list); // sorted
        Assert.Equal(list.Count, list.Distinct().Count()); // distinct
    }
}
