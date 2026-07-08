using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NbTcgTrader.Api.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardNameTrigramIndex : Migration
    {
        // Fuzzy/partial name search for the local catalog (#72): the pg_trgm GIN index
        // lets ILIKE '%term%' on Card.Name use an index instead of a full scan, so
        // "chariz" finds "Charizard" and p95 stays well under 200 ms. Raw SQL because
        // the extension and the gin_trgm_ops operator class aren't modeled by EF.
        private const string IndexName = "ix_cards_name_trgm";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // Resolve gin_trgm_ops from whatever schema pg_trgm actually lives in, rather
            // than assuming it is on the search_path. A platform (e.g. Supabase) may
            // pre-install the extension in a dedicated `extensions` schema that isn't
            // searched, so an unqualified operator class would fail to resolve here.
            migrationBuilder.Sql(
                $$"""
                DO $$
                DECLARE
                    ext_schema text;
                BEGIN
                    SELECT n.nspname INTO ext_schema
                    FROM pg_extension e
                    JOIN pg_namespace n ON n.oid = e.extnamespace
                    WHERE e.extname = 'pg_trgm';

                    EXECUTE format(
                        'CREATE INDEX IF NOT EXISTS {{IndexName}} '
                        'ON "Cards" USING gin ("Name" %I.gin_trgm_ops)',
                        ext_schema);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP INDEX IF EXISTS {IndexName};");
            // Leave the pg_trgm extension in place: other objects may depend on it and
            // dropping it is not required to reverse this migration's schema change.
        }
    }
}
