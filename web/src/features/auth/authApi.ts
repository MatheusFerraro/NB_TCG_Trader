import { apiFetch } from '../../lib/apiClient'
import { clearTokens, setTokens } from '../../lib/tokenStore'
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
 * Client-side only: drops both tokens. The MVP API has no revoke endpoint;
 * the orphaned refresh token expires server-side on its own schedule.
 */
export function logout(): void {
  clearTokens()
}
