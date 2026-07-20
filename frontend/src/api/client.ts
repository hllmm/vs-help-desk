import { clearSession, getAccessToken } from '../auth/tokenStorage'

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, message: string, body: unknown = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export type RequestOptions = {
  method?: string
  body?: unknown
  auth?: boolean
  skipAuthRedirect?: boolean
  signal?: AbortSignal
}

export type RedirectLocation = {
  pathname: string
  assign(url: string): void
}

export function normalizeApiBaseUrl(
  value: string | undefined,
): string {
  return value?.trim().replace(/\/+$/, '') ?? ''
}

export function buildApiUrl(
  path: string,
  baseUrl = import.meta.env.VITE_API_BASE_URL as string | undefined,
): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  return `${normalizeApiBaseUrl(baseUrl)}${normalizedPath}`
}

export function expireSession(
  location: RedirectLocation = window.location,
): void {
  clearSession()
  if (location.pathname !== '/login') {
    location.assign('/login?reason=session-expired')
  }
}

/**
 * Thin REST client — SPA talks only HTTP JSON to the ASP.NET Core API.
 */
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers = new Headers()
  headers.set('Accept', 'application/json')

  if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  if (options.auth !== false) {
    const token = getAccessToken()
    if (token) {
      headers.set('Authorization', `Bearer ${token}`)
    }
  }

  const response = await fetch(buildApiUrl(path), {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  })

  if (response.status === 401 && !options.skipAuthRedirect) {
    expireSession()
    throw new ApiError(401, 'Unauthorized')
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  let parsed: unknown = null
  if (text) {
    try {
      parsed = JSON.parse(text) as unknown
    } catch {
      parsed = text
    }
  }

  if (!response.ok) {
    const message =
      typeof parsed === 'object' &&
      parsed !== null &&
      'message' in parsed &&
      typeof (parsed as { message: unknown }).message === 'string'
        ? (parsed as { message: string }).message
        : `Request failed (${response.status})`
    throw new ApiError(response.status, message, parsed)
  }

  return parsed as T
}

export function getApiBaseUrl(): string {
  return normalizeApiBaseUrl(
    import.meta.env.VITE_API_BASE_URL as string | undefined,
  )
}
