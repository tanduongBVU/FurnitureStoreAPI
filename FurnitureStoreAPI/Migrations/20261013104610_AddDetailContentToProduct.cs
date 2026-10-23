using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureStoreAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailContentToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailContent",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailContent",
                table: "Products");
        }
    }
}
