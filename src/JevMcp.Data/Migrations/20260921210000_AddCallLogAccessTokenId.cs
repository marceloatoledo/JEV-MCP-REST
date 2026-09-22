using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JevMcp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCallLogAccessTokenId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccessTokenId",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_AccessTokenId",
                table: "CallLogs",
                column: "AccessTokenId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CallLogs_AccessTokenId",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "AccessTokenId",
                table: "CallLogs");
        }
    }
}
