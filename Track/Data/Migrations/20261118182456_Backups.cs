using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class Backups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BackupStorageBytes",
                table: "BillingSamples",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "BackupStorageRatePerGbMonth",
                table: "BillingRates",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AgentEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PendingTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentEnrollments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    VolumeKey = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    VolumeName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    NodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RepositoryKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StackName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ParentJobId = table.Column<long>(type: "INTEGER", nullable: true),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastProgressAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PercentDone = table.Column<double>(type: "REAL", nullable: true),
                    BytesDone = table.Column<long>(type: "INTEGER", nullable: true),
                    BytesTotal = table.Column<long>(type: "INTEGER", nullable: true),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BytesAdded = table.Column<long>(type: "INTEGER", nullable: true),
                    FilesNew = table.Column<long>(type: "INTEGER", nullable: true),
                    FilesChanged = table.Column<long>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    OutputTail = table.Column<string>(type: "TEXT", nullable: true),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsShared = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InitializedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    SizeUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastPrunedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastMaintenanceError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRepositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackupMinuteUtc = table.Column<int>(type: "INTEGER", nullable: false),
                    SpreadMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxJobsPerNode = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxJobsTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepDaily = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepWeekly = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepMonthly = table.Column<int>(type: "INTEGER", nullable: false),
                    PruneDay = table.Column<int>(type: "INTEGER", nullable: false),
                    PruneMinuteUtc = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    StaleAfterHours = table.Column<int>(type: "INTEGER", nullable: false),
                    HookTimeoutMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    HeartbeatUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RepositoryKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VolumeKey = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    VolumeName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StackName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Images = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Paths = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    BytesAdded = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    PreRestore = table.Column<bool>(type: "INTEGER", nullable: false),
                    JobId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Bucket = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Addressing = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentAccessKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AgentSecretKeyProtected = table.Column<string>(type: "TEXT", nullable: false),
                    MaintenanceAccessKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MaintenanceSecretKeyProtected = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupVolumeOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VolumeName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Excluded = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupVolumeOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollments_NodeName",
                table: "AgentEnrollments",
                column: "NodeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_ParentJobId",
                table: "BackupJobs",
                column: "ParentJobId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_QueuedAt",
                table: "BackupJobs",
                column: "QueuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_Status",
                table: "BackupJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_VolumeKey_QueuedAt",
                table: "BackupJobs",
                columns: new[] { "VolumeKey", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRepositories_Key",
                table: "BackupRepositories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupSnapshots_RepositoryKey_SnapshotId",
                table: "BackupSnapshots",
                columns: new[] { "RepositoryKey", "SnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupSnapshots_VolumeKey_Time",
                table: "BackupSnapshots",
                columns: new[] { "VolumeKey", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupSnapshots_VolumeName_Time",
                table: "BackupSnapshots",
                columns: new[] { "VolumeName", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupVolumeOverrides_VolumeName",
                table: "BackupVolumeOverrides",
                column: "VolumeName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentEnrollments");

            migrationBuilder.DropTable(
                name: "BackupJobs");

            migrationBuilder.DropTable(
                name: "BackupRepositories");

            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DropTable(
                name: "BackupSnapshots");

            migrationBuilder.DropTable(
                name: "BackupTargets");

            migrationBuilder.DropTable(
                name: "BackupVolumeOverrides");

            migrationBuilder.DropColumn(
                name: "BackupStorageBytes",
                table: "BillingSamples");

            migrationBuilder.DropColumn(
                name: "BackupStorageRatePerGbMonth",
                table: "BillingRates");
        }
    }
}
