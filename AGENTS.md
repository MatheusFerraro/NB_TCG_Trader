# NB TCG Trader — Project Context

> Single source of truth for this project. Read it fully before generating or
> editing code. Keep it updated as decisions change. When in doubt, prefer the
> simplest thing that satisfies the MVP scope in section 2.

---

## 1. What we're building

**NB TCG Trader** is a localized trading-card marketplace and digital collection
organizer. The core differentiator is a **bulk CSV import** that turns a
spreadsheet of cards into a visual digital "binder", from which a user can mark
cards for sale and connect with local buyers.

- **Audiences:** collectors in Moncton (New Brunswick, Canada) and Ipaussu
  (São Paulo, Brazil), plus a portfolio/demo piece for a Canadian dev job search.
- **Design principle:** the data model is **TCG-agnostic**. Pokémon ships first,
  but the schema must also be able to hold Magic, Lorcana, One Piece, etc. The
  Pokémon-only UI is a presentation choice, not a schema constraint.

---

## 2. Goals & non-goals

**Goals**
- Ship a clean, demoable, deployed MVP.
- Showcase solid engineering: vertical slices, validation, structured logging,
  external API integration, an import-and-reconcile flow, tests.
- Keep hosting cost near zero.

**Non-goals for the MVP (explicitly out of scope — do NOT build these yet)**
- In-app chat (MVP "contact seller" just reveals an email/Discord/Instagram handle)
- Trade / card-swap system
- Live market-price estimation (users type their own prices)
- Wishlist
- Mobile app (Flutter comes in a later phase)
- Admin moderation dashboard
- Event advertising

---

## 3. Tech stack (locked)

