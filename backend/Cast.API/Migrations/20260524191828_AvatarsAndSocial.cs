using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cast.API.Migrations
{
    /// <inheritdoc />
    public partial class AvatarsAndSocial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ManualStatus",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FollowerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FriendLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddresseeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FollowerId_StreamerId",
                table: "Follows",
                columns: new[] { "FollowerId", "StreamerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Follows_StreamerId",
                table: "Follows",
                column: "StreamerId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendLinks_AddresseeId",
                table: "FriendLinks",
                column: "AddresseeId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendLinks_RequesterId_AddresseeId",
                table: "FriendLinks",
                columns: new[] { "RequesterId", "AddresseeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "FriendLinks");

            migrationBuilder.DropColumn(
                name: "ManualStatus",
                table: "AspNetUsers");
        }
    }
}
