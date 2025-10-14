using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations
{
    /// <inheritdoc />
    public partial class AddTagNumberToHardware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetTag",
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
                name: "AssetTag",
                schema: "dbo",
                table: "HardwareAssets");
        }
    }
}
