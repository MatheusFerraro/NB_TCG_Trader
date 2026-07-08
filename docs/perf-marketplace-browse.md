# Marketplace browse performance (#65)

Measured 2026-07-08 against local Postgres 16 (the `docker-compose` dev database),
tuning `GET /marketplace` (`BrowseListings`). Since #72 the catalog (Cards, CardSets,
Games) is seeded locally, so every join the browse query makes — card, set, game,
seller — is a local Postgres join with no external round trip. Card images are stored
`Card.ImageUrl` strings the browser fetches later; they never touch the query hot path.
Query shape is therefore the only pressure as listing volume grows, which is what this
document measures and tunes.

## Test data

`EXPLAIN (ANALYZE, BUFFERS)` was run on a throwaway copy of the dev database
(`CREATE DATABASE nbtcg_perf TEMPLATE nbtcg`) so the real dev data stayed clean. On top
of the full local catalog (20,354 Cards, 174 CardSets, 1 Game) it carried a
representative public-listing volume:

- **8,063 `CollectionItems`** total across **13 sellers** in **6 cities**
  (Moncton, Ipaussu, Toronto, Sao Paulo, Halifax, Vancouver), split CAD/BRL, all five
  conditions, prices 1–500, and `CreatedAt` spread over a full year so the
  `CreatedAt DESC` ordering has real work to do.
- **6,048 public rows** (`IsForSale AND NOT IsPrivate`) — the marketplace slice.
- **2,015 non-public rows** (private or not-for-sale) so the base predicate is
  selective and the privacy filter has something to exclude.

