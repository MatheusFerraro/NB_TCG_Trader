# Browser session storage — decision & tradeoffs

Status: **accepted** for the MVP (issue #64). Revisit when the follow-up in
`BACKLOG.md` (unify frontend + API onto one registrable domain) lands.

## Model

| Token         | Where it lives                 | Lifetime            |
|---------------|--------------------------------|---------------------|
| Access token  | JavaScript memory only         | ~15 min             |
| Refresh token | `localStorage` (`nbtcg:refreshToken`) | 7 days, single-use, rotating |

- The access token is never persisted: a page reload drops it and the app trades
  the refresh token for a fresh pair via `POST /auth/refresh` on boot.
- The refresh token is single-use. Each refresh rotates it and revokes the old one
  atomically (`Refresh.cs`), so a captured-then-used token cannot mint two lines of
  successors.

## Why localStorage (and not a cookie) for now

The frontend (Vercel) and the API (Azure Container Apps) are on **different
registrable domains**. A refresh-token cookie would therefore have to be
`SameSite=None; Secure` — a cross-site cookie. Those are:

- Blocked by default in Safari (ITP) and by Firefox's Total Cookie Protection, so
  sessions would silently break for a large share of users.
- CSRF-exposed, requiring an anti-CSRF token layer we do not otherwise need.

A first-party `HttpOnly; Secure; SameSite=Lax` cookie — the actually-strong option
— is only available once both apps share one registrable domain (e.g. `app.` and
`api.` subdomains). Until then, `localStorage` is the pragmatic choice.

## Risk accepted

`localStorage` is readable by any JavaScript running on the origin, so a
**successful XSS** could exfiltrate the refresh token. We accept this for the MVP
and reduce its likelihood and blast radius with the compensating controls below.

## Compensating controls (in place)

- **Strict CSP** on the deployed frontend (`web/vercel.json`,
  `web/public/staticwebapp.config.json`): `script-src 'self'` (no inline scripts,
  no CDN), `object-src 'none'`, `frame-ancestors 'none'`, `base-uri 'self'`. This
  is the primary defense — it makes injected script execution hard in the first
  place. `img-src` allows the remote card images (`images.pokemontcg.io`);
  `connect-src` allows the API origin (`*.azurecontainerapps.io`).
  > When the API's real origin is fixed, tighten `connect-src` to that exact host.
- **Short access-token lifetime** (~15 min) and **rotating, single-use refresh
  tokens** — a leaked token has a narrow window and is invalidated the moment the
  legitimate client next refreshes.
- **Reuse detection** (`Refresh.cs`): replaying an already-rotated refresh token
  revokes the user's whole token family and logs a warning — a thief and the
  victim both lose the session.
- **Logout revocation** (`POST /auth/logout`): sign-out revokes the current refresh
  token server-side instead of leaving it live until expiry.
- Refresh tokens are stored **hashed** (SHA-256), so a database leak is not
  replayable; only the raw token (held by the client) is usable.

## Migration path (tracked in BACKLOG.md)

1. Serve frontend and API from one registrable domain (`app.` / `api.` subdomains).
2. Move the refresh token to an `HttpOnly; Secure; SameSite=Lax` first-party cookie
   set by the server; keep the access token in memory.
3. Drop refresh-token handling from `tokenStore.ts` / `authApi.ts`.
