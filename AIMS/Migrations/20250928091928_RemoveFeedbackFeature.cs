using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIMS.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFeedbackFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackEntries",
                schema: "dbo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackEntries",
                schema: "dbo",
                columns: table => new
                {
                    FeedbackID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmittedByUserID = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmissionDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackEntries", x => x.FeedbackID);
                    table.ForeignKey(
                        name: "FK_FeedbackEntries_Users_SubmittedByUserID",
                        column: x => x.SubmittedByUserID,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackEntries_SubmittedByUserID",
                schema: "dbo",
                table: "FeedbackEntries",
                column: "SubmittedByUserID");
        }
    }
}
