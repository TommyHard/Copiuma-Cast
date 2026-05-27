using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cast.API.Migrations
{
    /// <inheritdoc />
    public partial class AddedGameManifests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InteractionsJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModArchiveUrl",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModManifestJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Games",
                keyColumn: "Id",
                keyValue: new Guid("0a0c1d2e-3f40-5152-6364-757687980000"),
                columns: new[] { "InteractionsJson", "ModArchiveUrl", "ModManifestJson" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InteractionsJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ModArchiveUrl",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ModManifestJson",
                table: "Games");
        }
    }
}
