---
name: verify
description: Build, launch, and drive NB TCG Trader (API + React web) locally to verify changes end-to-end.
---

# Verify NB TCG Trader locally

## Launch (three pieces)

1. **Postgres** (Docker required): `docker compose up -d db` — localhost:5432, creds `nbtcg`/`nbtcg_dev_password`/db `nbtcg` (defaults in docker-compose.yml).
2. **API** on http://localhost:5167 (background task; PowerShell 5.1 syntax):
   ```powershell
   $env:ConnectionStrings__Default='Host=localhost;Port=5432;Database=nbtcg;Username=nbtcg;Password=nbtcg_dev_password'
   $rng = [Security.Cryptography.RandomNumberGenerator]::Create(); $bytes = New-Object byte[] 48; $rng.GetBytes($bytes)
   $env:Jwt__SigningKey = [Convert]::ToBase64String($bytes)
   dotnet run --project src/NbTcgTrader.Api --launch-profile http
   ```
   Migrations auto-apply in Development. Readiness probe: `GET /auth/me` returns 401 when up (~20–40 s cold).
3. **Web** on http://localhost:5173: `npm run dev` in `web/` (background). CORS for 5173 is preconfigured in appsettings.Development.json.

## Drive

Playwright is not a project dep — install throwaway in the scratchpad (`npm i playwright && npx playwright install chromium`) and drive http://localhost:5173 headless.

## Gotchas

- **No user-secrets configured** (`UserSecretsId` missing from csproj): `Jwt__SigningKey` MUST come from env or the API 500s on token issue.
- **PowerShell 5.1**: `[RandomNumberGenerator]::Fill()` doesn't exist — use `::Create()` + `.GetBytes()`. A failed statement before `dotnet run` does NOT stop the launch; check env vars actually got set.
- **Password policy** (PersistenceExtensions.cs): 12+ chars, upper, lower, digit, symbol. Test password that works: `Correct-Horse-Battery-42`.
- **Refresh tokens are single-use** (rotation). Frontend dedupes concurrent refreshes (apiClient.ts single-flight); don't fire parallel /auth/refresh in tests.
- **Playwright `isVisible()` doesn't wait** — use `locator.waitFor()` for post-navigation assertions or you get false negatives during the auth boot "Loading…" state.
- **react-router v7 navigate() is a React transition**: sync setState after navigate re-renders the old route first (see AppShell handleLogout — logout must be in the same transition lane).

## Teardown

Stop the two background tasks; `docker compose stop db` (volume `pgdata` persists data).
