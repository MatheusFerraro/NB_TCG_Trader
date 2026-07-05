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
  return import('./importApi')
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

const progress = {
  jobId: 7,
  status: 'NeedsReview',
  rowsTotal: 3,
  rowsMatched: 1,
  rowsUnmatched: 2,
}

describe('uploadImportJob', () => {
  it('posts the file as multipart form data, not JSON', async () => {
    const { uploadImportJob } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(201, {
        id: 7,
        fileName: 'cards.csv',
        status: 'NeedsReview',
        rowsTotal: 3,
        rowsMatched: 1,
        rowsUnmatched: 2,
        createdAt: '2026-07-05T00:00:00Z',
      }),
    )

    const file = new File(['card_name\nCharizard\n'], 'cards.csv', { type: 'text/csv' })
    const job = await uploadImportJob(file)

    expect(job.id).toBe(7)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/import/jobs`)
    expect(init?.method).toBe('POST')
    expect(init?.body).toBeInstanceOf(FormData)
    expect((init?.body as FormData).get('file')).toBe(file)
    // The browser must set the multipart boundary itself.
    expect(new Headers(init?.headers).get('Content-Type')).toBeNull()
  })

  it('surfaces ValidationProblem field errors as an ApiError', async () => {
    const { uploadImportJob } = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { file: ['Only .csv and .xlsx files are accepted.'] },
      }),
    )

    const file = new File(['nope'], 'cards.pdf', { type: 'application/pdf' })
    const error = await uploadImportJob(file).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      'Only .csv and .xlsx files are accepted.',
    ])
  })
})

describe('getImportRows', () => {
  it('requests the job rows', async () => {
    const { getImportRows } = await loadApi()
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { job: progress, rows: [] }))

    const result = await getImportRows(7)

    expect(result.job.rowsUnmatched).toBe(2)
    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/import/jobs/7/rows`)
  })
})

describe('resolveImportRow', () => {
  it('posts the chosen card external id as JSON', async () => {
    const { resolveImportRow } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, {
        job: progress,
        rowId: 12,
        matchStatus: 'ManuallyMatched',
        createdItemId: 99,
      }),
    )

    const result = await resolveImportRow(7, 12, 'base1-4')

    expect(result.createdItemId).toBe(99)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/import/jobs/7/rows/12/resolve`)
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual({ cardExternalId: 'base1-4' })
    expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
  })
})

describe('skipImportRow', () => {
  it('posts the skip with no body', async () => {
    const { skipImportRow } = await loadApi()
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { job: progress, rowId: 12, matchStatus: 'Skipped' }),
    )

    const result = await skipImportRow(7, 12)

    expect(result.matchStatus).toBe('Skipped')
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/import/jobs/7/rows/12/skip`)
    expect(init?.method).toBe('POST')
    expect(init?.body).toBeUndefined()
  })
})

describe('hasImportExtension', () => {
  it.each([
    ['cards.csv', true],
    ['cards.XLSX', true],
    ['cards.pdf', false],
    ['cards', false],
  ])('%s -> %s', async (name, expected) => {
    const { hasImportExtension } = await loadApi()
    expect(hasImportExtension(name)).toBe(expected)
  })
})
