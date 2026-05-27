using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cast.API.Migrations
{
    /// <inheritdoc />
    public partial class FiltersMediaCostEventToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CostCoins",
                table: "MediaItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "RoomEventToggles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomEventToggles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StreamerFilterSettings",
                columns: table => new
                {
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamerFilterSettings", x => x.StreamerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomEventToggles_RoomId_EventId",
                table: "RoomEventToggles",
                columns: new[] { "RoomId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomEventToggles");

            migrationBuilder.DropTable(
                name: "StreamerFilterSettings");

            migrationBuilder.DropColumn(
                name: "CostCoins",
                table: "MediaItems");
        }
    }
}
