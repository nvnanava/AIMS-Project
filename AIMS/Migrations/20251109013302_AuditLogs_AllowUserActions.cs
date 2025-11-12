using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class AuditLogs_AllowUserActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing constraint (if present)
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.AuditLogs')
      AND name = 'CK_AuditLog_ExactlyOneAsset'
)
    ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_AuditLog_ExactlyOneAsset;
");

            // Recreate it to ALSO allow user-only actions: AssetKind = 3
            migrationBuilder.Sql(@"
ALTER TABLE dbo.AuditLogs WITH NOCHECK ADD CONSTRAINT CK_AuditLog_ExactlyOneAsset
CHECK (
    ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)
 OR ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)
 OR ([AssetKind] = 3 AND [HardwareID] IS NULL AND [SoftwareID] IS NULL)
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the previous form (hardware OR software only)
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.AuditLogs')
      AND name = 'CK_AuditLog_ExactlyOneAsset'
)
    ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_AuditLog_ExactlyOneAsset;

ALTER TABLE dbo.AuditLogs WITH NOCHECK ADD CONSTRAINT CK_AuditLog_ExactlyOneAsset
CHECK (
    ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)
 OR ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)
);
");
        }
    }
}
