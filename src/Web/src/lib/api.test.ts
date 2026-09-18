import { describe, expect, it, vi } from 'vitest'
import { ApiError, api } from './api'
import { jsonResponse } from '@/test/renderWithProviders'

describe('api client', () => {
  it('sends credentials so the session cookie travels with every request', async () => {
    const fetchStub = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      jsonResponse({ ok: true }),
    )
    vi.stubGlobal('fetch', fetchStub)

    await api.get('/api/tenants/current')

    expect(fetchStub.mock.calls[0]?.[1]).toMatchObject({ credentials: 'include', method: 'GET' })
  })

  it('turns a ProblemDetails response into an ApiError carrying code and field errors', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse(
          {
            status: 400,
            title: 'Pedido inválido',
            detail: 'Um ou mais campos não passaram a validação.',
            code: 'validation_failed',
            correlationId: 'abc123',
            errors: { Email: ['Indique um endereço de e-mail válido.'] },
          },
          400,
        ),
      ),
    )

    const error = await api.post('/api/auth/login', { email: 'x' }).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(400)
    expect(apiError.code).toBe('validation_failed')
    expect(apiError.problem.correlationId).toBe('abc123')
    expect(apiError.fieldErrors.Email).toEqual(['Indique um endereço de e-mail válido.'])
  })

  it('flags an unauthenticated response so callers can distinguish it from a real failure', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ status: 401 }, 401)),
    )

    const error = (await api.get('/api/auth/me').catch((caught: unknown) => caught)) as ApiError

    expect(error.isUnauthenticated).toBe(true)
    expect(error.isForbidden).toBe(false)
  })

  it('returns undefined for a 204 response instead of attempting to parse a body', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 204 })),
    )

    await expect(api.post('/api/auth/logout')).resolves.toBeUndefined()
  })
})
