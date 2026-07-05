import { clearTokens, getAccessToken, getRefreshToken, setTokens } from './tokenStore'
import type { AuthResponse } from '../features/auth/types'

export const API_URL: string = import.meta.env.VITE_API_URL ?? 'http://localhost:5167'

/** RFC 7807 ProblemDetails as returned by the API for every failure. */
export interface ProblemDetails {
  type?: string
  title?: string
  detail?: string
  status?: number
  /** ValidationProblem field errors, e.g. { Email: ["'Email' must not be empty."] } */
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  /** Flattens ValidationProblem field errors into displayable lines. */
  get fieldErrors(): string[] {
    return Object.values(this.problem?.errors ?? {}).flat()
  }
}

async function parseProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

/**
 * Single-flight refresh: concurrent callers (a burst of 401s, or React
 * StrictMode double-running the boot effect) share one in-flight request.
 * Refresh tokens are single-use on the server — a second parallel POST
 * /auth/refresh with the same token would be rejected and kill the session.
 */
let refreshPromise: Promise<AuthResponse | null> | null = null
const authFailureListeners = new Set<() => void>()

export function onAuthFailure(listener: () => void): () => void {
  authFailureListeners.add(listener)
  return () => {
    authFailureListeners.delete(listener)
  }
}

function notifyAuthFailure(): void {
  for (const listener of authFailureListeners) {
    listener()
  }
}

export function refreshSession(): Promise<AuthResponse | null> {
  refreshPromise ??= doRefresh().finally(() => {
    refreshPromise = null
  })
  return refreshPromise
}

async function doRefresh(): Promise<AuthResponse | null> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) {
    return null
  }

  const response = await fetch(`${API_URL}/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })

  if (!response.ok) {
    clearTokens()
    notifyAuthFailure()
    return null
  }

  const auth = (await response.json()) as AuthResponse
  setTokens(auth.accessToken, auth.refreshToken)
  return auth
}

export interface ApiFetchOptions extends Omit<RequestInit, 'body'> {
  /** JSON-serialized unless it is a FormData (multipart upload). */
  body?: unknown
  /** Set false for anonymous endpoints (login/register). Default true. */
  auth?: boolean
}

/**
 * JSON fetch wrapper: prefixes the API base URL, attaches the Bearer token,
 * and on a 401 refreshes the session once and retries. Throws ApiError on any
 * non-2xx response; returns the parsed JSON body (undefined for 204).
 * A FormData body is sent as-is so the browser sets the multipart boundary.
 */
export async function apiFetch<T>(path: string, options: ApiFetchOptions = {}): Promise<T> {
  const { body, auth = true, headers, ...init } = options
  const isFormData = body instanceof FormData

  const doFetch = () => {
    const requestHeaders = new Headers(headers)
    if (body !== undefined && !isFormData) {
      requestHeaders.set('Content-Type', 'application/json')
    }
    const token = getAccessToken()
    if (auth && token) {
      requestHeaders.set('Authorization', `Bearer ${token}`)
    }
    return fetch(`${API_URL}${path}`, {
      ...init,
      headers: requestHeaders,
      body: body === undefined ? undefined : isFormData ? body : JSON.stringify(body),
    })
  }

  let response = await doFetch()

  if (response.status === 401 && auth) {
    const refreshed = await refreshSession()
    if (refreshed) {
      response = await doFetch()
    }
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response))
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
