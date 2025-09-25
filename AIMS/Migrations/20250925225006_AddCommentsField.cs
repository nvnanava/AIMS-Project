using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentsField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                schema: "dbo",
                table: "HardwareAssets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                schema: "dbo",
                table: "SoftwareAssets");

            migrationBuilder.DropColumn(
                name: "Comment",
                schema: "dbo",
                table: "HardwareAssets");
        }
    }
}
