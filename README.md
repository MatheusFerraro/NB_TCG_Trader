# NB TCG Trader

[![CI](https://github.com/MatheusFerraro/NB_TCG_Trader/actions/workflows/ci.yml/badge.svg)](https://github.com/MatheusFerraro/NB_TCG_Trader/actions/workflows/ci.yml)

A localized trading-card marketplace and digital collection organizer. The core
differentiator: **bulk CSV import** that turns a spreadsheet of cards into a
visual digital "binder", from which cards can be marked for sale and found by
local buyers (Moncton, NB, Canada / Ipaussu, SP, Brazil).

> [CLAUDE.md](CLAUDE.md) is the full project context (scope, data model,
> conventions); [BACKLOG.md](BACKLOG.md) is the roadmap.

## Demo flow

1. **Register / log in** — ASP.NET Core Identity + JWT (short-lived access token,
   rotating refresh token).
2. **Import a collection** — download the CSV template, upload a `.csv`/`.xlsx`
   of cards. Rows are matched against the pokemontcg.io catalog (exact
   set+number match, then name search).
3. **Reconcile** — ambiguous rows are never dropped: the import lands in
   *Needs review* and the UI lets you pick the right card or skip each row.
4. **Browse the binder** — the imported collection as a visual card grid, with
   quantity, condition, and notes.
5. **Sell** — flag any binder item *for sale* with your own price (CAD/BRL).
   Public listings appear in the marketplace; private or not-for-sale items
   never do.
6. **Contact the seller** — a listing reveals only the seller's public contact
   handles (email / Discord / Instagram). No in-app chat in the MVP.
7. **Admin hub** — role-gated user management: lock/unlock with mandatory
   reason, coarse activity stamps, and an immutable audit log.

## Architecture

Modular monolith with **vertical slices** (one folder per feature: Auth,
Catalog, Collection, Import, Marketplace, Admin). Designed for later extraction
into services, without paying that cost now.

```
React + TS (Azure Static Web Apps)
        │ HTTPS
        ▼
ASP.NET Core API (.NET 9, Docker, Azure Container Apps)
        ├── EF Core / Npgsql ──► PostgreSQL (Supabase, managed Postgres only)
        └── HttpClient ───────► pokemontcg.io (card catalog, cached)
```

- Supabase supplies **only** the managed Postgres (a connection string). Auth is
  ASP.NET Core Identity inside the API; EF Core migrations own the schema.
- The data model is TCG-agnostic (Pokémon ships first; Magic/Lorcana/One Piece
  fit the same schema — a `jsonb` metadata column absorbs per-game attributes).
- The card-data provider sits behind an `ICardCatalogClient` interface with
  in-memory caching, so the provider can be swapped (TCGdex fallback planned).

## Repository layout

```
src/NbTcgTrader.Api/     # ASP.NET Core Web API — feature slices under Features/
tests/NbTcgTrader.Tests/ # xUnit + Shouldly (unit + Testcontainers integration)
web/                     # React + TypeScript frontend (Vite)
.github/workflows/       # CI: API unit / API integration / web
```

## Getting started

Prerequisites: [.NET SDK 9](https://dotnet.microsoft.com/download) (pinned via
`global.json`), [Node.js 20+](https://nodejs.org/), Docker (for the local
database and integration tests).

```bash
# 1. Database + API in containers (uses safe defaults; .env optional — see .env.example)
docker compose up

# 2. Or run the API directly against the compose database
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/NbTcgTrader.Api
dotnet run --project src/NbTcgTrader.Api

# 3. Frontend
cd web && npm install && npm run dev   # http://localhost:5173
```

In Development the API applies EF migrations on startup and serves OpenAPI +
Scalar UI at `/scalar`. Health probe at `/health`.

## Testing

```bash
dotnet test --filter "Category!=Integration"  # fast suite (~seconds, no Docker)
dotnet test --filter "Category=Integration"   # Testcontainers: real Postgres per class
dotnet test                                    # everything
cd web && npm test                             # vitest (API client layer)
```

- **Fast suite** — validators, import matching/parsing, handlers, and
  in-process API hosts that never open a database. This is the local
  inner-loop signal.
- **Integration suite** — tagged `[Trait("Category", "Integration")]`; each
  test class boots the real API against a disposable Postgres container
  (auth flows, ownership rules, import end-to-end, marketplace filters, admin
  lock/audit, schema migration). Skips cleanly when Docker is unavailable.
- CI runs both suites as separate jobs, plus web lint/tests/build — a PR is red
  unless the demo still boots and passes.

## Security choices

- **Passwords & tokens** — Identity's password hasher (never hand-rolled);
  ~15-minute access tokens; refresh tokens rotate and the old one is
  invalidated on use. JWT signing key is env-supplied, ≥256-bit, validated at
  startup (fail-fast), never committed.
- **Authorization** — every mutating endpoint requires auth; handlers enforce
  ownership server-side (your binder/import/profile only). Marketplace queries
  structurally exclude private and not-for-sale items.
- **Proxy hardening** — the API honours `X-Forwarded-For`/`X-Forwarded-Proto`
  from exactly one trusted ingress hop (Azure Container Apps terminates TLS),
  so per-client rate limiting can't be spoofed and HTTPS redirection/HSTS see
  the real scheme. Covered by tests.
- **Input** — FluentValidation on every request; EF Core parameterization; file
  uploads allowlisted (`.csv`/`.xlsx`), size- and row-capped; CSV/XLSX exports
  prefix `= + - @` cells against formula injection.
- **Surface** — CORS locked to configured origins; rate limits on auth and
  import endpoints plus a global per-IP cap; errors are ProblemDetails only (no
  stack traces); HSTS outside Development.
- **Secrets** — environment variables / user-secrets only; `.env.example`
  documents names, never values.

### Secret scanning

Full git-history scan with gitleaks (2026-07-06, 96 commits): **no live secrets
in history**. The two findings are deliberate non-secrets — a throwaway JWT
signing key used only by in-process test servers (`tests/.../TestJwt.cs`) and a
documented local demo password — both allowlisted in [.gitleaks.toml](.gitleaks.toml)
with justification. `.env` has never been committed. Re-run anytime:

```bash
docker run --rm -v "$PWD:/repo" zricethezav/gitleaks:latest git /repo --no-banner --redact
```

Production credentials (database, JWT key, card-API key) are created per
environment at deploy time and can be rotated without code changes.

## Deployment notes

Deploy target: API container on Azure Container Apps (alt: Fly.io/Render),
frontend on Azure Static Web Apps (alt: Vercel), database on Supabase Postgres.
Step-by-step manual deployment guides:
[docs/deploy-azure-container-apps.md](docs/deploy-azure-container-apps.md) (API)
and [docs/deploy-frontend-vercel.md](docs/deploy-frontend-vercel.md) (frontend;
Static Web Apps is region-blocked on the student subscription — see the note in
[docs/deploy-azure-static-web-apps.md](docs/deploy-azure-static-web-apps.md)).

Production environment checklist (API container):

| Variable | Required | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` — disables Scalar/OpenAPI surface and startup migrations, enables HSTS |
| `ConnectionStrings__Default` | yes | Supabase Postgres connection string (SSL required) |
| `Jwt__SigningKey` | yes | ≥32 bytes, generated per environment (`openssl rand -base64 48`); startup fails fast if weak/missing |
| `Jwt__Issuer` / `Jwt__Audience` | no | Non-secret defaults in `appsettings.json` |
| `Cors__AllowedOrigins__0` | yes | Deployed frontend origin; empty default = API locked down |
| `Catalog__PlaceholderImageUrl` | yes | Absolute URL to `/assets/card-placeholder.svg` on the deployed API; startup fails fast if unset |
| `CardApi__Key` | no | pokemontcg.io key (works without one at a lower rate limit) |
| `Admin__SeedEmails` | no | Comma-separated emails granted the Admin role on startup (accounts must already exist; idempotent) |

Frontend (build-time): `VITE_API_URL` — the deployed API origin, no trailing
slash.

- **Migrations** run on startup only in Development. In production, apply them
  as a deliberate deploy step before routing traffic:

  ```
  dotnet ef database update --project src/NbTcgTrader.Api
  ```

  The design-time factory resolves `ConnectionStrings:Default` from user-secrets
  first, then the `ConnectionStrings__Default` environment variable (env wins),
  falling back to the local docker-compose defaults. Point it at Supabase via
  either source; never commit the value.
- **Supabase connection string**: use the **Session pooler** string (port 5432,
  username `postgres.<project-ref>`, `Ssl Mode=Require`). The direct-connection
  host (`db.<project-ref>.supabase.co`) is IPv6-only on the free tier and fails
  DNS on IPv4-only networks; the Transaction pooler (port 6543) breaks the
  prepared statements EF relies on.
- **Forwarded headers** are always on and trust a single proxy hop — correct
  behind ACA/Fly/Render ingress. Do not expose the container directly to the
  internet without a proxy in front.
- CI (`.github/workflows/ci.yml`) gates PRs on: API build (warnings as errors)
  + fast tests, API integration tests on real Postgres, and web
  lint + tests + build.
