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
  return import('./catalogApi')
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

describe('searchCatalog', () => {
  it('sends only the filled filters, trimmed, without auth', async () => {
    const { searchCatalog, CATALOG_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 1, pageSize: CATALOG_PAGE_SIZE, totalCount: 0 }),
    )

    await searchCatalog({ query: ' Charizard ', set: '', number: undefined })

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(
      `${API}/catalog/cards?page=1&pageSize=${CATALOG_PAGE_SIZE}&query=Charizard`,
    )
    expect(new Headers(init?.headers).get('Authorization')).toBeNull()
  })

  it('requests the given page for the same filters', async () => {
    const { searchCatalog, CATALOG_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 3, pageSize: CATALOG_PAGE_SIZE, totalCount: 55 }),
    )

    const result = await searchCatalog({ query: 'Pikachu' }, 3)

    expect(result.page).toBe(3)
    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(
      `${API}/catalog/cards?page=3&pageSize=${CATALOG_PAGE_SIZE}&query=Pikachu`,
    )
  })

  it('combines name, set, and number filters', async () => {
    const { searchCatalog, CATALOG_PAGE_SIZE } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 1, pageSize: CATALOG_PAGE_SIZE, totalCount: 0 }),
    )

    await searchCatalog({ query: 'Charizard', set: 'Base', number: '4' })

    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(
      `${API}/catalog/cards?page=1&pageSize=${CATALOG_PAGE_SIZE}&query=Charizard&set=Base&number=4`,
    )
  })
})
