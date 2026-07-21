import { getCsrfToken } from '../auth/csrf'
import { clearSession } from '../auth/tokenStorage'

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
  /** @deprecated Cookie auth; kept for call-site clarity. Ignored for headers. */
  auth?: boolean
  skipAuthRedirect?: boolean
  signal?: AbortSignal
}

export type RedirectLocation = {
  pathname: string
  assign(url: string): void
}

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

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

function buildHeaders(options: RequestOptions): Headers {
  const headers = new Headers()
  headers.set('Accept', 'application/json')

  if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  return headers
}

async function sendRequest(
  path: string,
  options: RequestOptions = {},
): Promise<Response> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = buildHeaders(options)

  if (UNSAFE_METHODS.has(method)) {
    const csrf = getCsrfToken()
    if (csrf) {
      headers.set('X-CSRF-Token', csrf)
    }
  }

  const response = await fetch(buildApiUrl(path), {
    method,
    headers,
    credentials: 'include',
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  })

  if (response.status === 401 && !options.skipAuthRedirect) {
    expireSession()
    throw new ApiError(401, 'Unauthorized')
  }

  return response
}

function messageFromErrorBody(parsed: unknown, status: number): string {
  return typeof parsed === 'object' &&
    parsed !== null &&
    'message' in parsed &&
    typeof (parsed as { message: unknown }).message === 'string'
    ? (parsed as { message: string }).message
    : `Request failed (${status})`
}

async function parseErrorBody(response: Response): Promise<unknown> {
  const text = await response.text()
  if (!text) {
    return null
  }
  try {
    return JSON.parse(text) as unknown
  } catch {
    return text
  }
}

/**
 * Thin REST client — SPA talks only HTTP JSON to the ASP.NET Core API.
 * Auth is cookie-based (`credentials: 'include'`); no Authorization Bearer.
 */
export async function apiRequest<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const response = await sendRequest(path, options)

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
    throw new ApiError(
      response.status,
      messageFromErrorBody(parsed, response.status),
      parsed,
    )
  }

  return parsed as T
}

/**
 * Authenticated binary download helper. Cookie credentials only — never tokens in URLs.
 */
export async function apiBlobRequest(
  path: string,
  options: RequestOptions = {},
): Promise<Blob> {
  const response = await sendRequest(path, options)

  if (!response.ok) {
    const parsed = await parseErrorBody(response)
    throw new ApiError(
      response.status,
      messageFromErrorBody(parsed, response.status),
      parsed,
    )
  }

  return response.blob()
}

export function getApiBaseUrl(): string {
  return normalizeApiBaseUrl(
    import.meta.env.VITE_API_BASE_URL as string | undefined,
  )
}
