using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupDumps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "Excluded",
                table: "BackupVolumeOverrides",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "BackupVolumeOverrides",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DumpEngine",
                table: "BackupSnapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "BackupJobs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Method",
                table: "BackupVolumeOverrides");

            migrationBuilder.DropColumn(
                name: "DumpEngine",
                table: "BackupSnapshots");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "BackupJobs");

            migrationBuilder.AlterColumn<bool>(
                name: "Excluded",
                table: "BackupVolumeOverrides",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
