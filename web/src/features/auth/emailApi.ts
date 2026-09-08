import { apiFetch } from '../../lib/apiClient'
import type {
  ForgotPasswordRequest,
  ResendVerificationRequest,
  ResetPasswordRequest,
  VerifyEmailRequest,
} from './types'

/**
 * Confirms an address from the emailed link. Resolves on 204; throws ApiError
 * with a 400 when the link is expired, malformed, or already spent.
 */
export function verifyEmail(request: VerifyEmailRequest): Promise<void> {
  return apiFetch<void>('/auth/email/verify', {
    method: 'POST',
    body: request,
    auth: false,
  })
}

/**
 * Asks for a fresh confirmation email. The API answers 202 whether or not the
 * address is registered, so a caller can never use this to probe for accounts —
 * the UI must show the same message either way.
 */
export function resendVerificationEmail(request: ResendVerificationRequest): Promise<void> {
  return apiFetch<void>('/auth/email/verify/resend', {
    method: 'POST',
    body: request,
    auth: false,
  })
}

/** Starts a password reset. Also 202 regardless of whether the account exists. */
export function forgotPassword(request: ForgotPasswordRequest): Promise<void> {
  return apiFetch<void>('/auth/password/forgot', {
    method: 'POST',
    body: request,
    auth: false,
  })
}

/**
 * Completes the reset with the token from the email. On success the server has
 * already revoked every refresh token, so the user signs in again from scratch.
 */
export function resetPassword(request: ResetPasswordRequest): Promise<void> {
  return apiFetch<void>('/auth/password/reset', {
    method: 'POST',
    body: request,
    auth: false,
  })
}
