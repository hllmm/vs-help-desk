import { beforeEach, describe, expect, it, vi } from 'vitest'
import { login } from './authApi'
import {
  apiBlobRequest,
  apiRequest,
  buildApiUrl,
  expireSession,
  normalizeApiBaseUrl,
  type RedirectLocation,
} from './client'

function clearCsrfCookie() {
  document.cookie = 'vshd.csrf=; Max-Age=0; path=/'
}

describe('API URL handling', () => {
  it.each([undefined, '', '   '])(
    'uses same-origin for %s',
    (value) => {
      expect(normalizeApiBaseUrl(value)).toBe('')
      expect(buildApiUrl('/api/tickets', value)).toBe('/api/tickets')
    },
  )

  it('trims every trailing slash from an explicit base', () => {
    expect(buildApiUrl('/api/tickets', 'https://api.example.test///'))
      .toBe('https://api.example.test/api/tickets')
  })

  it('never constructs an undefined URL', () => {
    expect(buildApiUrl('/api/auth/login', undefined))
      .not.toContain('undefined')
  })
})

describe('expireSession', () => {
  it('removes session keys and assigns session-expired reason', () => {
    sessionStorage.setItem('vshd.accessToken', 'token')
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )

    const assign = vi.fn()
    const location: RedirectLocation = {
      pathname: '/tickets',
      assign,
    }

    expireSession(location)

    expect(sessionStorage.getItem('vshd.accessToken')).toBeNull()
    expect(sessionStorage.getItem('vshd.user')).toBeNull()
    expect(assign).toHaveBeenCalledWith('/login?reason=session-expired')
  })

  it('does not redirect when already on /login', () => {
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )
    const assign = vi.fn()

    expireSession({ pathname: '/login', assign })

    expect(sessionStorage.getItem('vshd.user')).toBeNull()
    expect(assign).not.toHaveBeenCalled()
  })
})

describe('apiRequest', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearCsrfCookie()
    vi.unstubAllGlobals()
  })

  it('sends credentials include and no Authorization header', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/tickets')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
  })

  it('adds X-CSRF-Token on POST when csrf cookie present', async () => {
    document.cookie = 'vshd.csrf=test-csrf'
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/tickets/1/resolve', { method: 'POST' })

    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('X-CSRF-Token')).toBe('test-csrf')
    expect(headers.get('Authorization')).toBeNull()
  })

  it('does not add X-CSRF-Token on GET even when csrf cookie present', async () => {
    document.cookie = 'vshd.csrf=test-csrf'
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/tickets')

    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('X-CSRF-Token')).toBeNull()
  })

  it('passes the exact AbortSignal to fetch', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/tickets', { signal: controller.signal })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      signal: controller.signal,
      credentials: 'include',
    })
  })

  it('on protected 401 removes session keys via expireSession', async () => {
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )

    // Redirect URL is asserted through the fake location seam (not jsdom window.location).
    const assign = vi.fn()
    expireSession({ pathname: '/tickets', assign })
    expect(assign).toHaveBeenCalledWith('/login?reason=session-expired')

    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
    )

    await expect(apiRequest('/api/tickets')).rejects.toMatchObject({
      status: 401,
      message: 'Unauthorized',
    })

    expect(sessionStorage.getItem('vshd.user')).toBeNull()
  })

  it('does not clear session when skipAuthRedirect is true', async () => {
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
    )

    await expect(
      apiRequest('/api/auth/login', {
        method: 'POST',
        body: { username: 'u', password: 'p' },
        auth: false,
        skipAuthRedirect: true,
      }),
    ).rejects.toMatchObject({ status: 401 })

    expect(sessionStorage.getItem('vshd.user')).not.toBeNull()
  })

  it('login API continues to set skipAuthRedirect: true', async () => {
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ message: 'Invalid credentials' }), {
          status: 401,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )

    await expect(
      login({ username: 'u', password: 'p' }),
    ).rejects.toMatchObject({ status: 401 })

    expect(sessionStorage.getItem('vshd.user')).not.toBeNull()
  })

  it('login response type has no accessToken field', async () => {
    const body = {
      userId: 'user-1',
      fullName: 'Destek Kullanıcısı',
      username: 'support',
      role: 'Support',
    }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )

    const result = await login({ username: 'support', password: 'secret' })

    expect(result).toEqual(body)
    expect(result).not.toHaveProperty('accessToken')
  })
})

describe('apiBlobRequest', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearCsrfCookie()
    vi.unstubAllGlobals()
  })

  it('reuses cookie credentials and passes AbortSignal without Authorization', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(new TextEncoder().encode('abc'), {
        status: 200,
        headers: { 'Content-Type': 'application/pdf' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await apiBlobRequest('/api/attachments/1', { signal: controller.signal })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      signal: controller.signal,
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
    expect(headers.has('Content-Type')).toBe(false)
  })

  it('on protected 401 removes session via expireSession', async () => {
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test User',
        username: 'test',
        role: 'Support',
      }),
    )

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
    )

    await expect(apiBlobRequest('/api/attachments/1')).rejects.toMatchObject({
      status: 401,
      message: 'Unauthorized',
    })

    expect(sessionStorage.getItem('vshd.user')).toBeNull()
  })

  it('throws ApiError for 404/5xx without parsing success as JSON', async () => {
    const notFoundBody = { status: 404, title: 'missing' }
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(
          new Response(JSON.stringify(notFoundBody), {
            status: 404,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
        .mockResolvedValueOnce(
          new Response(JSON.stringify({ message: 'boom' }), {
            status: 500,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
        .mockResolvedValueOnce(
          new Response(new Uint8Array([1, 2, 3]), {
            status: 200,
            headers: { 'Content-Type': 'application/octet-stream' },
          }),
        ),
    )

    await expect(apiBlobRequest('/api/attachments/missing')).rejects.toMatchObject(
      {
        status: 404,
        body: notFoundBody,
      },
    )

    await expect(apiBlobRequest('/api/attachments/broken')).rejects.toMatchObject(
      {
        status: 500,
        message: 'boom',
      },
    )

    const blob = await apiBlobRequest('/api/attachments/ok')
    expect(blob.type).toBe('application/octet-stream')
    expect(await blob.arrayBuffer()).toEqual(new Uint8Array([1, 2, 3]).buffer)
  })
})