| Layer            | Choice                                              |
|------------------|-----------------------------------------------------|
| Backend          | ASP.NET Core Web API (.NET 9, C#)                   |
| Architecture     | Modular monolith, vertical slices                   |
| Database         | PostgreSQL (hosted on Supabase), EF Core + Npgsql   |
| Auth             | ASP.NET Core Identity + JWT (access + refresh)      |
| Frontend         | React + TypeScript (Vite)                           |
| Container        | Docker                                              |
| API host         | Azure Container Apps (alt: Fly.io / Render)          |
| Frontend host    | Azure Static Web Apps (alt: Vercel)                 |
| Logging          | Serilog                                             |
| Validation       | FluentValidation                                    |
| Tests            | xUnit + Shouldly                                    |
| CSV / Excel      | CsvHelper (.csv), ClosedXML (.xlsx)                 |
| API docs / UI    | Scalar (OpenAPI)                                    |
| Rate limiting    | Built-in ASP.NET Core rate limiting                 |
| Card data        | pokemontcg.io (primary), TCGdex (free fallback)     |

**MediatR is optional.** Slices are implemented with minimal-API endpoint
classes calling handler classes directly; FluentValidation runs via an endpoint
filter. If a request pipeline is later desired, MediatR is free for solo/small
use — but do not add it unless a slice genuinely needs cross-cutting behaviors.

---

## 4. Architecture & hosting topology

A single deployable API (modular monolith) organized into **vertical slices** by
feature. No microservices. The README should note the monolith is "designed for
later extraction into services" to document the trade-off without paying its cost.

```
React (Static Web Apps)  ──HTTPS──►  ASP.NET Core API (Docker, Azure Container Apps)
                                          │
                                          ├── EF Core / Npgsql ──► PostgreSQL (Supabase)
                                          └── HttpClient ──► pokemontcg.io / TCGdex
```

**Important:** Supabase is used **only as the managed Postgres database** (a
connection string). Auth is ASP.NET Core Identity inside our own API — we do NOT
use Supabase Auth or Supabase Row-Level Security. EF Core migrations own the
schema.

---

## 5. Solution structure

```
/
├── AGENTS.md
├── BACKLOG.md
├── docker-compose.yml          # local: api + local postgres for dev
├── Dockerfile
├── src/
│   └── NbTcgTrader.Api/
│       ├── Features/
│       │   ├── Auth/
│       │   ├── Catalog/         # external card data
│       │   ├── Collection/      # the binder
│       │   ├── Import/          # CSV upload + reconcile
│       │   └── Marketplace/
│       ├── Common/
│       │   ├── Persistence/     # AppDbContext, migrations, configs
│       │   ├── Errors/          # ProblemDetails helpers
│       │   ├── Filters/         # validation endpoint filter
│       │   └── Extensions/
│       └── Program.cs
├── tests/
│   └── NbTcgTrader.Tests/
└── web/                         # React + TS (Vite)
```

---

## 6. Vertical slice conventions

Each feature folder is self-contained. A typical slice (e.g. `Collection/AddCard`)
holds, in one or few files:

1. **Endpoint** — a static class with `MapXxxEndpoints(this IEndpointRouteBuilder)`
   registering the route(s).
2. **Request/Command/Query** — record types for input.
3. **Handler** — the business logic; takes `AppDbContext` and any services via DI.
4. **Validator** — a `FluentValidation.AbstractValidator<T>` for the request.
5. **Response DTO** — never return EF entities directly.

Rules:
- Slices do not call each other's handlers. Shared logic goes in `Common/`.
- Return `Results.Ok/Created/NotFound/...`; map failures to **ProblemDetails**.
- All endpoints that mutate data require `[Authorize]` unless noted.

---

## 7. Data model (TCG-agnostic)

EF Core code-first. Names are guidance; adjust as needed but keep the shape.

**Game** — a TCG. `Id`, `Name`, `Slug`.

**CardSet** — an expansion/set. `Id`, `GameId`, `Name`, `Code`, `ReleaseDate?`,
`ExternalId?`.

**Card** — a catalog entry (shared, not owned by a user). `Id`, `GameId`,
`CardSetId?`, `ExternalId` (id from the data API), `Name`, `Number?`, `Rarity?`,
`ImageUrl?`, `Metadata` (Postgres `jsonb` for variable attributes). Indexed on
`(GameId, Name)` and `ExternalId`.

**AppUser** — extends `IdentityUser`. Adds `DisplayName`, `City` (e.g. "Moncton",
"Ipaussu"), `Country`, and public contact fields: `ContactEmail?`,
`DiscordHandle?`, `InstagramHandle?`.

**CollectionItem** — a user owning copies of a card (this is the binder row AND,
when flagged, the listing). `Id`, `UserId`, `CardId`, `Quantity`, `Condition`
(enum: NM, LP, MP, HP, DMG), `IsForSale` (bool), `Price?` (decimal),
`Currency` (enum: CAD, BRL), `IsPrivate` (bool), `Notes?`, `CreatedAt`,
`UpdatedAt`. A marketplace query = `CollectionItems where IsForSale && !IsPrivate`.
> Phase-2 note: split the for-sale fields into a dedicated `Listing` entity when
> multiple listings per card or offer history is needed.

**ImportJob** — one CSV upload. `Id`, `UserId`, `FileName`, `Status` (enum:
Pending, Processing, NeedsReview, Completed, Failed), `RowsTotal`, `RowsMatched`,
`RowsUnmatched`, `CreatedAt`.

**ImportRow** — a single parsed row, kept for reconciliation. `Id`, `ImportJobId`,
`RawName`, `RawSet?`, `RawNumber?`, `Quantity`, `Price?`, `Condition?`,
`MatchStatus` (enum: Unmatched, AutoMatched, ManuallyMatched, Skipped),
`MatchedCardId?`.

Relationships: Game 1—* CardSet, Game 1—* Card, CardSet 1—* Card,
AppUser 1—* CollectionItem, Card 1—* CollectionItem, AppUser 1—* ImportJob,
ImportJob 1—* ImportRow.

---

## 8. External card data

- **Primary:** pokemontcg.io — free with an API key; provides names, sets,
  rarities, and high-quality images. Used to populate `Card` and `CardSet`.
- **Fallback:** TCGdex — open source, no key, multilingual; good for resilience.
- **No live pricing in the MVP.** Pricing APIs are now mostly paid; users enter
  their own prices.
- **Do NOT store card images.** Persist the remote `ImageUrl` and hotlink it.
  Only blob-store images that *users* upload (a later phase).
- Wrap the data source behind an `ICardCatalogClient` interface so the provider
  can be swapped. Cache catalog lookups (in-memory is fine for MVP) and respect
  the provider's rate limits.

---

## 9. CSV import contract

Provide a downloadable template. Expected columns (header row required):

```
card_name, set, card_number, quantity, condition, price, for_sale
```

- `card_name` required; everything else optional.
- Parsing: CsvHelper for `.csv`, ClosedXML for `.xlsx`.
- **Matching strategy (best-effort):**
  1. If `set` + `card_number` present → exact match against the catalog.
  2. Else → name search; if exactly one result, auto-match; if several, leave
     `Unmatched` for the user to resolve.
- Unmatched rows are first-class: the import lands in `NeedsReview`, and the UI
  lets the user pick the right card or skip. Never silently drop a row.
- Large files: parse and match server-side; for the MVP, synchronous processing
  is acceptable if files stay small; otherwise process per-row and report counts.

---

## 10. Cross-cutting concerns

- **Logging:** Serilog, structured, console + (optionally) a file sink. Log
  request/response at the edge; never log secrets, tokens, or full card lists.
- **Validation:** FluentValidation via a shared endpoint filter; invalid input →
  `400` ProblemDetails with field errors.
- **Errors:** central exception handling → ProblemDetails (`application/problem+json`).
  No raw stack traces to clients.
- **Rate limiting:** built-in ASP.NET Core limiter on auth and import endpoints.
- **API docs:** Scalar UI over the OpenAPI document (Swashbuckle is no longer in
  the default template).
- **Config/secrets:** connection string, JWT signing key, and card-API key come
  from environment variables / user-secrets locally. Never commit secrets.
- **CORS:** allow only the known frontend origin(s).

---

## 11. Testing

- xUnit + Shouldly.
- Prioritize: import matching logic, validators, and marketplace query filters.
- Handler tests use an in-memory or test Postgres; keep them fast and isolated.
- A test is meaningful only if it can fail for a real reason — no assertion-free
  tests.

---

## 12. Definition of Done (per issue)

- Code compiles; no new warnings introduced.
- Endpoint validated, authorized where required, and documented in OpenAPI.
- Happy path + at least one failure path covered by a test where logic is non-trivial.
- Errors return ProblemDetails; secrets stay out of code and logs.
- BACKLOG.md / AGENTS.md updated if a decision changed.

---

## 13. Local dev setup

- `docker-compose up` runs the API plus a local Postgres for development.
- EF Core migrations applied on startup in Development only.
- A `.env.example` lists required variables: `ConnectionStrings__Default`,
  `Jwt__SigningKey`, `Jwt__Issuer`, `Jwt__Audience`, `CardApi__Key`,
  `Cors__AllowedOrigins`.
- Supabase (real Postgres) is used for the deployed environments, not local dev.

---

## 14. Coding standards

- Nullable reference types on; treat warnings seriously.
- Records for DTOs/requests; `sealed` classes by default.
- Async all the way for I/O; pass `CancellationToken` through handlers.
- No EF entities crossing the API boundary — always map to DTOs (manual mapping
  or Mapperly; avoid AutoMapper).
- Small, focused commits that map to backlog issues.

---

## 15. Security

**Authentication & tokens**
- Use ASP.NET Core Identity's password hasher — never hand-roll hashing.
- Short-lived access tokens (~15 min) + rotating refresh tokens; invalidate the
  old refresh token when it is used.
- The JWT signing key comes from config/secret, is at least 256-bit, is never
  committed, and differs per environment.
- Enforce a sane password policy via Identity options.

**Authorization**
- Every mutating endpoint requires `[Authorize]`.
- Enforce ownership in the handler: a user can only read/edit/delete their own
  `CollectionItem`s, `ImportJob`s, and profile. Check the owning `UserId`
  server-side — never trust an id supplied by the client.
- Private or not-for-sale items must never appear in marketplace responses.
- Expose only public contact fields (ContactEmail / Discord / Instagram). Never
  return password hashes or another user's private data.

**Input & data**
- Validate all input with FluentValidation; reject unexpected fields.
- Rely on EF Core parameterization; never build SQL by string concatenation.
- File uploads: allowlist extensions (`.csv` / `.xlsx`) and content types, cap
  file size, and cap row count. Do not trust the client-supplied filename.
- CSV injection: when generating the template or any CSV/XLSX export, prefix
  cells beginning with `=` `+` `-` `@` so spreadsheets don't run them as formulas.

**Transport & surface**
- HTTPS only; enable HSTS in production.
- CORS restricted to the known frontend origin(s).
- Rate-limit auth and import endpoints.
- Return ProblemDetails; never leak stack traces, connection strings, or internal
  paths to clients.

**Secrets & dependencies**
- Secrets via environment variables / user-secrets only. Provide `.env.example`
  with variable names but never values. Add secret files to `.gitignore`.
- Keep NuGet/npm packages patched; avoid abandoned packages; review every new
  dependency before adding it.

**Logging**
- Never log tokens, passwords, signing keys, connection strings, or full personal
  data. Log user ids, not credentials.

---

## 16. Commit messages & git workflow

- **Do NOT run any git commands** (`git add` / `commit` / `push`). The developer
  commits manually.
- After completing a unit of work, output a **proposed commit message as plain
  text** in a single copyable code block — nothing else inside it — so it can be
  pasted straight into a manual commit.
- Use **Conventional Commits**: `type(scope): summary`, where `type` is one of
  feat, fix, refactor, test, docs, chore, build, ci.
- Subject line ≤ 72 chars, imperative mood. Add a short body only when context
  helps. Reference the backlog issue, e.g. `Closes #14`.
- One logical change per commit, mirroring the backlog issues.
- Send the commits by files. If a feature touches multiple files, commit them together, to clarify: one commit by file or files that matches.

Example:
```
Files:

src/NbTcgTrader.Api/NbTcgTrader.Api.csproj
src/NbTcgTrader.Api/Common/Domain/ (all files)

feat(data): add TCG-agnostic domain model and EF/Identity packages

Add Game, CardSet, Card, AppUser (IdentityUser + contact/location
fields), CollectionItem, ImportJob, ImportRow and their enums per
AGENTS.md §7. Reference Npgsql EF Core, EF Core Design tooling, and
ASP.NET Core Identity EF stores.

Part of #5

-------

Commit 5 — Tests

Files:

tests/NbTcgTrader.Tests/NbTcgTrader.Tests.csproj
tests/NbTcgTrader.Tests/ApiWebApplicationFactory.cs
tests/NbTcgTrader.Tests/AppDbContextModelTests.cs
tests/NbTcgTrader.Tests/DatabaseMigrationTests.cs
tests/NbTcgTrader.Tests/CrossCuttingTests.cs
tests/NbTcgTrader.Tests/RateLimitingTests.cs

test(data): cover EF model and migration against Postgres

Add an offline AppDbContext model test (AppUser contact/location fields,
domain entities, indexes, jsonb, string enums) and a Testcontainers
Postgres test that applies the migration and asserts tables exist
(skips cleanly without Docker). Introduce a shared ApiWebApplicationFactory
so cross-cutting tests boot without a database, and repoint existing
fixtures to it.

Closes #5
```

<!-- caveman-begin -->
Respond terse like smart caveman. All technical substance stay. Only fluff die.

Rules:
- Drop: articles (a/an/the), filler (just/really/basically), pleasantries, hedging
- Fragments OK. Short synonyms. Technical terms exact. Code unchanged.
- Pattern: [thing] [action] [reason]. [next step].
- Not: "Sure! I'd be happy to help you with that."
- Yes: "Bug in auth middleware. Fix:"

Switch level: /caveman lite|full|ultra|wenyan-lite|wenyan-full|wenyan-ultra
Stop: "stop caveman" or "normal mode"

Auto-Clarity: drop caveman for security warnings, irreversible actions, user confused. Resume after.

Boundaries: code/commits/PRs written normal.
<!-- caveman-end -->
