# Deploy the frontend to Azure Static Web Apps (manual)

> **Superseded:** the Azure for Students subscription's region policy
> (`RequestDisallowedByAzure`) blocks every region SWA offers, so the frontend
> deploys to Vercel instead — see
> [deploy-frontend-vercel.md](deploy-frontend-vercel.md). This guide is kept
> in case the project moves to an unrestricted subscription.

Manual deployment of `web/` (React + Vite) to Azure Static Web Apps (SWA), per
issue #25. Pairs with the API deployment in
[deploy-azure-container-apps.md](deploy-azure-container-apps.md).

**Prerequisites**

- API already deployed and reachable over HTTPS (#24).
- Repo hosted on GitHub — SWA deploys through a GitHub Actions workflow it
  generates and commits itself.

**Cost:** SWA **Free** plan — $0, includes HTTPS, global CDN, and
`*.azurestaticapps.net` hostname.

---

## 1. SPA fallback config

`web/public/staticwebapp.config.json` (checked in) rewrites unknown paths to
`/index.html` so BrowserRouter routes survive a hard refresh. Vite copies
`public/` into `dist/`, which is where SWA reads it. No action needed — just
don't delete it.

## 2. Create the Static Web App

Portal → **Static Web Apps** → **Create**.

- Subscription / resource group: `Azure for Students` / `rg-nbtcg`
- Name: `nbtcg-web`
- Plan type: **Free**
- Region: pick the closest offered (hosting is CDN-backed; region only places
  the staging infrastructure)
- Deployment source: **GitHub** → authorize → select this repo and the
  `master` branch
- Build presets: **React**
  - App location: `/web`
  - Api location: *(empty — the API lives in Container Apps, not SWA functions)*
  - Output location: `dist`

**Create.** Azure then:

1. Commits a workflow file to `master`
   (`.github/workflows/azure-static-web-apps-<name>.yml`).
2. Adds the deployment token as a repo secret
   (`AZURE_STATIC_WEB_APPS_API_TOKEN_<NAME>`). The token never appears in
   source.

## 3. Point the build at the deployed API

Vite bakes `VITE_API_URL` in at **build time**, so it must be set in the
workflow, not in SWA configuration. `git pull` on `master`, then edit the
generated workflow's build/deploy step:

```yaml
      - name: Build And Deploy
        id: builddeploy
        uses: Azure/static-web-apps-deploy@v1
        env:
          VITE_API_URL: https://<api-fqdn>   # no trailing slash; not a secret
        with:
          # ...generated values stay as-is...
```

Commit and push — the workflow runs and publishes the site at
`https://<name>.<region-hash>.azurestaticapps.net` (exact URL on the SWA
Overview blade).

## 4. Allow the frontend origin in the API CORS

Container App `nbtcg-api` → **Containers → Edit and deploy** → environment
variables → add:

| Variable | Value |
|---|---|
| `Cors__AllowedOrigins__0` | `https://<name>.<region-hash>.azurestaticapps.net` (no trailing slash) |

Save → new revision. Exactly this origin — no wildcard.

## 5. Verify

1. Open the SWA URL — home page loads over HTTPS.
2. Deep-link refresh: navigate to a client route (e.g. `/marketplace`), press
   F5 — page renders instead of 404 (proves the fallback rewrite).
3. Marketplace browse + detail work anonymously.
4. Register/login — succeeds and authenticated pages (binder, add-card,
   import) call the deployed API (Network tab shows the ACA origin, no CORS
   errors).
5. Token refresh + logout behave (leave a tab open >15 min or trigger a
   refresh; logout clears the session).
6. DevTools console: no requests to `localhost` anywhere.

## Notes

- **PR previews:** the generated workflow also deploys a temporary preview
  environment per pull request. Its hostname differs per PR, so API calls from
  previews hit CORS unless that origin is added — fine to ignore for MVP.
- No frontend secrets exist: `VITE_API_URL` is public by nature (it ships in
  the JS bundle). Anything secret must stay API-side.
