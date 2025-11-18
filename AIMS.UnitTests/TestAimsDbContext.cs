using AIMS.Data;
using AIMS.Models;
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

        // Update the AuditLog constraint to allow User actions (AssetKind = 3)
        modelBuilder.Entity<AIMS.Models.AuditLog>()
            .ToTable(tb => tb.HasCheckConstraint(
                "CK_AuditLog_ExactlyOneAsset",
                @"
                (
                    ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)
                    OR
                    ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)
                    OR
                    ([AssetKind] = 3 AND [HardwareID] IS NULL AND [SoftwareID] IS NULL)
                )"
            ));
    }
}
