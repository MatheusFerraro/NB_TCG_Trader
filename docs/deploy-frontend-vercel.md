# Deploy the frontend to Vercel (manual)

Deployment of `web/` (React + Vite) to Vercel, per issue #25. Vercel replaced
Azure Static Web Apps for this project: the Azure for Students subscription
region policy (`RequestDisallowedByAzure`) blocks every region SWA offers.
The SWA guide is kept for reference in
[deploy-azure-static-web-apps.md](deploy-azure-static-web-apps.md).

**Prerequisites**

- API already deployed and reachable over HTTPS (#24).
- GitHub account owning this repo.

**Cost:** Vercel **Hobby** plan — $0, includes HTTPS, global CDN,
`*.vercel.app` hostname, and automatic deploys on push.

---

## 1. SPA fallback config

`web/vercel.json` (checked in) rewrites unmatched paths to `/index.html` so
BrowserRouter routes survive a hard refresh. Vercel serves real files first;
the rewrite only applies when nothing matches, so assets are unaffected. No
action needed — just don't delete it.

## 2. Create the project

1. [vercel.com](https://vercel.com) → sign up / log in **with GitHub** (Hobby
   plan).
2. **Add New → Project** → import `NB_TCG_Trader`.
3. Configure:
   - **Root Directory:** `web`
   - **Framework Preset:** Vite (auto-detected)
   - Build command / output: defaults (`npm run build` / `dist`)
4. **Environment Variables** → add:

   | Name | Value | Environment |
   |---|---|---|
   | `VITE_API_URL` | `https://<api-fqdn>` (no trailing slash) | Production |

   Not a secret — it ships in the JS bundle. Vite bakes it in at build time,
   so changing it later requires a redeploy.
5. **Deploy.** Production URL appears as `https://<project>.vercel.app`.

Pushes to `master` now auto-deploy production; pull requests get preview
deployments (their hostnames differ, so previews hit API CORS — ignore for
MVP).

## 3. Allow the frontend origin in the API CORS

Container App `nbtcg-api` → **Containers → Edit and deploy** → environment
variables → add:

| Variable | Value |
|---|---|
| `Cors__AllowedOrigins__0` | `https://<project>.vercel.app` (no trailing slash) |

Save → new revision. Exactly this origin — no wildcard.

## 4. Verify

1. Open the Vercel URL — home page loads over HTTPS.
2. Deep-link refresh: navigate to a client route (e.g. `/marketplace`), press
   F5 — page renders instead of 404 (proves the rewrite).
3. Marketplace browse + detail work anonymously.
4. Register/login — succeeds and authenticated pages (binder, add-card,
   import) call the deployed API (Network tab shows the ACA origin, no CORS
   errors).
5. Token refresh + logout behave (leave a tab open >15 min or trigger a
   refresh; logout clears the session).
6. DevTools console: no requests to `localhost` anywhere.
