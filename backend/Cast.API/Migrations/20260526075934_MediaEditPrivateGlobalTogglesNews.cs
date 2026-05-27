using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cast.API.Migrations
{
    /// <inheritdoc />
    public partial class MediaEditPrivateGlobalTogglesNews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WebmUrl",
                table: "MediaItems",
                newName: "WebmKey");

            migrationBuilder.RenameColumn(
                name: "OggUrl",
                table: "MediaItems",
                newName: "OggKey");

            migrationBuilder.AddColumn<int>(
                name: "ClipEndMs",
                table: "MediaItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClipStartMs",
                table: "MediaItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosXPct",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PosYPct",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScalePct",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GameEventOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameEventOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "News",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_News", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameEventOverrides_GameId_EventId",
                table: "GameEventOverrides",
                columns: new[] { "GameId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_News_Published_CreatedAt",
                table: "News",
                columns: new[] { "Published", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameEventOverrides");

            migrationBuilder.DropTable(
                name: "News");

            migrationBuilder.DropColumn(
                name: "ClipEndMs",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "ClipStartMs",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "PosXPct",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "PosYPct",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "ScalePct",
                table: "MediaItems");

            migrationBuilder.RenameColumn(
                name: "WebmKey",
                table: "MediaItems",
                newName: "WebmUrl");

            migrationBuilder.RenameColumn(
                name: "OggKey",
                table: "MediaItems",
                newName: "OggUrl");
        }
    }
}