Query shape mirrors the handler exactly: joins to Cards / CardSets (LEFT) / Games /
AspNetUsers, `WHERE IsForSale AND NOT IsPrivate` plus the scenario's filter,
`ORDER BY CreatedAt DESC, Id DESC`, `LIMIT :pageSize` (25 for these measurements; the
handler's `PageSize` defaults to 25 and is capped at 100).

## Baseline plans (before the index)

| Scenario | Time | Plan summary |
|---|---|---|
| Base COUNT | 0.99 ms | Index-only scan on `(IsForSale, IsPrivate)`; 8 buffers. Already fine. |
| **Base fetch, page 1** | **20.4 ms** | Seq scan CollectionItems → **hash-join the whole 20k Cards table** → **top-N sort of all 6,048 public rows** → LIMIT. 693 buffers. |
| Name filter (`ILIKE '%char%'`) | 4.2 ms | **Bitmap Index Scan on `ix_cards_name_trgm`** (265 cards) → nested-loop back to listings → small sort. Trigram serves the join. |
| Set filter (`ILIKE '%base%'` on Name/Code) | 1.2 ms | Seq scan CardSets (174 rows, 4 match) → index scan Cards by `CardSetId`. |
| Location filter (`lower(City)='moncton'`) | 4.0 ms | Seq scan AspNetUsers (13 rows, 3 match) → bitmap on `UserId`. |
| Price + currency filter | 6.5 ms | Same shape as base: seq scan + full-Cards hash + full sort, price/currency as a row filter. |
| Deep page (offset 4000) | 13.7 ms | quicksort of all 6,048 rows, then discard 4,000. |

**The bottleneck** is the page-1 fetch (and its price/currency variant): the base
predicate matches thousands of rows, so Postgres materialises and **sorts the entire
public set** and hashes the whole Cards table just to return the top 25. The existing
`(IsForSale, IsPrivate)` btree covers the predicate but does nothing for the
`CreatedAt DESC, Id DESC` sort.

## Index added

A **partial index over only the public rows, in page order**
(`AddMarketplaceBrowsePartialIndex`, modeled in `CollectionItemConfiguration`):

```sql
CREATE INDEX ix_collectionitems_public_browse
    ON "CollectionItems" ("CreatedAt" DESC, "Id" DESC)
    WHERE "IsForSale" AND NOT "IsPrivate";
```

It covers the base predicate (partial `WHERE`), the ordering, and the `Id` tie-break in
one small index over ~6k rows instead of the whole table. The planner walks it in order
and nested-loop-joins just the page's rows — no full sort, no whole-Cards hash.

### Plans after the index

| Scenario | Before | After | Change |
|---|---|---|---|
| **Base fetch, page 1** | 20.4 ms | **0.61 ms** | Index scan on `ix_collectionitems_public_browse` → 25 nested-loop joins. Buffers 693 → 168. **~33×.** |
| **Price + currency** | 6.5 ms | **0.25 ms** | Same index walk; price/currency applied as a **residual filter** on the index scan — no dedicated price index needed. |
| Name filter | 4.2 ms | 2.0 ms | **Still uses `ix_cards_name_trgm`** — the trigram-filtered set (89 rows) is small, so a top-N sort of it beats walking the browse index. No regression, no duplicate index. |
| Deep page (offset 4000) | 13.7 ms | 14.5 ms | **Unchanged** — at offset 4000 the planner must produce 4,025 ordered rows either way and prefers hash-join + sort. Offset is inherently O(offset); see pagination note. |

## Decisions

- **Partial public-listing index — added.** The measured win on the common path (first
  page, with or without price/currency). Price/currency filters ride along as a residual
  filter, so **no separate price/currency index** is warranted at MVP volume.
- **Card-name search — reuses `ix_cards_name_trgm`, no second index.** Confirmed the
  trigram GIN index (from #72) serves marketplace `ILIKE '%name%'` through the join, the
  same index catalog browse uses. Adding a second `Card.Name` index is explicitly avoided.
- **Set-name/code trigram — not added.** CardSets is tiny (174 rows); the seq scan is
  ~0.17 ms and 2 buffers. A `pg_trgm` GIN index would add write cost for no measurable
  read gain. Revisit only if set volume grows by orders of magnitude (multi-game).
- **Location `lower(City)`/`lower(Country)` expression index — not added.** With 13
  sellers the AspNetUsers seq scan is trivial. Gate on measured cost: revisit when the
  user table is large, at which point normalizing/storing a lowercased location column is
  likely better than an expression index.
- **Base `(IsForSale, IsPrivate)` composite — kept.** It still serves the browse COUNT as
  an index-only scan (~1 ms).

## Pagination: offset kept for MVP, keyset is the later upgrade

Ordering is deterministic — `CreatedAt DESC, Id DESC`, with `Id` as the tie-break so
equal timestamps never reorder across pages, and `MarketplaceEndpointsTests` still proves
private / not-for-sale items never appear.

Offset pagination is O(offset): deep pages (offset 4000 ≈ 14 ms) still sort the whole set
because the engine must count past every skipped row. This is acceptable at MVP —
`PageSize ≤ 100`, `Page ≤ 10,000`, and real traffic lives on the first few pages. The
partial index's `(CreatedAt DESC, Id DESC)` shape is the clean seam for a **keyset/cursor**
upgrade later: pass the last row's `(CreatedAt, Id)` and the query becomes
`WHERE (CreatedAt, Id) < (:lastCreatedAt, :lastId)` `ORDER BY CreatedAt DESC, Id DESC
LIMIT n`, which the same index answers in constant time regardless of depth. Deferred
until deep-page traffic justifies the API-shape change.

## How to reproduce

Both SQL scripts live next to this doc: [`perf/marketplace-browse-seed.sql`](perf/marketplace-browse-seed.sql)
generates the synthetic listings; [`perf/marketplace-browse-explain.sql`](perf/marketplace-browse-explain.sql)
runs the baseline plans. The copy assumes a seeded dev DB named `nbtcg` (adjust the
container/db names to your setup).

```bash
# 1. Throwaway copy of the seeded dev DB (keeps real dev data clean).
docker exec nb_tcg_trader-db-1 psql -U nbtcg -d postgres \
  -c "CREATE DATABASE nbtcg_perf TEMPLATE nbtcg;"

# 2. Seed synthetic listings, then capture the baseline plans.
docker exec -i nb_tcg_trader-db-1 psql -U nbtcg -d nbtcg_perf \
  < docs/perf/marketplace-browse-seed.sql
docker exec -i nb_tcg_trader-db-1 psql -U nbtcg -d nbtcg_perf \
  < docs/perf/marketplace-browse-explain.sql

# 3. (Optional) apply the migration and re-run the explain script to see the
#    post-index plans, then drop the throwaway DB.
docker exec nb_tcg_trader-db-1 psql -U nbtcg -d postgres \
  -c "DROP DATABASE nbtcg_perf;"
```
