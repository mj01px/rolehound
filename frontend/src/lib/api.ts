// In development Vite proxies /api to the local API. In production the API
// lives on its own subdomain of the same site, set at build time.
const baseUrl = import.meta.env.VITE_API_URL ?? '/api'

/** RFC 9457 problem details as the API sends them. */
export interface Problem {
  status: number
  code?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly problem: Problem

  constructor(problem: Problem) {
    super(problem.detail ?? `Request failed with ${problem.status}`)
    this.name = 'ApiError'
    this.problem = problem
  }

  get status(): number {
    return this.problem.status
  }

  get code(): string | undefined {
    return this.problem.code
  }

  /** First message for a field, from a validation problem. */
  field(name: string): string | undefined {
    return this.problem.errors?.[name]?.[0]
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

async function request<T>(method: Method, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const init: RequestInit = { method, credentials: 'include', signal: signal ?? null }
  if (body !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(body)
  }

  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, init)
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause
    }
    throw new ApiError({ status: 0, code: 'network', detail: 'Sem conexão com o servidor. Tente de novo.' })
  }

  if (!response.ok) {
    let problem: Problem = { status: response.status }
    try {
      problem = { ...((await response.json()) as Problem), status: response.status }
    } catch {
      // Not every error carries a body (a bare 401, for instance).
    }
    throw new ApiError(problem)
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>('GET', path, undefined, signal),
  post: <T = void>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T = void>(path: string, body?: unknown) => request<T>('PUT', path, body ?? {}),
}

/** A message a person can read, for any error thrown by the API client. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.problem.detail ?? 'Algo deu errado. Tente de novo.'
  }
  return 'Algo deu errado. Tente de novo.'
}
