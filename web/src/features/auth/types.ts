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
