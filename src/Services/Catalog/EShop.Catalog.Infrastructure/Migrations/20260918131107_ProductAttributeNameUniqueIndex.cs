using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// M1 (Admin panel S3), closing risk A6. Adds the database backstop for
    /// "an attribute name is unique within a product", which until now was enforced only in memory
    /// by <c>Product.AddAttribute</c> — so two concurrent <c>POST /attributes</c> could both read a
    /// product without the name, both pass, and both insert.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The index is on <c>lower("Name")</c>, not on <c>"Name"</c>.</b> The domain's dedupe is
    /// <c>StringComparison.OrdinalIgnoreCase</c>, and a plain unique index is case-sensitive under
    /// Postgres' default collation — so it would happily accept the "Color"/"color" pair that
    /// <c>AddAttribute</c> refuses, leaving exactly the race this index exists to close half open.
    /// </para>
    /// <para>
    /// <b>It is written as raw SQL and is deliberately absent from the EF model.</b> EF Core cannot
    /// express an expression index, so <c>HasIndex</c> could only declare the case-sensitive form —
    /// which would be the wrong index and would also make the model disagree with the database. EF
    /// never drops an index it does not know about, so leaving it out is safe;
    /// <c>dotnet ef migrations has-pending-model-changes</c> stays clean precisely because nothing
    /// in <c>OnModelCreating</c> changed. <c>CatalogDbContext</c> carries a comment pointing here,
    /// since the index is otherwise invisible from the model.
    /// </para>
    /// <para>
    /// Consumers: <c>CatalogProblemDetailsExtensions.AddProductAttributeConflict()</c> matches this
    /// index name to answer a lost race with 409 <c>Product.AttributeConflict</c> instead of the
    /// generic <c>DuplicateResource</c>, and must stay registered before <c>AddEfDuplicateKey()</c>.
    /// </para>
    /// </remarks>
    public partial class ProductAttributeNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dedupe has only ever been in-memory, and only within one request's view of a product,
            // so duplicates may already be stored. CREATE UNIQUE INDEX would reject them naming a
            // single value ("Key (ProductId, lower(Name))=(..., color) is duplicated") with no
            // indication of how many others follow. This pre-step fails first and lists every
            // offending group at once.
            //
            // It deliberately does NOT delete or rename anything. Which of two attributes named
            // "Color" is the right one is an editorial decision about product data, not something a
            // schema migration should pick silently. Same fail-loudly stance as CapDescriptionLengths
            // and RestoreProductSkuAndNameIndexes.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    offenders text;
                    offender_count integer;
                BEGIN
                    SELECT string_agg(
                               o.""ProductId""::text || ' / ' || o.name || ' (x' || o.copies || ')',
                               ', ' ORDER BY o.""ProductId"", o.name),
                           count(*)
                    INTO offenders, offender_count
                    FROM (
                        SELECT ""ProductId"", lower(""Name"") AS name, count(*) AS copies
                        FROM ""ProductAttributes""
                        GROUP BY ""ProductId"", lower(""Name"")
                        HAVING count(*) > 1
                    ) o;

                    IF offender_count > 0 THEN
                        RAISE EXCEPTION
                            'Cannot make attribute names unique per product: % duplicated name(s): %.',
                            offender_count, offenders
                        USING HINT =
                            'Attribute names are compared case-insensitively, so ''Color'' and ''color'' collide. Delete or rename the redundant rows in ""ProductAttributes"", then re-run this migration.';
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_ProductAttributes_ProductId_Name""
                ON ""ProductAttributes"" (""ProductId"", lower(""Name""));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_ProductAttributes_ProductId_Name"";");
        }
    }
}
