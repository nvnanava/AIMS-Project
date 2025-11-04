using AIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AIMS.UnitTests;

public sealed class TestAimsDbContext : AimsDbContext
{
    public TestAimsDbContext(DbContextOptions<AimsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Strip SQL Server-specific column types that break SQLite (e.g., nvarchar(max), varbinary(max))
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                var ct = prop.GetColumnType();
                if (ct is null) continue;

                // any type with "(max)" or explicit nvarchar/varchar/varbinary with non-numeric length
                if (ct.Contains("(max)", StringComparison.OrdinalIgnoreCase) ||
                    ct.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ||
                    ct.StartsWith("varchar", StringComparison.OrdinalIgnoreCase) ||
                    ct.StartsWith("varbinary", StringComparison.OrdinalIgnoreCase))
                {
                    // Let SQLite provider choose the right affinity instead
                    prop.SetColumnType(null);
                }
            }
        }
    }
}
