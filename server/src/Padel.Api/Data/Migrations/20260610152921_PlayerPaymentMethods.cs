using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Padel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MercadoPagoCustomerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MercadoPagoCardId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentMethodId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CardBrand = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    LastFourDigits = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPaymentMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerPaymentMethods_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerPaymentMethods_UserId",
                table: "PlayerPaymentMethods",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerPaymentMethods");
        }
    }
}
