using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityEnvVars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityEnvVars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityEnvVars", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityEnvVars_Name",
                table: "IdentityEnvVars",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityEnvVars");
        }
    }
}
