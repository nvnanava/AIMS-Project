using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations._local_sync
{
    /// <inheritdoc />
    public partial class AddSoftwareRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "dbo",
                table: "SoftwareAssets",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "dbo",
                table: "SoftwareAssets");
        }
    }
}
