using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NbTcgTrader.Api.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRowForSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsForSale",
                table: "ImportRows",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsForSale",
                table: "ImportRows");
        }
    }
}
