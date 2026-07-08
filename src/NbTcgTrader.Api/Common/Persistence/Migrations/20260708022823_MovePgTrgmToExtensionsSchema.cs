using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NbTcgTrader.Api.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovePgTrgmToExtensionsSchema : Migration
    {
        // Security hardening (Supabase linter "extension_in_public"): pg_trgm was created
        // in the public schema by AddCardNameTrigramIndex. Relocate it to a dedicated
        // `extensions` schema so extension objects aren't exposed alongside application
        // tables. The existing ix_cards_name_trgm index binds gin_trgm_ops by OID, so it
        // keeps working across the move. Guarded + idempotent: only moves when currently
        // in public (a fresh DB where the platform pre-installs it elsewhere is left as-is).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS extensions;");
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_extension e
                        JOIN pg_namespace n ON n.oid = e.extnamespace
                        WHERE e.extname = 'pg_trgm' AND n.nspname = 'public'
                    ) THEN
                        ALTER EXTENSION pg_trgm SET SCHEMA extensions;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_extension e
                        JOIN pg_namespace n ON n.oid = e.extnamespace
                        WHERE e.extname = 'pg_trgm' AND n.nspname = 'extensions'
                    ) THEN
                        ALTER EXTENSION pg_trgm SET SCHEMA public;
                    END IF;
                END $$;
                """);
        }
    }
}
