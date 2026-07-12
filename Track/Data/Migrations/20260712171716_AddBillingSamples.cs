using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    MemoryUsedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DiskBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ObjectStorageBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CpuPercentOfCluster = table.Column<double>(type: "REAL", nullable: false),
                    MemoryPercentOfCluster = table.Column<double>(type: "REAL", nullable: false),
                    BlendedPercentOfCluster = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClusterCapacitySamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NodeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CpuCores = table.Column<long>(type: "INTEGER", nullable: false),
                    MemoryBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClusterCapacitySamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSamples_CustomerId_Timestamp",
                table: "BillingSamples",
                columns: new[] { "CustomerId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSamples_Timestamp",
                table: "BillingSamples",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ClusterCapacitySamples_Timestamp",
                table: "ClusterCapacitySamples",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingSamples");

            migrationBuilder.DropTable(
                name: "ClusterCapacitySamples");
        }
    }
}
