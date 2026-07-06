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
  return import('./marketplaceApi')
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

describe('browseListings', () => {
  it('sends only paging, without auth, when no filters are set', async () => {
    const { browseListings, MARKETPLACE_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 1, pageSize: MARKETPLACE_PAGE_SIZE, totalCount: 0 }),
    )

    await browseListings({})

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/marketplace?page=1&pageSize=${MARKETPLACE_PAGE_SIZE}`)
    expect(new Headers(init?.headers).get('Authorization')).toBeNull()
  })

  it('sends only the filled filters, trimmed', async () => {
    const { browseListings, MARKETPLACE_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 1, pageSize: MARKETPLACE_PAGE_SIZE, totalCount: 0 }),
    )

    await browseListings({
      name: ' Charizard ',
      game: '',
      set: 'Base',
      minPrice: '5',
      maxPrice: '150',
      currency: 'CAD',
      city: ' Moncton ',
      country: '',
    })

    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(
      `${API}/marketplace?page=1&pageSize=${MARKETPLACE_PAGE_SIZE}` +
        '&name=Charizard&set=Base&minPrice=5&maxPrice=150&currency=CAD&city=Moncton',
    )
  })

  it('requests the given page for the same filters', async () => {
    const { browseListings, MARKETPLACE_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 3, pageSize: MARKETPLACE_PAGE_SIZE, totalCount: 60 }),
    )

    const result = await browseListings({ name: 'Pikachu' }, 3)

    expect(result.page).toBe(3)
    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(
      `${API}/marketplace?page=3&pageSize=${MARKETPLACE_PAGE_SIZE}&name=Pikachu`,
    )
  })

  it('surfaces a 400 invalid-filter response as ApiError with field errors', async () => {
    const { browseListings } = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Currency: ["'Currency' is required when filtering by price."] },
      }),
    )

    const error = await browseListings({ minPrice: '5' }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      "'Currency' is required when filtering by price.",
    ])
  })
})

describe('getListing', () => {
  it('fetches the listing detail without auth', async () => {
    const { getListing } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, {
        id: 7,
        seller: { displayName: 'Alice', contactEmail: 'a@example.com' },
      }),
    )

    const listing = await getListing(7)

    expect(listing.id).toBe(7)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/marketplace/7`)
    expect(new Headers(init?.headers).get('Authorization')).toBeNull()
  })

  it('surfaces a 404 as ApiError with status 404', async () => {
    const { getListing } = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(404, {
        title: 'Listing not found',
        detail: 'No marketplace listing 99 exists.',
        status: 404,
      }),
    )

    const error = await getListing(99).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).status).toBe(404)
  })
})
