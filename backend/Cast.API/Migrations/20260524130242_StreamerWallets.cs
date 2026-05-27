using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cast.API.Migrations
{
    /// <inheritdoc />
    public partial class StreamerWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StreamerId",
                table: "CoinTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "StreamerWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Coins = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamerWallets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StreamerWallets_UserId_StreamerId",
                table: "StreamerWallets",
                columns: new[] { "UserId", "StreamerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StreamerWallets");

            migrationBuilder.DropColumn(
                name: "StreamerId",
                table: "CoinTransactions");
        }
    }
}
