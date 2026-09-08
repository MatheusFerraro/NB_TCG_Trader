/** Mirrors the API's UserResponse (Features/Auth/AuthContracts.cs). */
export interface User {
  id: string
  email: string
  /** Whether the account email has been confirmed via the emailed link. */
  emailConfirmed: boolean
  displayName: string
  city: string | null
  country: string | null
  contactEmail: string | null
  discordHandle: string | null
  instagramHandle: string | null
  /** Identity role names, e.g. ["Admin"]. Empty for regular users. */
  roles: string[]
}

/** True when the user carries the Admin role (server still enforces the policy). */
export function isAdmin(user: User | null): boolean {
  return user?.roles.includes('Admin') ?? false
}

/** Mirrors the API's AuthResponse: token bundle from register/login/refresh. */
export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  user: User
}

export interface LoginRequest {
  email: string
  password: string
}

/**
 * PUT /auth/me body (UpdateProfile.cs). PUT semantics: every optional field is
 * the complete desired state — null clears it, so the form must always send
 * all fields. Account email/password are deliberately not editable here.
 */
export interface UpdateProfileRequest {
  displayName: string
  city: string | null
  country: string | null
  contactEmail: string | null
  discordHandle: string | null
  instagramHandle: string | null
}

export interface RegisterRequest {
  email: string
  password: string
  displayName: string
  city?: string
  country?: string
  contactEmail?: string
  discordHandle?: string
  instagramHandle?: string
}

/** POST /auth/email/verify body (VerifyEmail.cs). Both values come from the emailed link. */
export interface VerifyEmailRequest {
  userId: string
  token: string
}

/** POST /auth/email/verify/resend body (ResendVerificationEmail.cs). */
export interface ResendVerificationRequest {
  email: string
}

/** POST /auth/password/forgot body (ForgotPassword.cs). */
export interface ForgotPasswordRequest {
  email: string
}

/**
 * POST /auth/password/reset body (ResetPassword.cs). `email` and `token` are
 * carried by the emailed link; the token is single-use and short-lived.
 */
export interface ResetPasswordRequest {
  email: string
  token: string
  newPassword: string
}
