using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupJobLogSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LogSessionId",
                table: "BackupJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_LogSessionId",
                table: "BackupJobs",
                column: "LogSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BackupJobs_LogSessionId",
                table: "BackupJobs");

            migrationBuilder.DropColumn(
                name: "LogSessionId",
                table: "BackupJobs");
        }
    }
}
