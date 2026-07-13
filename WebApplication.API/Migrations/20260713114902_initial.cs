using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WebApplication.API.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatSessionStates",
                columns: table => new
                {
                    ConversationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MessagesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessionStates", x => x.ConversationId);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Category", "Description", "Name", "Price", "Stock" },
                values: new object[,]
                {
                    { 1, "Electronics", "High performance laptop", "Laptop X", 1500.00m, 50 },
                    { 2, "Electronics", "Latest smartphone with 5G", "Smartphone Y", 999.99m, 100 },
                    { 3, "Home", "Automatic programmable coffee maker", "Coffee Maker", 80.00m, 30 },
                    { 4, "Home", "Ergonomic mesh office chair", "Office Chair", 120.50m, 20 },
                    { 5, "Clothing", "Comfortable running shoes", "Running Shoes", 65.00m, 75 },
                    { 6, "Clothing", "Cotton plain t-shirt", "T-Shirt", 15.00m, 200 },
                    { 7, "Books", "Bestselling sci-fi novel", "Science Fiction Book", 12.99m, 150 },
                    { 8, "Electronics", "Noise-cancelling over-ear headphones", "Headphones", 250.00m, 40 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatSessionStates");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
