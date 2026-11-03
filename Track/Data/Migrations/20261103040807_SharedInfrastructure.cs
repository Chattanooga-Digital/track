using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "BillingSamples",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SharedStacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StackName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedStacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedStacks_StackName",
                table: "SharedStacks",
                column: "StackName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedStacks");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "BillingSamples");
        }
    }
}
