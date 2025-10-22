using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class AddIsArchivedToEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "dbo",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "dbo",
                table: "HardwareAssets",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "dbo",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "dbo",
                table: "SoftwareAssets");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "dbo",
                table: "HardwareAssets");
        }
    }
}
