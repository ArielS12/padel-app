using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Padel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CourtManagementUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FloorType",
                table: "Courts",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "FullMatchPrice",
                table: "Courts",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsCovered",
                table: "Courts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WallType",
                table: "Courts",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoAccessToken",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoAccountEmail",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoPublicKey",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourtSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    SlotMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtSchedules_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtSchedules_CourtId_DayOfWeek_OpensAt_ClosesAt",
                table: "CourtSchedules",
                columns: new[] { "CourtId", "DayOfWeek", "OpensAt", "ClosesAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtSchedules");

            migrationBuilder.DropColumn(
                name: "FloorType",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "FullMatchPrice",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "IsCovered",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "WallType",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "MercadoPagoAccessToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MercadoPagoAccountEmail",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MercadoPagoPublicKey",
                table: "AspNetUsers");
        }
    }
}
