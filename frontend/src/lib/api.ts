// In development Vite proxies /api to the local API. In production the API
// lives on its own subdomain of the same site, set at build time.
const baseUrl = import.meta.env.VITE_API_URL ?? '/api'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, { credentials: 'include', signal })
  if (!response.ok) {
    throw new ApiError(response.status, `GET ${path} failed with ${response.status}`)
  }
  const type = response.headers.get('content-type') ?? ''
  return (type.includes('json') ? await response.json() : await response.text()) as T
}
