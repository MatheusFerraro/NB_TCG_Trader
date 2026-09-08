import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const API = 'http://localhost:5167'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function authBody(access: string, refresh: string) {
  return {
    accessToken: access,
    accessTokenExpiresAt: '2026-07-04T00:15:00Z',
    refreshToken: refresh,
    refreshTokenExpiresAt: '2026-07-11T00:00:00Z',
    user: {
      id: 'u1',
      email: 'a@b.c',
      displayName: 'Ash',
      city: null,
      country: null,
      contactEmail: null,
      discordHandle: null,
      instagramHandle: null,
    },
  }
}

// The client keeps module-level state (access token, in-flight refresh), so
// each test gets a fresh module instance.
async function loadClient() {
  vi.resetModules()
  const client = await import('./apiClient')
  const tokens = await import('./tokenStore')
  return { ...client, ...tokens }
}

const fetchMock = vi.fn<typeof fetch>()

beforeEach(() => {
  const store = new Map<string, string>()
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => void store.set(key, value),
    removeItem: (key: string) => void store.delete(key),
  })
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
})

describe('apiFetch', () => {
  it('resolves a bodiless 202 instead of choking on the empty stream', async () => {
    const { apiFetch } = await loadClient()
    // What /auth/password/forgot actually returns: accepted, nothing to parse.
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 202 }))

    await expect(apiFetch('/auth/password/forgot', { method: 'POST', auth: false }))
      .resolves.toBeUndefined()
  })

  it('resolves a 200 that carries no JSON content type', async () => {
    const { apiFetch } = await loadClient()
    fetchMock.mockResolvedValueOnce(new Response('', { status: 200 }))

    await expect(apiFetch('/health', { auth: false })).resolves.toBeUndefined()
  })

  it('attaches the Bearer token to authenticated requests', async () => {
    const { apiFetch, setTokens } = await loadClient()
    setTokens('access-1', 'refresh-1')
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { ok: true }))

    await apiFetch('/auth/me')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/auth/me`)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer access-1')
  })

  it('refreshes once and retries the request on a 401', async () => {
    const { apiFetch, setTokens } = await loadClient()
    setTokens('stale-access', 'refresh-1')
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(jsonResponse(200, authBody('fresh-access', 'refresh-2')))
      .mockResolvedValueOnce(jsonResponse(200, { ok: true }))

    const result = await apiFetch<{ ok: boolean }>('/collection')

    expect(result.ok).toBe(true)
    expect(fetchMock).toHaveBeenCalledTimes(3)
    const [refreshUrl, refreshInit] = fetchMock.mock.calls[1]
    expect(refreshUrl).toBe(`${API}/auth/refresh`)
    expect(JSON.parse(refreshInit?.body as string)).toEqual({ refreshToken: 'refresh-1' })
    const [, retryInit] = fetchMock.mock.calls[2]
    expect(new Headers(retryInit?.headers).get('Authorization')).toBe('Bearer fresh-access')
  })

  it('throws ApiError with ProblemDetails field errors on a 400', async () => {
    const { apiFetch, ApiError } = await loadClient()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Email: ["'Email' must not be empty."] },
      }),
    )

    const error = await apiFetch('/auth/login', {
      method: 'POST',
      body: {},
      auth: false,
    }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).status).toBe(400)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      "'Email' must not be empty.",
    ])
  })

  it('does not require FormData to exist for JSON requests', async () => {
    vi.stubGlobal('FormData', undefined)
    const { apiFetch } = await loadClient()
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { ok: true }))

    await apiFetch('/collection', {
      method: 'POST',
      body: { quantity: 1 },
    })

    const [, init] = fetchMock.mock.calls[0]
    expect(JSON.parse(init?.body as string)).toEqual({ quantity: 1 })
    expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
  })
})

describe('refreshSession', () => {
  it('deduplicates concurrent refresh calls into one request', async () => {
    const { refreshSession, setTokens } = await loadClient()
    setTokens('access-1', 'refresh-1')
    fetchMock.mockResolvedValue(jsonResponse(200, authBody('access-2', 'refresh-2')))

    const [first, second] = await Promise.all([refreshSession(), refreshSession()])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(first?.accessToken).toBe('access-2')
    expect(second?.accessToken).toBe('access-2')
  })

  it('clears tokens and returns null when the refresh token is rejected', async () => {
    const { refreshSession, setTokens, getRefreshToken, getAccessToken } = await loadClient()
    setTokens('access-1', 'refresh-1')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(401, { title: 'Authentication failed', status: 401 }),
    )

    const result = await refreshSession()

    expect(result).toBeNull()
    expect(getAccessToken()).toBeNull()
    expect(getRefreshToken()).toBeNull()
  })

  it('notifies listeners when the refresh token is rejected', async () => {
    const { refreshSession, setTokens, onAuthFailure } = await loadClient()
    const listener = vi.fn()
    setTokens('access-1', 'refresh-1')
    onAuthFailure(listener)
    fetchMock.mockResolvedValueOnce(
      jsonResponse(401, { title: 'Authentication failed', status: 401 }),
    )

    await refreshSession()

    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('returns null without a network call when no refresh token is stored', async () => {
    const { refreshSession } = await loadClient()

    const result = await refreshSession()

    expect(result).toBeNull()
    expect(fetchMock).not.toHaveBeenCalled()
  })
})
