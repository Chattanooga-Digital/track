using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Track.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBucketAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BucketAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BucketName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BucketAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BucketAssignments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BucketAssignments_CustomerId",
                table: "BucketAssignments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_BucketAssignments_SourceName_BucketName",
                table: "BucketAssignments",
                columns: new[] { "SourceName", "BucketName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BucketAssignments");
        }
    }
}
