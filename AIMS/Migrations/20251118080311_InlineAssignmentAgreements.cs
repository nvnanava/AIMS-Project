using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class InlineAssignmentAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgreementContentType",
                schema: "dbo",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "AgreementFile",
                schema: "dbo",
                table: "Assignments",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgreementFileName",
                schema: "dbo",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgreementContentType",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AgreementFile",
                schema: "dbo",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AgreementFileName",
                schema: "dbo",
                table: "Assignments");
        }
    }
}
