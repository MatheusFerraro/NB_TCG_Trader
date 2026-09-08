import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const API = 'http://localhost:5167'

function noContent(): Response {
  return new Response(null, { status: 204 })
}

function accepted(): Response {
  return new Response(null, { status: 202 })
}

function problem(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

// apiClient keeps module-level state, so each test gets a fresh module instance.
async function loadApi() {
  vi.resetModules()
  return import('./emailApi')
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

describe('verifyEmail', () => {
  it('POSTs the link values anonymously and resolves on 204', async () => {
    const api = await loadApi()
    fetchMock.mockResolvedValueOnce(noContent())

    await api.verifyEmail({ userId: 'user-1', token: 'encoded-token' })

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/auth/email/verify`)
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual({
      userId: 'user-1',
      token: 'encoded-token',
    })
    // These pages are reachable while signed out; an Authorization header would
    // be meaningless and a 401 retry loop is worse than none.
    expect(new Headers(init?.headers).has('Authorization')).toBe(false)
  })

  it('throws ApiError carrying the ProblemDetails detail on an expired link', async () => {
    const api = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      problem(400, {
        title: 'Confirmation failed',
        detail: 'This confirmation link is invalid or has expired. Request a new one.',
        status: 400,
      }),
    )

    const error = await api
      .verifyEmail({ userId: 'user-1', token: 'stale' })
      .catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).status).toBe(400)
    expect((error as Error).message).toContain('invalid or has expired')
  })

  it('does not retry via refresh when the server rejects the link', async () => {
    const api = await loadApi()
    const { setTokens } = await import('../../lib/tokenStore')
    setTokens('access-token', 'refresh-token')
    fetchMock.mockResolvedValueOnce(problem(401, { title: 'Unauthorized', status: 401 }))

    await api.verifyEmail({ userId: 'user-1', token: 'x' }).catch(() => undefined)

    // auth:false means a 401 is final: exactly one call, no POST /auth/refresh.
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})

describe('resendVerificationEmail', () => {
  it('POSTs the address and resolves on the non-committal 202', async () => {
    const api = await loadApi()
    fetchMock.mockResolvedValueOnce(accepted())

    await api.resendVerificationEmail({ email: 'alice@cards.test' })

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/auth/email/verify/resend`)
    expect(JSON.parse(init?.body as string)).toEqual({ email: 'alice@cards.test' })
  })
})

describe('forgotPassword', () => {
  it('resolves on 202 for an unregistered address, exactly as for a real one', async () => {
    const api = await loadApi()
    fetchMock.mockResolvedValueOnce(accepted())

    await api.forgotPassword({ email: 'nobody@cards.test' })

    expect(fetchMock.mock.calls[0][0]).toBe(`${API}/auth/password/forgot`)
  })

  it('surfaces the rate limiter so the UI can tell the user to wait', async () => {
    const api = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429 }))

    const error = await api
      .forgotPassword({ email: 'alice@cards.test' })
      .catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).status).toBe(429)
  })
})

describe('resetPassword', () => {
  it('POSTs email, token and the new password anonymously', async () => {
    const api = await loadApi()
    fetchMock.mockResolvedValueOnce(noContent())

    await api.resetPassword({
      email: 'alice@cards.test',
      token: 'encoded-token',
      newPassword: 'CorrectHorse!23',
    })

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API}/auth/password/reset`)
    expect(JSON.parse(init?.body as string)).toEqual({
      email: 'alice@cards.test',
      token: 'encoded-token',
      newPassword: 'CorrectHorse!23',
    })
    expect(new Headers(init?.headers).has('Authorization')).toBe(false)
  })

  it('exposes password-policy field errors from the ValidationProblem', async () => {
    const api = await loadApi()
    const { ApiError } = await import('../../lib/apiClient')
    fetchMock.mockResolvedValueOnce(
      problem(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { NewPassword: ['Passwords must have at least one digit.'] },
      }),
    )

    const error = await api
      .resetPassword({ email: 'a@b.test', token: 't', newPassword: 'nodigitshere' })
      .catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as InstanceType<typeof ApiError>).fieldErrors).toEqual([
      'Passwords must have at least one digit.',
    ])
  })
})
