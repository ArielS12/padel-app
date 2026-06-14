using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Padel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerPaymentAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_MatchId",
                table: "Payments");

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizationExpiresAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapturedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAuthorizedPaymentId",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MatchId_UserId",
                table: "Payments",
                columns: new[] { "MatchId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_MatchId_UserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AuthorizationExpiresAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AuthorizedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CapturedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderAuthorizedPaymentId",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MatchId",
                table: "Payments",
                column: "MatchId");
        }
    }
}
