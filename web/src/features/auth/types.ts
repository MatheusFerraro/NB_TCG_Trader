/** Mirrors the API's UserResponse (Features/Auth/AuthContracts.cs). */
export interface User {
  id: string
  email: string
  displayName: string
  city: string | null
  country: string | null
  contactEmail: string | null
  discordHandle: string | null
  instagramHandle: string | null
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
