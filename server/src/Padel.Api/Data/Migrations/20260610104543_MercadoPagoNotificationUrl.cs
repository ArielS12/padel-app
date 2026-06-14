using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Padel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MercadoPagoNotificationUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationUrl",
                table: "MercadoPagoSettings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationUrl",
                table: "MercadoPagoSettings");
        }
    }
}
