using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NbTcgTrader.Api.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceBrowsePartialIndex : Migration
    {
        // Marketplace browse (#65): a partial btree over only the public rows
        // (IsForSale AND NOT IsPrivate) in the page order (CreatedAt DESC, Id DESC).
        // The paged fetch walks it and nested-loop-joins just the page instead of
        // sorting every public listing and hashing the whole Cards table; price/currency
        // filters ride along as a residual filter. Measured ~20 ms -> ~0.6 ms for the
        // first page on ~6k public rows (docs/perf-marketplace-browse.md). The composite
        // (IsForSale, IsPrivate) index stays for the browse COUNT. EF renders this as
        // CREATE INDEX ... ("CreatedAt" DESC, "Id" DESC) WHERE "IsForSale" AND NOT "IsPrivate".
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_collectionitems_public_browse",
                table: "CollectionItems",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0],
                filter: "\"IsForSale\" AND NOT \"IsPrivate\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collectionitems_public_browse",
                table: "CollectionItems");
        }
    }
}
