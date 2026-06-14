using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Padel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentSplitAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdminFeeAmount",
                table: "Payments",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnerAmount",
                table: "Payments",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProcessingReserveAmount",
                table: "Payments",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminFeeAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OwnerAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProcessingReserveAmount",
                table: "Payments");
        }
    }
}
