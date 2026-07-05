import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { formatPrice } from './format'
import type { UpdateItemRequest } from './types'

const API = 'http://localhost:5167'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

// apiClient keeps module-level state (in-flight refresh), so each test gets a
// fresh module instance — same pattern as apiClient.test.ts.
async function loadApi() {
  vi.resetModules()
  const api = await import('./collectionApi')
  const tokens = await import('../../lib/tokenStore')
  return { ...api, ...tokens }
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

describe('getBinder', () => {
  it('requests the binder page with the grid page size and Bearer token', async () => {
    const { getBinder, BINDER_PAGE_SIZE, setTokens } = await loadApi()
    setTokens('access-1', 'refresh-1')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { items: [], page: 2, pageSize: BINDER_PAGE_SIZE, totalCount: 0 }),
    )

    const result = await getBinder(2)

    expect(result.page).toBe(2)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/collection/me?page=2&pageSize=${BINDER_PAGE_SIZE}`)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer access-1')
  })
})

describe('updateItem', () => {
  const request: UpdateItemRequest = {
    quantity: 3,
    condition: 'LP',
    isForSale: true,
    price: 12.5,
    currency: 'CAD',
    isPrivate: false,
    notes: 'binder page 4',
  }

  it('PUTs the full desired state to the item endpoint', async () => {
    const { updateItem, setTokens } = await loadApi()
    setTokens('access-1', 'refresh-1')
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: 7 }))

    await updateItem(7, request)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/collection/items/7`)
    expect(init?.method).toBe('PUT')
    expect(JSON.parse(init?.body as string)).toEqual(request)
  })

  it('surfaces validation ProblemDetails as ApiError field errors', async () => {
    const { updateItem, setTokens } = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    setTokens('access-1', 'refresh-1')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Price: ['Price is required when the item is for sale.'] },
      }),
    )

    const error = await updateItem(7, { ...request, price: null }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      'Price is required when the item is for sale.',
    ])
  })
})

describe('formatPrice', () => {
  it('formats CAD with the en-CA locale', () => {
    expect(formatPrice(1234.5, 'CAD')).toMatch(/^(CA)?\$\s?1,234\.50$/)
  })

  it('formats BRL with the pt-BR locale', () => {
    // Intl separates "R$" from the amount with a non-breaking space.
    expect(formatPrice(1234.5, 'BRL')).toMatch(/^R\$[\s]1\.234,50$/)
  })
})
