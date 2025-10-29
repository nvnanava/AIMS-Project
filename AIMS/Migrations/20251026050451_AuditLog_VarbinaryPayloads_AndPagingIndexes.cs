using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class AuditLog_VarbinaryPayloads_AndPagingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserID",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.RenameColumn(
                name: "SnapshotJson",
                schema: "dbo",
                table: "AuditLogs",
                newName: "SnapshotContentType");

            migrationBuilder.RenameColumn(
                name: "BlobUri",
                schema: "dbo",
                table: "AuditLogs",
                newName: "AttachmentContentType");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                schema: "dbo",
                table: "AuditLogs",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<byte[]>(
                name: "AttachmentBytes",
                schema: "dbo",
                table: "AuditLogs",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SnapshotBytes",
                schema: "dbo",
                table: "AuditLogs",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Field",
                schema: "dbo",
                table: "AuditLogChanges",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AssetKind_HardwareID",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "AssetKind", "HardwareID" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AssetKind_SoftwareID",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "AssetKind", "SoftwareID" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TimestampUtc",
                schema: "dbo",
                table: "AuditLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserID_Action",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "UserID", "Action" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_AssetKind_HardwareID",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_AssetKind_SoftwareID",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TimestampUtc",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserID_Action",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "AttachmentBytes",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SnapshotBytes",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.RenameColumn(
                name: "SnapshotContentType",
                schema: "dbo",
                table: "AuditLogs",
                newName: "SnapshotJson");

            migrationBuilder.RenameColumn(
                name: "AttachmentContentType",
                schema: "dbo",
                table: "AuditLogs",
                newName: "BlobUri");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                schema: "dbo",
                table: "AuditLogs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Field",
                schema: "dbo",
                table: "AuditLogChanges",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserID",
                schema: "dbo",
                table: "AuditLogs",
                column: "UserID");
        }
    }
}
