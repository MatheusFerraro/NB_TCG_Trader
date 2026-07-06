# NB TCG Trader — Backlog

Mirror of the GitHub Project board. **Phase 1 = MVP.** Phase 2 = deferred.
Each issue has acceptance criteria (AC) and labels.

**Status:** ✅ = done (closed on the board); unmarked = open. Synced from git
history on 2026-07-06 — when in doubt, the board wins.

> **Numbering note:** GitHub issues and PRs share one number sequence, so the
> board's issue numbers diverge from a pure 1..N list: #28–#38 were consumed by
> pull requests, and issues created later on the board (review feedback,
> frontend slices) start at #39. Those are listed in "Board-created issues"
> below.

**Suggested labels:** `phase-1`, `phase-2`, `epic`, `infra`, `backend`,
`frontend`, `auth`, `data`, `docs`.

**Milestones:** `M0 Setup`, `M1 Core API`, `M2 Import`, `M3 Marketplace`,
`M4 Frontend`, `M5 Deploy`.

---

## M0 — Project setup (`infra`, `phase-1`)

**#1 Initialize solution and repo structure** ✅
Create the .NET solution, `NbTcgTrader.Api`, test project, and `web/` React app
per CLAUDE.md §5. Add `.gitignore`, `.editorconfig`, README.
- AC: solution builds; folder structure matches CLAUDE.md; README has a one-line
  description and run instructions.
- Labels: infra, backend, phase-1

**#2 Dockerfile + docker-compose for local dev** ✅
Containerize the API; compose file runs API + local Postgres.
- AC: `docker-compose up` starts API and Postgres; API reachable locally.
- Labels: infra, phase-1

**#3 Configure Serilog, ProblemDetails, CORS, rate limiting** ✅
Wire cross-cutting concerns from CLAUDE.md §10 in `Program.cs`.
- AC: structured logs on startup; unhandled errors return ProblemDetails; CORS
  restricted to configured origins; rate limiter registered.
- Labels: backend, infra, phase-1

**#4 Scalar / OpenAPI documentation** ✅
Expose the OpenAPI doc with a Scalar UI.
- AC: Scalar UI loads and lists endpoints as they're added.
- Labels: backend, docs, phase-1

---

## Epic: Auth (`epic`, `auth`, `phase-1`) — M1

**#5 EF Core + Identity + Postgres wiring** ✅
Add `AppDbContext`, Npgsql, ASP.NET Core Identity with `AppUser` (CLAUDE.md §7),
initial migration.
- AC: migration creates Identity + domain tables on Postgres; `AppUser` carries
  contact + location fields.
- Labels: backend, auth, data, phase-1

**#6 Register / Login with JWT (access + refresh)** ✅
Endpoints: register, login, refresh, `me`.
- AC: valid credentials return access + refresh tokens; refresh rotates; `me`
  returns the current user; invalid input → 400 ProblemDetails; auth endpoints
  rate-limited.
- Labels: backend, auth, phase-1

**#7 Profile / contact info update** ✅
Let a user set DisplayName, City/Country, and public contact handles.
- AC: authorized user updates profile; validation enforced; reflected in `me`.
- Labels: backend, auth, phase-1

---

## Epic: Card Catalog (`epic`, `data`, `phase-1`) — M1

**#8 `ICardCatalogClient` over pokemontcg.io** ✅
Typed HttpClient wrapper with in-memory caching and rate-limit handling.
- AC: search cards by name/set/number; map results to `Card`/`CardSet`; TCGdex
  fallback documented behind the same interface.
- Labels: backend, data, phase-1

**#9 Catalog search endpoint** ✅
`GET /catalog/cards?query=&set=&number=` for the "browse and add" flow.
- AC: returns paged card results with image URLs; never returns nulls for
  required display fields.
- Labels: backend, data, phase-1

---

## Epic: Collection / Binder (`epic`, `backend`, `phase-1`) — M1

**#10 Add card to my collection** ✅
Create a `CollectionItem` from a catalog card (quantity, condition).
- AC: authorized; persists; rejects unknown card; returns the created item.
- Labels: backend, phase-1

**#11 View my binder** ✅
`GET /collection/me` returning the user's items with card display data.
- AC: paged/grid-ready DTO; supports include/exclude private items.
- Labels: backend, phase-1

**#12 Edit / delete a collection item** ✅
Update quantity, condition, price, for-sale flag, private flag; delete.
- AC: owner-only; validation; soft rules (price required if `for_sale`).
- Labels: backend, phase-1

---

## Epic: CSV Import & Reconciliation (`epic`, `backend`, `phase-1`) — M2

**#13 Downloadable import template** ✅
Serve the template CSV (CLAUDE.md §9 columns).
- AC: endpoint returns a valid template file with headers.
- Labels: backend, docs, phase-1

**#14 Upload + parse CSV/XLSX into an ImportJob** ✅
Accept upload; parse with CsvHelper/ClosedXML; create `ImportJob` + `ImportRow`s.
- AC: bad files rejected with clear errors; row count recorded; oversized files
  guarded.
- Labels: backend, data, phase-1

**#15 Auto-match rows to catalog** ✅
Apply the matching strategy; set `MatchStatus`; move job to `NeedsReview` or
`Completed`.
- AC: set+number exact match works; single-name match auto-matches; ambiguous
  rows stay Unmatched; counts updated.
- Labels: backend, data, phase-1

