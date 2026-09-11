using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// L23 (Catalog audit Stage 10). Caps <c>Products.Description</c> and
    /// <c>Categories.Description</c> at 1000 characters, the limit the Category validators already
    /// enforced — so schema and validator agree the way they do for Name and Sku.
    /// </summary>
    public partial class CapDescriptionLengths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both columns were unbounded `text`, and Products.Description had no validator rule at
            // all, so a longer value may already exist. Postgres would reject the ALTER with
            // "value too long for type character varying(1000)", naming no row. This pre-step fails
            // first and lists every offending row in both tables at once.
            //
            // It deliberately does NOT truncate. A description is customer-facing copy; cutting it
            // mid-sentence is an editorial decision, not something a schema migration should make
            // silently. Same fail-loudly stance as RestoreProductSkuAndNameIndexes.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    offenders text;
                    offender_count integer;
                BEGIN
                    SELECT string_agg(o.tbl || ' ' || o.id || ' (' || o.len || ' chars)', ', ' ORDER BY o.tbl, o.id),
                           count(*)
                    INTO offenders, offender_count
                    FROM (
                        SELECT 'Products' AS tbl, ""Id""::text AS id, char_length(""Description"") AS len
                        FROM ""Products"" WHERE char_length(""Description"") > 1000
                        UNION ALL
                        SELECT 'Categories', ""Id""::text, char_length(""Description"")
                        FROM ""Categories"" WHERE char_length(""Description"") > 1000
                    ) o;

                    IF offender_count > 0 THEN
                        RAISE EXCEPTION
                            'Cannot cap Description at 1000 characters: % row(s) are longer: %.',
                            offender_count, offenders
                        USING HINT =
                            'Shorten each listed description to 1000 characters or fewer (soft-deleted rows included — the column constraint applies to every row), then re-run this migration.';
                    END IF;
                END $$;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Categories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Products",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
