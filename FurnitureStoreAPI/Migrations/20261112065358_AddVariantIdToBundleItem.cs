using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureStoreAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantIdToBundleItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "BundleItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "BundleItems");
        }
    }
}
