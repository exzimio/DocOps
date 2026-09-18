export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  correlationId?: string
  errors?: Record<string, string[]>
}

/** Every non-2xx response from the API is surfaced as this error, carrying the ProblemDetails payload. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly problem: ProblemDetails
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.code = problem.code ?? `http_${status}`
    this.fieldErrors = problem.errors ?? {}
  }

  get isUnauthenticated(): boolean {
    return this.status === 401
  }

  get isForbidden(): boolean {
    return this.status === 403
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    // The session lives in an HTTP-only cookie; no token is ever read or written by this code.
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })

  if (response.status === 204) {
    return undefined as T
  }

  // Errors arrive as `application/problem+json`, successes as `application/json`; matching the
  // `+json` suffix too is what keeps the error code and the field errors from being thrown away.
  const contentType = response.headers.get('content-type') ?? ''
  const isJson = /^application\/([\w.-]+\+)?json\b/.test(contentType)
  const payload = isJson ? await response.json() : undefined

  if (!response.ok) {
    throw new ApiError(response.status, (payload as ProblemDetails) ?? { status: response.status })
  }

  return payload as T
}

export const api = {
  get: <T>(path: string, init?: RequestInit) => request<T>(path, { ...init, method: 'GET' }),
  post: <T>(path: string, body?: unknown, init?: RequestInit) =>
    request<T>(path, {
      ...init,
      method: 'POST',
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  put: <T>(path: string, body?: unknown, init?: RequestInit) =>
    request<T>(path, { ...init, method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  delete: <T>(path: string, init?: RequestInit) => request<T>(path, { ...init, method: 'DELETE' }),
}
