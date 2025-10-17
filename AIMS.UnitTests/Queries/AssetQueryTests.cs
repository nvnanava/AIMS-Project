using AIMS.Queries;

namespace AIMS.UnitTests.Queries.AssetQuerySpecs;

public sealed class AssetQueryIntegrationTests
{
    [Fact]
    public async Task Unique_ReturnsDistinctSorted_TypesAcrossHardwareAndSoftware()
    {
        var (db, conn) = await Db.CreateContextAsync();
        await using (conn)
        await using (db)
        {
            await Db.SeedAsync(db);
            var assetQuery = new AssetQuery(db);

            var list = await assetQuery.unique();

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
}
