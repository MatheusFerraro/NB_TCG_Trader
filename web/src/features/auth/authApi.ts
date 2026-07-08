import { API_URL, apiFetch } from '../../lib/apiClient'
import { clearTokens, getAccessToken, getRefreshToken, setTokens } from '../../lib/tokenStore'
import type {
  AuthResponse,
  LoginRequest,
  RegisterRequest,
  UpdateProfileRequest,
  User,
} from './types'

export async function login(request: LoginRequest): Promise<User> {
  const auth = await apiFetch<AuthResponse>('/auth/login', {
    method: 'POST',
    body: request,
    auth: false,
  })
  setTokens(auth.accessToken, auth.refreshToken)
  return auth.user
}

export async function register(request: RegisterRequest): Promise<User> {
  const auth = await apiFetch<AuthResponse>('/auth/register', {
    method: 'POST',
    body: request,
    auth: false,
  })
  setTokens(auth.accessToken, auth.refreshToken)
  return auth.user
}

/** Replaces the public profile; returns the updated user (same shape as /auth/me). */
export function updateProfile(request: UpdateProfileRequest): Promise<User> {
  return apiFetch<User>('/auth/me', { method: 'PUT', body: request })
}

/**
 * Signs out. Clears the in-memory access token and the persisted refresh token
 * immediately, then best-effort asks the server to revoke that refresh token so it
 * cannot outlive the session. The revoke uses a direct fetch (not apiFetch) so a
 * stale access token never triggers a refresh that would rotate the very token we
 * are trying to kill; a failed revoke is non-fatal — the token expires on its own.
 */
export function logout(): void {
  const accessToken = getAccessToken()
  const refreshToken = getRefreshToken()
  clearTokens()

  if (!accessToken || !refreshToken) {
    return
  }

  void fetch(`${API_URL}/auth/logout`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    body: JSON.stringify({ refreshToken }),
    keepalive: true,
  }).catch(() => {
    // Best-effort: the refresh token still expires server-side on its own schedule.
  })
}
