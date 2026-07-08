# Spike / ADR: AI-assisted semantic card search with pgvector (#66)

> **Status:** Accepted — option (B). **Date:** 2026-07-08.
> **Decision:** *Defer* query-time vector search. Ship a no-AI **similarity-ranking**
> upgrade first (pg_trgm ranking for catalog search + import reconciliation
> suggestions). Keep pgvector as a documented, feature-flagged phase-2 option that
> only pays off once natural-language search is proven to be needed.
>
> **Implemented (2026-07-08):** the two phase-1 slices from §10 shipped — relevance-ranked
> catalog search (`GET /catalog/cards` orders name hits by `similarity()`) and import-assist
> candidate suggestions (`GET /import/jobs/{jobId}/rows/{rowId}/candidates`, ranked local
> catalog with `%`/`similarity()` typo tolerance). Both are fully local, return only real
> `Card` ids, and add no external dependency. pgvector remains deferred (§8). Enabling change:
> the `extensions` schema is now on the connection search_path so pg_trgm functions resolve.

This is a spike write-up, not shipped code. It records what was evaluated, a runnable
prototype design, a behavior comparison, and follow-up issues. No embedding-provider
key or live Supabase instance was available in the spike environment, so the vector
column of the comparison is *projected from known behavior*, not measured — see
[§6](#6-comparison-deterministic--trigram--vector). The numbers we *can* stand behind
(the existing local trigram path) come from `docs/perf-catalog-search.md`.

---

## 1. Context (where the app is today)

- The catalog is **owned locally in Postgres** (~20k Pokémon `Card` rows, seeded from
  `pokemon-tcg-data`, #72). `Card` carries `Name`, `Number`, `Rarity`, `ImageUrl`, a
  `CardSet` (name/code/release date), and a `jsonb` `Metadata` column for
  variable attributes (types, subtypes, supertype, hp, …).
- **Search is already local and fast.** `GET /catalog/cards` runs
  `ILIKE '%term%'` on `Card.Name`, served by a `pg_trgm` GIN index
  (`ix_cards_name_trgm`). p95 stays well under 200 ms and the hot path makes **no
  external call** — a property deliberately built in #48/#72. `pg_trgm` already lives
  in a dedicated `extensions` schema (Supabase linter hardening).
- **Import reconciliation** (`ImportRowMatcher`) auto-matches a row only when the
  catalog returns **exactly one** card; anything ambiguous stays `Unmatched` for the
  user to resolve by hand. It does **not** currently rank or suggest candidates — the
  manual reconcile screen picks from an unranked search.

So the baseline is stronger than the issue assumes: fuzzy/typo-tolerant name search
already exists and is cheap. The open gaps are (a) **natural-language** queries
("blue charizard from 2023") and (b) **ranked suggestions** for unmatched import rows.

Guardrail from the issue, carried into every option below: **AI/search may only
return real `Card.Id`s from the catalog** — it must never invent cards, prices,
sellers, or touch private data.

---

## 2. Does pgvector fit on Supabase? (operational)

Yes, technically. `pgvector` is a standard Supabase-supported extension (enable via
`create extension vector` in the `extensions` schema, same pattern we already use for
`pg_trgm`). Npgsql supports the type through the `Pgvector.EntityFrameworkCore`
package. On our data size it is comfortable:

- **20k rows** is tiny for an HNSW index; build and query are sub-millisecond at the
  index level.
- **Storage is the real constraint, not compute.** `vector(1536)` (OpenAI
  `text-embedding-3-small`) is ~6 KB/row → **~120 MB** for the catalog plus the HNSW
  index. On Supabase's free tier (~500 MB database) that is a quarter of the budget
  for one feature. Mitigations if we build: shorten `text-embedding-3-small` to 512
  dims (supported, ~40 MB) and/or store as `halfvec` (2 bytes/component).

Conclusion: availability/ops is **not** the blocker. Value-for-cost is.

---

## 3. Embedding shape for a catalog card

If we embed, embed a **stable, public, catalog-only** text projection — never notes,
prices, contact info, or user data. Canonical template (one string per card):

```
{game} | {name} | set: {setName} ({setCode}) | number: {number} |
rarity: {rarity} | {supertype} | types: {types} | subtypes: {subtypes}
```

Example:
`Pokémon | Charizard | set: Base (BS) | number: 4 | rarity: Rare Holo | Pokémon | types: Fire | subtypes: Stage 2`

Rules:
- Source only from `Card` + `CardSet` + whitelisted `Metadata` keys
  (`supertype`, `types`, `subtypes`, `hp`). **Allowlist keys**, don't dump the whole
  `jsonb`, so no future private/derived field leaks into an embedding.
- Store `EmbeddingModel` + `EmbeddingUpdatedAt` alongside the vector so a model change
  is a detectable, re-embeddable event.
- The embedded text is fully reconstructable from public columns → nothing sensitive
  ever leaves the box, satisfying the issue's privacy guardrail by construction.

Note the ceiling: card *names* don't contain "blue" and cards don't carry a plain
release **year** in text. Color/year in "blue charizard from 2023" only becomes
answerable if we also embed/normalize `types` and `CardSet.ReleaseDate` — and even
then a **structured filter** (type = Water/Fire, year = release-date range) answers it
more reliably and cheaper than hoping the embedding encodes it. This is the core
reason NL search is weaker in practice than it sounds (see §5).

---

## 4. Prototype (runnable design; not executed in this spike)

Provided so the follow-up issue starts from working SQL rather than a blank page.
Run against a Postgres with `vector` enabled, over a **100-card sample** (e.g. the
Base set) to satisfy the spike's "50–100 cards" criterion.

```sql
-- 1. Extension + column (mirror the pg_trgm 'extensions' schema convention)
create extension if not exists vector with schema extensions;

alter table "Cards" add column if not exists "Embedding" extensions.vector(512);
alter table "Cards" add column if not exists "EmbeddingModel" text;
alter table "Cards" add column if not exists "EmbeddingUpdatedAt" timestamptz;

-- 2. ANN index (build AFTER backfilling embeddings)
create index if not exists ix_cards_embedding_hnsw
  on "Cards" using hnsw ("Embedding" extensions.vector_cosine_ops);

-- 3. Query: user text is embedded once by the app, passed as $1 (a 512-vector).
--    Returns REAL Card ids only — never generates a card.
select "Id", "Name", "Number", "Rarity",
       1 - ("Embedding" <=> $1) as similarity
from "Cards"
where "GameId" = $2 and "Embedding" is not null
order by "Embedding" <=> $1
limit 10;
```

Backfill is a batch job over the 100-card sample: build the §3 string per card, call
the embedding provider in batches, write the vector. In the app this belongs in the
**seeder / catalog-sync path** (offline), *not* a request handler — see §7.

**Deterministic + trigram baselines to compare against** (no new infra needed — these
run today):

```sql
-- Deterministic exact (what import set+number matching uses)
select "Id","Name" from "Cards"
where "CardSetId" = $set and "Number" = $num;

-- Trigram similarity RANKING (this is the cheap win we don't yet use)
select "Id","Name", similarity("Name", $q) as sim
from "Cards"
where "GameId" = $game and "Name" % $q      -- % = above pg_trgm threshold
order by "Name" <-> $q                        -- distance = best first
limit 10;
```

---

## 5. What each approach can and can't do

| Use case | Deterministic | Trigram (pg_trgm) | Vector (pgvector) |
|---|---|---|---|
| Exact set+number lookup | ✅ best | ⚪ n/a | ⚪ overkill |
| Typo in a name ("charzard") | ❌ | ✅ handles it | ✅ handles it |
| Partial / prefix ("chariz") | ❌ | ✅ handles it | 🟡 ok, not its strength |
| Rank ambiguous import row candidates | ❌ (all-or-nothing today) | ✅ `<->` ranking | ✅ semantic ranking |
| "pikachu promo with grey hat" | ❌ | ❌ | 🟡 *only if* that text is embedded; usually isn't |
| "blue charizard from 2023" | ❌ | ❌ | 🟡 weak — color/year aren't in card text; a structured filter wins |
| Cross-language ("carta do dragão") | ❌ | ❌ | ✅ genuinely unique to embeddings |

**Takeaway:** the two gaps the issue names are *mostly* addressable by
**trigram ranking we already have the index for** — we just don't call it. Vector's
genuinely unique value is **cross-lingual / conceptual** matching (relevant to the
Brazil + bilingual-NB audience), and that is a real but not-yet-urgent differentiator.

---

## 6. Comparison: deterministic → trigram → vector

*(Trigram/deterministic characteristics are from the live local path in
`docs/perf-catalog-search.md`; vector figures are projected, not measured.)*

| Dimension | Deterministic | Trigram | Vector |
|---|---|---|---|
| Infra to add | none | none (already live) | pgvector ext + column + index + embedding provider |
| Hot-path external call | no | no | **yes** (embed the query) unless cached |
| Added query latency | ~0 | ~0 (indexed) | +100–300 ms provider round trip (projected) |
| One-time cost | — | — | ~$0.01 to embed 20k cards (`3-small`, projected) |
| Ongoing cost | — | — | re-embed on catalog sync + ~$ per uncached query |
| Storage | — | small GIN | ~40–120 MB (dim/type dependent) |
| Privacy surface | none | none | query text leaves to provider (catalog text only if we also pre-embed offline) |
| Failure mode | local | local | external dep on the hot path → new 5xx/timeout surface we deliberately removed in #48 |

The decisive row is **hot-path external call**. #48/#72 spent real effort getting the
provider *out* of the search hot path; query-time embedding puts an external
dependency (and its latency + failure modes) right back in. That trade is only worth
it for capability we can't get locally — i.e. cross-lingual/conceptual search — and
only behind a fallback to the local path.

---

## 7. If/when we build it: generation strategy

- **Generate at ingestion / catalog-sync time (offline batch), never on demand for the
  catalog.** Embeddings are a pure function of public card fields; compute them in the
  seeder and on catalog updates, exactly like we own the catalog itself now. This keeps
  the hot path local.
- **Only the user's query is embedded at request time**, and that single call should be
  cached (same normalized query → same vector), mirroring the existing catalog search
  cache. Deterministic/trigram remains the default; vector runs only for queries that
  look natural-language (multi-word, non-set-code) and always with the local path as
  fallback if the provider is slow/down.
- Feature-flag it (`Search:VectorEnabled`) so it can ship dark and be measured.

---

## 8. Decision & recommendation

**Recommendation: (B) implement import-assist + ranked search first (no AI), defer pgvector.**

1. **Now, cheap, high-value, zero AI/privacy surface:** use **pg_trgm similarity
   ranking** (`% ` + `<->`) to
   - return **ranked candidate suggestions** for `Unmatched` import rows (turns the
     current all-or-nothing matcher into "here are the 5 most likely cards, pick one"),
     and
   - order catalog search results by relevance instead of alphabetically.
   All local, all real `Card.Id`s, no external call, no new dependency. Captures most
   of the issue's practical value (typo tolerance + import suggestions).

2. **Defer query-time pgvector** until natural-language / cross-lingual search is a
   proven, requested need. When it is, build it per §3–§7 (offline embeddings, public
   fields only, feature-flagged, local fallback, `halfvec`/512-dim to fit the free
   tier). The `ICardCatalogClient` + local-catalog seam makes this a contained
   addition later.

Why not "build vector now": it re-introduces an external dependency and latency on a
hot path we deliberately made local, spends ~a quarter of the free-tier DB budget, and
its unique capability (conceptual/cross-lingual) isn't yet a validated user need — all
against the project's "simplest thing that satisfies the MVP" and "hosting cost near
zero" principles.

---

## 9. Acceptance-criteria trace

- **ADR/spike notes on whether pgvector is worth adding now** — this doc; answer: not
  yet (§8).
- **Prototype ≥50–100 cards** — runnable SQL over a 100-card sample in §4 (designed,
  not executed here: no embedding key / live Supabase in the spike env; execution is
  task 1 of the follow-up issue).
- **Comparison of deterministic / fuzzy-text / vector** — §5 and §6.
- **AI suggestions always return real Card ids** — enforced by design: every query in
  §4 selects `"Id"` from `"Cards"`; nothing is generated (§1, §3).
- **No live pricing / private notes / tokens / contact data embedded** — §3 allowlists
  public catalog keys only; guardrail is structural.
- **Follow-up issues if we recommend building** — §10.

---

## 10. Proposed follow-up issues

- ✅ **Rank import reconciliation candidates with pg_trgm** *(phase-1, backend, data)* —
  **shipped.** New `GET /import/jobs/{jobId}/rows/{rowId}/candidates` returns the top-5
  ranked catalog cards for a row using the `%` operator (typo tolerance) + `similarity()`
  ordering, boosting an exact collector-number match when the row carried one. Auto-match
  rule unchanged (still exact-1); owner-scoped 404; fully local; real `Card` ids only.
- ✅ **Relevance-rank catalog search** *(phase-1, backend, data)* — **shipped.**
  `GET /catalog/cards` now orders name hits by trigram `similarity()` (name/id tiebreak)
  instead of alphabetical, so the closest name ranks first; the pg_trgm GIN index still
  serves the filter.
- **#NN — (phase-2, spike-impl) pgvector semantic + cross-lingual search behind a
  flag** *(phase-2, backend, data)*. Only if NL/cross-lingual search becomes a real
  need. Implements §3–§7: offline embeddings over public fields, `halfvec`/512-dim,
  HNSW, query-embed-once + cache, deterministic/trigram fallback, `Search:VectorEnabled`
  flag. AC: real `Card.Id`s only; nothing private embedded; local path serves when the
  provider is unavailable.
```
