using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Padel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MercadoPagoOAuthLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OAuthClientId",
                table: "MercadoPagoSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientSecret",
                table: "MercadoPagoSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthRedirectUrl",
                table: "MercadoPagoSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MercadoPagoLinkedAtUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoRefreshToken",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MercadoPagoTokenExpiresAtUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoUserId",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MercadoPagoOAuthStates",
                columns: table => new
                {
                    State = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoPagoOAuthStates", x => x.State);
                    table.ForeignKey(
                        name: "FK_MercadoPagoOAuthStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MercadoPagoOAuthStates_UserId_CreatedAtUtc",
                table: "MercadoPagoOAuthStates",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MercadoPagoOAuthStates");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "MercadoPagoSettings");

            migrationBuilder.DropColumn(
                name: "OAuthClientSecret",
                table: "MercadoPagoSettings");

            migrationBuilder.DropColumn(
                name: "OAuthRedirectUrl",
                table: "MercadoPagoSettings");

            migrationBuilder.DropColumn(
                name: "MercadoPagoLinkedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MercadoPagoRefreshToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MercadoPagoTokenExpiresAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MercadoPagoUserId",
                table: "AspNetUsers");
        }
    }
}
