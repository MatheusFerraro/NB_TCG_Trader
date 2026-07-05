/**
 * Token storage for the auth flow (BACKLOG #19).
 *
 * The access token lives only in memory: it is short-lived (~15 min) and
 * keeping it out of localStorage narrows the XSS blast radius. The refresh
 * token is persisted in localStorage so the session survives a page refresh;
 * on boot the app trades it for a fresh pair via POST /auth/refresh.
 */

const REFRESH_TOKEN_KEY = 'nbtcg:refreshToken'

let accessToken: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function getRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_TOKEN_KEY)
  } catch {
    return null
  }
}

export function setTokens(access: string, refresh: string): void {
  accessToken = access
  try {
    localStorage.setItem(REFRESH_TOKEN_KEY, refresh)
  } catch {
    // Storage unavailable (private mode, quota): the session simply won't
    // survive a refresh, which is degraded but not broken.
  }
}

export function clearTokens(): void {
  accessToken = null
  try {
    localStorage.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    // Nothing to clean up if storage is unavailable.
  }
}
