using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cast.API.Migrations
{
    /// <inheritdoc />
    public partial class ToleranceAndAnalyticsAndPresenceAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MediaId",
                table: "EventLog",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StreamerTagFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamerTagFilters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StreamerTagFilters_StreamerId_Tag",
                table: "StreamerTagFilters",
                columns: new[] { "StreamerId", "Tag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StreamerTagFilters");

            migrationBuilder.DropColumn(
                name: "MediaId",
                table: "EventLog");
        }
    }
}
