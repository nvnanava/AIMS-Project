using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class Assignments_MultiSeatSoftware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assignments_SoftwareID_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_SoftwareID_UserID_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments",
                columns: new[] { "SoftwareID", "UserID", "UnassignedAtUtc" },
                unique: true,
                filter: "[SoftwareID] IS NOT NULL AND [UserID] IS NOT NULL AND [UnassignedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assignments_SoftwareID_UserID_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_SoftwareID_UnassignedAtUtc",
                schema: "dbo",
                table: "Assignments",
                columns: new[] { "SoftwareID", "UnassignedAtUtc" },
                unique: true,
                filter: "[SoftwareID] IS NOT NULL AND [UnassignedAtUtc] IS NULL");
        }
    }
}
