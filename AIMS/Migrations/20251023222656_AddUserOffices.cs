using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class AddUserOffices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_Offices_OfficeID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Offices_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_OfficeID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "OfficeID",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.RenameColumn(
                name: "GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports",
                newName: "GeneratedForOfficeID");

            migrationBuilder.RenameIndex(
                name: "IX_Reports_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports",
                newName: "IX_Reports_GeneratedForOfficeID");

            migrationBuilder.AddColumn<int>(
                name: "OfficeID",
                schema: "dbo",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_OfficeID",
                schema: "dbo",
                table: "Users",
                column: "OfficeID");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Offices_GeneratedForOfficeID",
                schema: "dbo",
                table: "Reports",
                column: "GeneratedForOfficeID",
                principalSchema: "dbo",
                principalTable: "Offices",
                principalColumn: "OfficeID",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Offices_OfficeID",
                schema: "dbo",
                table: "Users",
                column: "OfficeID",
                principalSchema: "dbo",
                principalTable: "Offices",
                principalColumn: "OfficeID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Offices_GeneratedForOfficeID",
                schema: "dbo",
                table: "Reports");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Offices_OfficeID",
                schema: "dbo",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_OfficeID",
                schema: "dbo",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OfficeID",
                schema: "dbo",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "GeneratedForOfficeID",
                schema: "dbo",
                table: "Reports",
                newName: "GeneratedByOfficeID");

            migrationBuilder.RenameIndex(
                name: "IX_Reports_GeneratedForOfficeID",
                schema: "dbo",
                table: "Reports",
                newName: "IX_Reports_GeneratedByOfficeID");

            migrationBuilder.AddColumn<int>(
                name: "OfficeID",
                schema: "dbo",
                table: "Assignments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_OfficeID",
                schema: "dbo",
                table: "Assignments",
                column: "OfficeID");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_Offices_OfficeID",
                schema: "dbo",
                table: "Assignments",
                column: "OfficeID",
                principalSchema: "dbo",
                principalTable: "Offices",
                principalColumn: "OfficeID");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Offices_GeneratedByOfficeID",
                schema: "dbo",
                table: "Reports",
                column: "GeneratedByOfficeID",
                principalSchema: "dbo",
                principalTable: "Offices",
                principalColumn: "OfficeID",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
