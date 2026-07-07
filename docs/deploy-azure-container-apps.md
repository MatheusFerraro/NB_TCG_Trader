# Deploy the API to Azure Container Apps (manual)

Manual, portal-first deployment of the API container to Azure Container Apps
(ACA), per issue #24. CI/CD automation is out of scope here (#26). Names below
are suggestions — keep them consistent once chosen.

**Prerequisites**

- Azure subscription (portal access).
- Docker Desktop running locally (to build/push the image), or use `az acr build`
  to build in the cloud without local Docker.
- Azure CLI (`az`) logged in — only needed for the image push step; everything
  else works in the portal.
- Supabase database already provisioned and migrated (see README → Deployment
  notes for the migration step and the Session-pooler connection string).

Suggested region: **Canada Central** (closest to Moncton users).

---

## 1. Resource group

Portal → **Resource groups** → **Create**.

- Name: `rg-nbtcg`
- Region: Canada Central

## 2. Container registry (ACR)

Portal → **Container registries** → **Create**.

- Name: `nbtcgacr` (must be globally unique, alphanumeric only)
- SKU: **Basic** (~US$5/month; the only fixed cost in this setup)
- After creation: **Settings → Access keys → Enable admin user** (simplest for a
  manual push; switch to managed identity/OIDC if a CD pipeline is added later —
  existing CI (#26) only builds and tests, it does not push images).

## 3. Build and push the image

From the repo root:

```powershell
az acr login --name nbtcgacr
docker build -t nbtcgacr.azurecr.io/nbtcg-api:v1 .
docker push nbtcgacr.azurecr.io/nbtcg-api:v1
```

No local Docker? Build in the cloud instead:

```powershell
az acr build --registry nbtcgacr --image nbtcg-api:v1 .
```

Tag a new version (`v2`, `v3`, …) for each deploy; avoid `latest` so revisions
are traceable.

## 4. Container App + environment

Portal → **Container Apps** → **Create**. The wizard creates the Container Apps
environment and a Log Analytics workspace alongside the app.

**Basics**

- Name: `nbtcg-api`
- Region: Canada Central
- Environment: create new (defaults are fine; consumption-only)

**Container**

- Image source: Azure Container Registry → `nbtcgacr` → `nbtcg-api` → `v1`
- CPU / memory: 0.25 vCPU / 0.5 Gi (smallest tier)

**Ingress**

- Enabled, **Accepting traffic from anywhere** (external)
- Ingress type: HTTP
- Target port: **8080**

ACA terminates TLS at the ingress and forwards one proxy hop — this matches the
API's forwarded-headers setup (trusts exactly one hop). HTTPS works out of the
box on the generated `*.azurecontainerapps.io` hostname; no certificate work.

**Scale** (after creation: Application → Scale)

- Min replicas **0**, max **1**. Scale-to-zero keeps compute cost ≈ $0 at MVP
  traffic (consumption free grant), at the price of a cold start after idle.
  Set min 1 later if cold starts hurt demos.

## 5. Secrets

App → **Settings → Secrets** → add (secret names must be lowercase
alphanumeric/dashes):

| Secret name | Value |
|---|---|
| `connectionstrings-default` | Supabase **Session pooler** string (see README) |
| `jwt-signingkey` | Fresh ≥32-byte key for production — generate: `openssl rand -base64 48` |
| `cardapi-key` | pokemontcg.io API key (optional — skip if none) |

Generate the JWT key on Windows without openssl:

```powershell
$rng = [Security.Cryptography.RNGCryptoServiceProvider]::new(); $bytes = New-Object byte[] 48; $rng.GetBytes($bytes); [Convert]::ToBase64String($bytes)
```

(Windows PowerShell 5.1 compatible. If the output is all `A` characters, the
random fill failed — never use that value.)

Never reuse the local-dev signing key, and never commit any of these values.

## 6. Environment variables

App → **Application → Containers → Edit and deploy** → select the container →
**Environment variables**. Reference the secrets created above ("Reference a
secret"):

| Variable | Source | Value |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | manual | `Production` |
| `ConnectionStrings__Default` | secret | `connectionstrings-default` |
| `Jwt__SigningKey` | secret | `jwt-signingkey` |
| `Jwt__Issuer` | manual | `nb-tcg-trader` (optional; default in appsettings.json) |
| `Jwt__Audience` | manual | `nb-tcg-trader` (optional; default in appsettings.json) |
| `CardApi__Key` | secret | `cardapi-key` (optional — omit this variable if you did not create the secret) |
| `Cors__AllowedOrigins__0` | manual | Deployed frontend origin from #25, e.g. `https://<swa-host>.azurestaticapps.net`. Leave unset until then — unset = CORS locked down |
| `Catalog__PlaceholderImageUrl` | manual | `https://<app-fqdn>/assets/card-placeholder.svg` — see note below |

**Two-pass note:** `Catalog__PlaceholderImageUrl` needs the app's public FQDN
(App → Overview → Application Url), which only exists after the first deploy.
First deploy therefore crash-loops at startup (fail-fast by design). Copy the
FQDN, set the variable, and save — ACA creates a new revision that starts
cleanly.

## 7. Health probes (optional but recommended)

App → **Application → Containers → Edit and deploy** → container → **Health
probes**:

- Liveness + readiness: HTTP GET `/health`, port 8080.

ACA's default TCP probe also works; the HTTP probe just catches more failure
modes.

## 8. Verify

1. `https://<app-fqdn>/health` returns `{"status":"ok"}` over HTTPS.
2. `https://<app-fqdn>/scalar` returns **404** — correct: docs UI is
   Development-only.
3. Auth round-trip (register + login) — proves Supabase connectivity and JWT
   config:

   ```powershell
   $base = "https://<app-fqdn>"
   Invoke-RestMethod -Method Post -Uri "$base/auth/register" -ContentType "application/json" -Body '{"email":"smoke@example.com","password":"<test-password>","displayName":"Smoke","city":"Moncton","country":"Canada"}'
   Invoke-RestMethod -Method Post -Uri "$base/auth/login" -ContentType "application/json" -Body '{"email":"smoke@example.com","password":"<test-password>"}'
   ```

4. Catalog search — proves the card-provider call path:
   `GET https://<app-fqdn>/catalog/cards?query=pikachu`
5. Logs: App → **Monitoring → Log stream** (live) or **Logs** (Log Analytics).
   Spot-check that no connection strings, tokens, or keys appear — Serilog
   config never logs them, but verify once per environment.
6. CORS: after #25, browser calls from the frontend origin succeed; calls from
   other origins are blocked.

## Known gaps / later hardening

- **Data Protection keys** are stored on the container filesystem and are lost
  on restart/scale-to-zero — Identity tokens (password reset, email confirm)
  issued before a restart become invalid. Acceptable for MVP; durable key
  storage (e.g. blob container) is tracked as later hardening.
- **ACR admin user** is fine for manual pushes; move to managed identity or a
  scoped token if/when a CD pipeline (image push + deploy) is added.
- Custom domain + certificate: not needed for MVP; ACA supports both when a
  domain is chosen.
