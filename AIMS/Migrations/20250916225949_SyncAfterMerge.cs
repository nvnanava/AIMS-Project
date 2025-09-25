using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations
{
    /// <inheritdoc />
    public partial class SyncAfterMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs",
                sql: "\n                    (\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\n                    )");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments",
                sql: "\n                    (\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\n                    )");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs",
                sql: "\r\n                    (\r\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\r\n                        OR\r\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\r\n                    )");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments",
                sql: "\r\n                    (\r\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\r\n                        OR\r\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\r\n                    )");
        }
    }
}
