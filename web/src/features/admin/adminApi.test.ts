import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const API = 'http://localhost:5167'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

// apiClient keeps module-level state, so each test gets a fresh module instance.
// The access token lives in tokenStore memory (not localStorage), so it is set
// through the same fresh module registry the api module will import.
async function loadApi() {
  vi.resetModules()
  const tokenStore = await import('../../lib/tokenStore')
  tokenStore.setTokens('admin-token', 'refresh-token')
  return import('./adminApi')
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

describe('listUsers', () => {
  it('sends paging plus only the filled filters, with the bearer token', async () => {
    const { listUsers, ADMIN_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 1, pageSize: ADMIN_PAGE_SIZE, totalCount: 0 }),
    )

    await listUsers({ search: ' Misty ', city: '', locked: 'true' })

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(
      `${API}/admin/users?page=1&pageSize=${ADMIN_PAGE_SIZE}&search=Misty&locked=true`,
    )
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer admin-token')
  })

  it('surfaces a 403 for non-admins as ApiError with status 403', async () => {
    const { listUsers } = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(jsonResponse(403, { title: 'Forbidden', status: 403 }))

    const error = await listUsers({}).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).status).toBe(403)
  })
})

describe('lockUser / unlockUser', () => {
  it('posts the lock reason in the body', async () => {
    const { lockUser } = await loadApi()
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    await lockUser('user-1', 'Scam reports')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/admin/users/user-1/lock`)
    expect(init?.method).toBe('POST')
    expect(JSON.parse(String(init?.body))).toEqual({ reason: 'Scam reports' })
  })

  it('posts a null reason on unlock when none is given', async () => {
    const { unlockUser } = await loadApi()
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    await unlockUser('user-1')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/admin/users/user-1/unlock`)
    expect(JSON.parse(String(init?.body))).toEqual({ reason: null })
  })

  it('surfaces a 400 missing-reason response as ApiError with field errors', async () => {
    const { lockUser } = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Reason: ["'Reason' must not be empty."] },
      }),
    )

    const error = await lockUser('user-1', '').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      "'Reason' must not be empty.",
    ])
  })
})

describe('getUserDetail', () => {
  it('URL-encodes the user id', async () => {
    const { getUserDetail } = await loadApi()
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: 'a/b' }))

    await getUserDetail('a/b')

    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/admin/users/a%2Fb`)
  })
})

describe('getDashboard / getActivity / getAuditLog', () => {
  it('fetches the dashboard with the bearer token', async () => {
    const { getDashboard } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, {
        totalUsers: 2,
        newUsersLast7Days: 1,
        newUsersLast30Days: 2,
        lockedUsers: 0,
        activeListings: 5,
        failedImportsLast7Days: 0,
      }),
    )

    const dashboard = await getDashboard()

    expect(dashboard.totalUsers).toBe(2)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/admin/dashboard`)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer admin-token')
  })

  it('requests the given audit log page', async () => {
    const { getAuditLog, ADMIN_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 2, pageSize: ADMIN_PAGE_SIZE, totalCount: 30 }),
    )

    await getAuditLog(2)

    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/admin/audit-log?page=2&pageSize=${ADMIN_PAGE_SIZE}`)
  })
})
