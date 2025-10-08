using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class SchemaV2_Refactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_HardwareAssets_AssetTag",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_HardwareAssets_AssetTag",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_SoftwareAssets_SoftwareLicenseKey",
                schema: "dbo",
                table: "SoftwareAssets");

            migrationBuilder.DropIndex(
                name: "IX_HardwareAssets_SerialNumber",
                schema: "dbo",
                table: "HardwareAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_AssetTag_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.RenameColumn(
                name: "PreviousValue",
                schema: "dbo",
                table: "AuditLogs",
                newName: "SnapshotJson");

            migrationBuilder.RenameColumn(
                name: "NewValue",
                schema: "dbo",
                table: "AuditLogs",
                newName: "BlobUri");

            migrationBuilder.RenameColumn(
                name: "AssetTag",
                schema: "dbo",
                table: "AuditLogs",
                newName: "HardwareID");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_AssetTag",
                schema: "dbo",
                table: "AuditLogs",
                newName: "IX_AuditLogs_HardwareID");

            migrationBuilder.RenameColumn(
                name: "AssetTag",
                schema: "dbo",
                table: "Assignments",
                newName: "OfficeID");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                schema: "dbo",
                table: "Users",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeNumber",
                schema: "dbo",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "dbo",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareVersion",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareType",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareName",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareLicenseKey",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<int>(
                name: "LicenseSeatsUsed",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LicenseTotalSeats",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "RoleName",
                schema: "dbo",
                table: "Roles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "dbo",
                table: "Roles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "dbo",
                table: "Reports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "dbo",
                table: "Reports",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "BlobUri",
                schema: "dbo",
                table: "Reports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeneratedByUserID",
                schema: "dbo",
                table: "Reports",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SerialNumber",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Model",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Manufacturer",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AssetType",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AssetTag",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AssetName",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<int>(
                name: "UserID",
                schema: "dbo",
                table: "Assignments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "HardwareID",
                schema: "dbo",
                table: "Assignments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agreements",
                schema: "dbo",
                columns: table => new
                {
                    AgreementID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HardwareID = table.Column<int>(type: "int", nullable: true),
                    SoftwareID = table.Column<int>(type: "int", nullable: true),
                    AssetKind = table.Column<int>(type: "int", nullable: false),
                    FileUri = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agreements", x => x.AgreementID);
                    table.CheckConstraint("CK_Agreement_ExactlyOneAsset", "\n                    (\n                        ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)\n                    )");
                    table.ForeignKey(
                        name: "FK_Agreements_HardwareAssets_HardwareID",
                        column: x => x.HardwareID,
                        principalSchema: "dbo",
                        principalTable: "HardwareAssets",
                        principalColumn: "HardwareID");
                    table.ForeignKey(
                        name: "FK_Agreements_SoftwareAssets_SoftwareID",
                        column: x => x.SoftwareID,
                        principalSchema: "dbo",
                        principalTable: "SoftwareAssets",
                        principalColumn: "SoftwareID");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogChanges",
                schema: "dbo",
                columns: table => new
                {
                    AuditLogChangeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuditLogID = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogChanges", x => x.AuditLogChangeID);
                    table.ForeignKey(
                        name: "FK_AuditLogChanges_AuditLogs_AuditLogID",
                        column: x => x.AuditLogID,
                        principalSchema: "dbo",
                        principalTable: "AuditLogs",
                        principalColumn: "AuditLogID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Offices",
                schema: "dbo",
                columns: table => new
                {
                    OfficeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OfficeName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offices", x => x.OfficeID);
                });

            migrationBuilder.CreateTable(
                name: "Thresholds",
                schema: "dbo",
                columns: table => new
                {
                    ThresholdID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ThresholdValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Thresholds", x => x.ThresholdID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareAssets_SoftwareLicenseKey",
                schema: "dbo",
                table: "SoftwareAssets",
                column: "SoftwareLicenseKey",
                unique: true,
                filter: "[SoftwareLicenseKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports",
                column: "GeneratedByOfficeID");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_GeneratedByUserID",
                schema: "dbo",
                table: "Reports",
                column: "GeneratedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_HardwareAssets_SerialNumber",
                schema: "dbo",
                table: "HardwareAssets",
                column: "SerialNumber",
                unique: true,
                filter: "[SerialNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs",
                sql: "\n                    (\n                        ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)\n                    )");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_HardwareID_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments",
                columns: new[] { "HardwareID", "UnassignedAtUtc" },
                unique: true,
                filter: "[HardwareID] IS NOT NULL AND [UnassignedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_OfficeID",
                schema: "dbo",
                table: "Assignments",
                column: "OfficeID");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments",
                sql: "\n                    (\n                        ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)\n                    )");

            migrationBuilder.CreateIndex(
                name: "IX_Agreements_HardwareID",
                schema: "dbo",
                table: "Agreements",
                column: "HardwareID");

            migrationBuilder.CreateIndex(
                name: "IX_Agreements_SoftwareID",
                schema: "dbo",
                table: "Agreements",
                column: "SoftwareID");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogChanges_AuditLogID_Field",
                schema: "dbo",
                table: "AuditLogChanges",
                columns: new[] { "AuditLogID", "Field" });

            migrationBuilder.CreateIndex(
                name: "IX_Offices_OfficeName",
                schema: "dbo",
                table: "Offices",
                column: "OfficeName");

            migrationBuilder.CreateIndex(
                name: "IX_Thresholds_AssetType",
                schema: "dbo",
                table: "Thresholds",
                column: "AssetType");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_HardwareAssets_HardwareID",
                schema: "dbo",
                table: "Assignments",
                column: "HardwareID",
                principalSchema: "dbo",
                principalTable: "HardwareAssets",
                principalColumn: "HardwareID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_Offices_OfficeID",
                schema: "dbo",
                table: "Assignments",
                column: "OfficeID",
                principalSchema: "dbo",
                principalTable: "Offices",
                principalColumn: "OfficeID");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_HardwareAssets_HardwareID",
                schema: "dbo",
                table: "AuditLogs",
                column: "HardwareID",
                principalSchema: "dbo",
                principalTable: "HardwareAssets",
                principalColumn: "HardwareID");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Offices_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports",
                column: "GeneratedByOfficeID",
                principalSchema: "dbo",
                principalTable: "Offices",
                principalColumn: "OfficeID",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Users_GeneratedByUserID",
                schema: "dbo",
                table: "Reports",
                column: "GeneratedByUserID",
                principalSchema: "dbo",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_HardwareAssets_HardwareID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_Offices_OfficeID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_HardwareAssets_HardwareID",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Offices_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Users_GeneratedByUserID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropTable(
                name: "Agreements",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AuditLogChanges",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Offices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Thresholds",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_SoftwareAssets_SoftwareLicenseKey",
                schema: "dbo",
                table: "SoftwareAssets");

            migrationBuilder.DropIndex(
                name: "IX_Reports_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_GeneratedByUserID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_HardwareAssets_SerialNumber",
                schema: "dbo",
                table: "HardwareAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_HardwareID_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_OfficeID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "LicenseSeatsUsed",
                schema: "dbo",
                table: "SoftwareAssets");

            migrationBuilder.DropColumn(
                name: "LicenseTotalSeats",
                schema: "dbo",
                table: "SoftwareAssets");

            migrationBuilder.DropColumn(
                name: "BlobUri",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "GeneratedByUserID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "HardwareID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.RenameColumn(
                name: "SnapshotJson",
                schema: "dbo",
                table: "AuditLogs",
                newName: "PreviousValue");

            migrationBuilder.RenameColumn(
                name: "HardwareID",
                schema: "dbo",
                table: "AuditLogs",
                newName: "AssetTag");

            migrationBuilder.RenameColumn(
                name: "BlobUri",
                schema: "dbo",
                table: "AuditLogs",
                newName: "NewValue");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_HardwareID",
                schema: "dbo",
                table: "AuditLogs",
                newName: "IX_AuditLogs_AssetTag");

            migrationBuilder.RenameColumn(
                name: "OfficeID",
                schema: "dbo",
                table: "Assignments",
                newName: "AssetTag");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                schema: "dbo",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeNumber",
                schema: "dbo",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "dbo",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareVersion",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareType",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareName",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "SoftwareLicenseKey",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "RoleName",
                schema: "dbo",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "dbo",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "dbo",
                table: "Reports",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "dbo",
                table: "Reports",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "SerialNumber",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Model",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Manufacturer",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "AssetType",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "AssetTag",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "AssetName",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<int>(
                name: "UserID",
                schema: "dbo",
                table: "Assignments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareAssets_SoftwareLicenseKey",
                schema: "dbo",
                table: "SoftwareAssets",
                column: "SoftwareLicenseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HardwareAssets_SerialNumber",
                schema: "dbo",
                table: "HardwareAssets",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLog_ExactlyOneAsset",
                schema: "dbo",
                table: "AuditLogs",
                sql: "\n                    (\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\n                    )");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AssetTag_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments",
                columns: new[] { "AssetTag", "UnassignedAtUtc" },
                unique: true,
                filter: "[AssetTag] IS NOT NULL AND [UnassignedAtUtc] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment_ExactlyOneAsset",
                schema: "dbo",
                table: "Assignments",
                sql: "\n                    (\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\n                    )");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_HardwareAssets_AssetTag",
                schema: "dbo",
                table: "Assignments",
                column: "AssetTag",
                principalSchema: "dbo",
                principalTable: "HardwareAssets",
                principalColumn: "HardwareID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_HardwareAssets_AssetTag",
                schema: "dbo",
                table: "AuditLogs",
                column: "AssetTag",
                principalSchema: "dbo",
                principalTable: "HardwareAssets",
                principalColumn: "HardwareID");
        }
    }
}
