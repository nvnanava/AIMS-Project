using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixAuditLogModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignmentID",
                schema: "dbo",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AssignmentID",
                schema: "dbo",
                table: "AuditLogs",
                column: "AssignmentID");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Assignments_AssignmentID",
                schema: "dbo",
                table: "AuditLogs",
                column: "AssignmentID",
                principalSchema: "dbo",
                principalTable: "Assignments",
                principalColumn: "AssignmentID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Assignments_AssignmentID",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_AssignmentID",
                schema: "dbo",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "AssignmentID",
                schema: "dbo",
                table: "AuditLogs");
        }
    }
}
