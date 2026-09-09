using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// Restores the two B-tree indexes on Products that were silently lost.
    ///
    /// <para>
    /// Two <c>HasIndex</c> calls over the same property configure the same EF index, so the trigram
    /// declarations in <c>OnModelCreating</c> had been overwriting the unique index on
    /// <c>Sku</c> and the plain index on <c>Name</c> since InitialCreate. Nothing warned: for five
    /// migrations the unique constraint simply did not exist in any environment, and the default
    /// <c>ORDER BY Name</c> of <c>GET /api/v1/products</c> had no usable index.
    /// </para>
    ///
    /// <para>
    /// The Sku index is partial (<c>WHERE NOT "IsDeleted"</c>) so a soft-deleted product's SKU
    /// becomes reusable, matching the global query filter the application's own duplicate check
    /// already runs under.
    /// </para>
    /// </summary>
    public partial class RestoreProductSkuAndNameIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Because the constraint has never existed in ANY environment, live duplicate SKUs may
            // already be present — and CREATE UNIQUE INDEX simply aborts on the first one it meets
            // ('could not create unique index ... Key ("Sku")=(...) is duplicated'), naming one
            // value and leaving the operator to re-run the migration once per duplicate to discover
            // the rest. This pre-step fails first, with every offending SKU listed at once.
            //
            // It deliberately does NOT repair anything. A SKU is a customer-facing identifier;
            // picking a winner and re-keying the losers is a business decision, not something a
            // schema migration should make silently. Soft-deleted rows are excluded here for the
            // same reason the index excludes them — they cannot collide.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    duplicate_skus text;
                    duplicate_count integer;
                BEGIN
                    SELECT string_agg(d.""Sku"" || ' (' || d.live_rows || ' live rows)', ', ' ORDER BY d.""Sku""),
                           count(*)
                    INTO duplicate_skus, duplicate_count
                    FROM (
                        SELECT ""Sku"", count(*) AS live_rows
                        FROM ""Products""
                        WHERE NOT ""IsDeleted""
                        GROUP BY ""Sku""
                        HAVING count(*) > 1
                    ) d;

                    IF duplicate_count > 0 THEN
                        RAISE EXCEPTION
                            'Cannot create the unique index IX_Products_Sku: % SKU(s) are already held by more than one live product: %.',
                            duplicate_count, duplicate_skus
                        USING HINT =
                            'Products.Sku has never been unique, so existing data may violate it. Resolve each SKU by soft-deleting or re-keying all but one live product, then re-run this migration. Soft-deleted products are exempt from the index by design.';
                    END IF;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                table: "Products");
        }
    }
}
