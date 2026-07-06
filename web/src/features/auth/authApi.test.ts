import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const API = 'http://localhost:5167'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

// apiClient keeps module-level state, so each test gets a fresh module instance.
async function loadApi() {
  vi.resetModules()
  return import('./authApi')
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

describe('updateProfile', () => {
  const request = {
    displayName: 'Alice Cardeal',
    city: 'Moncton',
    country: 'Canada',
    contactEmail: 'alice@cards.test',
    discordHandle: null,
    instagramHandle: null,
  }

  it('PUTs the full profile with the bearer token and returns the user', async () => {
    const api = await loadApi()
    const { setTokens } = await import('../../lib/tokenStore')
    setTokens('access-token', 'refresh-token')
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: 'u1', ...request }))

    const user = await api.updateProfile(request)

    expect(user.displayName).toBe('Alice Cardeal')
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/auth/me`)
    expect(init?.method).toBe('PUT')
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer access-token')
    // Nulls must survive serialization: PUT semantics, null clears the field.
    expect(JSON.parse(init?.body as string)).toEqual(request)
  })

  it('surfaces validation problems as ApiError field errors', async () => {
    const api = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { ContactEmail: ["'Contact Email' is not a valid email address."] },
      }),
    )

    const error = await api
      .updateProfile({ ...request, contactEmail: 'not-an-email' })
      .catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      "'Contact Email' is not a valid email address.",
    ])
  })
})
