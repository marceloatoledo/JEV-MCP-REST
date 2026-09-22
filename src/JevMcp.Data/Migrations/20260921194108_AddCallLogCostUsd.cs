using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JevMcp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCallLogCostUsd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostUsd",
                table: "CallLogs",
                type: "TEXT",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostUsd",
                table: "CallLogs");
        }
    }
}
