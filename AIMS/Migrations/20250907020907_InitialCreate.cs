using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HardwareAssets",
                columns: table => new
                {
                    HardwareID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssetType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WarrantyExpiration = table.Column<DateOnly>(type: "date", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HardwareAssets", x => x.HardwareID);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    ReportID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.ReportID);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleID);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareAssets",
                columns: table => new
                {
                    SoftwareID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SoftwareName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoftwareType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoftwareVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoftwareLicenseKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SoftwareLicenseExpiration = table.Column<DateOnly>(type: "date", nullable: true),
                    SoftwareUsageData = table.Column<long>(type: "bigint", nullable: false),
                    SoftwareCost = table.Column<decimal>(type: "decimal(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareAssets", x => x.SoftwareID);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GraphObjectID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RoleID = table.Column<int>(type: "int", nullable: false),
                    SupervisorID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserID);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleID",
                        column: x => x.RoleID,
                        principalTable: "Roles",
                        principalColumn: "RoleID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Users_SupervisorID",
                        column: x => x.SupervisorID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    AssignmentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    AssetKind = table.Column<int>(type: "int", nullable: false),
                    AssetTag = table.Column<int>(type: "int", nullable: true),
                    SoftwareID = table.Column<int>(type: "int", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UnassignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.AssignmentID);
                    table.CheckConstraint("CK_Assignment_ExactlyOneAsset", "\n                    (\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\n                    )");
                    table.ForeignKey(
                        name: "FK_Assignments_HardwareAssets_AssetTag",
                        column: x => x.AssetTag,
                        principalTable: "HardwareAssets",
                        principalColumn: "HardwareID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assignments_SoftwareAssets_SoftwareID",
                        column: x => x.SoftwareID,
                        principalTable: "SoftwareAssets",
                        principalColumn: "SoftwareID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assignments_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssetKind = table.Column<int>(type: "int", nullable: false),
                    AssetTag = table.Column<int>(type: "int", nullable: true),
                    SoftwareID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogID);
                    table.CheckConstraint("CK_AuditLog_ExactlyOneAsset", "\n                    (\n                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)\n                        OR\n                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)\n                    )");
                    table.ForeignKey(
                        name: "FK_AuditLogs_HardwareAssets_AssetTag",
                        column: x => x.AssetTag,
                        principalTable: "HardwareAssets",
                        principalColumn: "HardwareID");
                    table.ForeignKey(
                        name: "FK_AuditLogs_SoftwareAssets_SoftwareID",
                        column: x => x.SoftwareID,
                        principalTable: "SoftwareAssets",
                        principalColumn: "SoftwareID");
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackEntries",
                columns: table => new
                {
                    FeedbackID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedByUserID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackEntries", x => x.FeedbackID);
                    table.ForeignKey(
                        name: "FK_FeedbackEntries_Users_SubmittedByUserID",
                        column: x => x.SubmittedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AssetTag_UnassignedAtUtc",
                table: "Assignments",
                columns: new[] { "AssetTag", "UnassignedAtUtc" },
                unique: true,
                filter: "[AssetTag] IS NOT NULL AND [UnassignedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_SoftwareID_UnassignedAtUtc",
                table: "Assignments",
                columns: new[] { "SoftwareID", "UnassignedAtUtc" },
                unique: true,
                filter: "[SoftwareID] IS NOT NULL AND [UnassignedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_UserID",
                table: "Assignments",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AssetTag",
                table: "AuditLogs",
                column: "AssetTag");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ExternalId",
                table: "AuditLogs",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_SoftwareID",
                table: "AuditLogs",
                column: "SoftwareID");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserID",
                table: "AuditLogs",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackEntries_SubmittedByUserID",
                table: "FeedbackEntries",
                column: "SubmittedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_HardwareAssets_SerialNumber",
                table: "HardwareAssets",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ExternalId",
                table: "Reports",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareAssets_SoftwareLicenseKey",
                table: "SoftwareAssets",
                column: "SoftwareLicenseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ExternalId",
                table: "Users",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleID",
                table: "Users",
                column: "RoleID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SupervisorID",
                table: "Users",
                column: "SupervisorID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "FeedbackEntries");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "HardwareAssets");

            migrationBuilder.DropTable(
                name: "SoftwareAssets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
