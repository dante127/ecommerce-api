using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrigramSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_CategoryId_Price",
                table: "Products");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_Price",
                table: "Products",
                columns: new[] { "CategoryId", "Price" },
                filter: "\"IsDeleted\" = false AND \"IsActive\" = true");

            // Functional indexes that EF has no fluent API for. The catalogue search is
            // lower(column) LIKE pattern, and a leading-wildcard LIKE can be served only by a
            // trigram index built over that same expression.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Products_Name_Trgm\" ON \"Products\" USING gin (lower(\"Name\") gin_trgm_ops);");

            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Products_Description_Trgm\" ON \"Products\" USING gin (lower(\"Description\") gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_CategoryId_Price",
                table: "Products");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Products_Name_Trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Products_Description_Trgm\";");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_Price",
                table: "Products",
                columns: new[] { "CategoryId", "Price" },
                filter: "\"IsDeleted\" = false");
        }
    }
}