**#16 Reconcile unmatched rows** ✅
Endpoints to list unmatched rows and resolve each (pick card / skip), creating
`CollectionItem`s on confirm.
- AC: resolving a row creates the binder item; skipped rows excluded; job
  completes when none remain.
- Labels: backend, data, phase-1

---

## Epic: Marketplace (`epic`, `backend`, `phase-1`) — M3

**#17 Browse / search for-sale listings** ✅
`GET /marketplace` filtering by game, set, name, price range, city/country.
- AC: returns only `IsForSale && !IsPrivate`; paging; filters combine correctly.
- Labels: backend, phase-1

**#18 Listing detail + seller contact** ✅
`GET /marketplace/{itemId}` returns card + price + seller's public contact info.
- AC: private/not-for-sale items 404; only public contact fields exposed.
- Labels: backend, phase-1

---

## Epic: Frontend — React (`epic`, `frontend`, `phase-1`) — M4

**#19 App shell, routing, auth flow** ✅
Vite + TS app; login/register; token storage; authenticated API client.
- AC: user can register, log in, stay logged in across refresh, log out.
- Labels: frontend, phase-1

**#20 Binder view (grid)** ✅
Card-grid binder with image, name, set, quantity, price; edit/for-sale/private
controls.
- AC: renders the user's collection; edits persist via API.
- Labels: frontend, phase-1

**#21 CSV import UI + reconciliation screen** ✅
Upload file, show match summary, resolve unmatched rows.
- AC: full upload → review → confirm flow works end to end.
- Labels: frontend, phase-1

**#22 Marketplace browse + listing detail** ✅
Search/filter UI; listing page with "contact seller" revealing handles.
- AC: filters work; contact info shown only on a real listing.
- Labels: frontend, phase-1

---

## Epic: Admin / operations (`epic`, `admin`, `phase-1`)

**#27 Admin hub: role, user management, lock/unlock, audit log, activity** ✅
Admin role (ASP.NET Core Identity roles) + protected `/admin` API surface and
React admin hub. First-admin bootstrap via `Admin__SeedEmails` env var
(idempotent startup seeding; accounts must already exist). Endpoints: dashboard
counts, user list (search/city/country/locked filters), user detail (contact
fields as present/absent flags only), lock (reason required, revokes refresh
tokens, blocks sign-in/refresh) / unlock, immutable `AdminAuditLog`
(lock/unlock/detail-view, correlation ids), activity view derived from existing
data (`CreatedAt`/`LastLoginAt`/`LastSeenAt`, failed-attempt counters, failed
imports) — no per-request tracking, no IPs.
- AC: admin endpoints enforce the Admin role policy (401 anon / 403 non-admin);
  lock requires a reason, blocks sign-in and refresh, and is audited; admins
  cannot lock themselves or other admins; contact handle values never appear in
  admin responses.
- Labels: backend, frontend, security, phase-1

---

## Board-created issues (added on GitHub after this file; #28–#38 are PRs)

**#39 Import: tighten upload stream handling** ✅
**#41 Import: reject blank reconcile card ids** ✅
**#42 Marketplace: tighten browse filters** ✅
**#43 Tooling: caveman-compress backup workflow** ✅
**#44 Web: auth flow review feedback** ✅
**#45 Web: binder review feedback** ✅
**#46 Web: manual add-card flow (+ catalog 503 on provider timeout)** ✅
**#47 Web: manage-card dialog with edit and delete** ✅
**#58 Web: profile settings page for seller contact info** ✅
**#59 Admin hub review feedback** ✅

---

## Epic: Deployment (`epic`, `infra`, `phase-1`) — M5

**Pre-deployment hardening (secret scan, proxy hardening, CI, test split, README)**
Before deploying the MVP: rotate any possibly-committed keys, run and document a
repository-history secret scan, production env-var checklist, forwarded-headers
hardening for hosted ingress, CI for API + web, split slow Testcontainers tests
from the fast suite, recruiter-ready README.
- AC: fresh clone runs documented checks without guesswork; no live secrets in
  history; deployed configuration documented and reproducible; README covers
  what/tradeoffs/practices.
- Labels: infra, docs, security, phase-1

**#23 Provision Supabase Postgres + run migrations**
- AC: deployed API connects to Supabase; schema migrated.
- Labels: infra, phase-1

**#24 Deploy API to Azure Container Apps**
- AC: container image deployed; env vars/secrets configured; HTTPS endpoint live.
- Labels: infra, phase-1

**#25 Deploy React to Azure Static Web Apps**
- AC: frontend live; points at the deployed API; CORS allows it.
- Labels: frontend, infra, phase-1

**#26 CI: build + test on PR (GitHub Actions)** ✅
Landed with the pre-deployment hardening work: `.github/workflows/ci.yml` runs
API build + fast tests, API integration tests (Testcontainers), and web
lint + tests + build.
- AC: PRs run build + tests; red on failure.
- Labels: infra, phase-1

---

## Phase 2 — deferred (`phase-2`)

- Wishlist ("I want this card")
- In-app chat between buyer and seller
- Trade / card-swap system (multi-party offers)
- Live market-price estimation (paid pricing API)
- User-uploaded card photos (blob storage)
- Admin moderation (spam/abuse)
- Event advertising (local trades/meetups)
- Flutter mobile app
- Multi-TCG UI (expose Magic/Lorcana/etc. already supported by the schema)
- Localization (PT-BR / EN / FR) for the Moncton + Brazil + bilingual NB market
