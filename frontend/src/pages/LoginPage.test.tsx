import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'

function mockFetch(
  handler: (url: string, init?: RequestInit) => Response | Promise<Response>,
) {
  const fetchMock = vi.fn(
    (input: RequestInfo | URL, init?: RequestInit) => {
      const url =
        typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.href
            : input.url
      return Promise.resolve(handler(url, init))
    },
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderAt(path: string) {
  window.history.pushState({}, '', path)
  return render(<App />)
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('LoginPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState({}, '', '/')
  })

  it('logs in and reaches the protected ticket list', async () => {
    const user = userEvent.setup()
    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      if (url.includes('/api/auth/login')) {
        return jsonResponse({
          userId: 'user-1',
          fullName: 'Destek Kullanıcısı',
          username: 'support',
          role: 'Support',
        })
      }
      if (url.includes('/api/tickets')) {
        return jsonResponse([])
      }
      return jsonResponse({ message: 'not found' }, 404)
    })

    renderAt('/login')

    await user.type(screen.getByLabelText('Kullanıcı adı'), 'support')
    await user.type(screen.getByLabelText('Parola'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Giriş yap' }))

    await waitFor(() => {
      expect(window.location.pathname).toBe('/tickets')
    })
    expect(sessionStorage.getItem('vshd.accessToken')).toBeNull()
    expect(sessionStorage.getItem('vshd.user')).not.toBeNull()
  })


  it('keeps invalid credentials on login and focuses password', async () => {
    const user = userEvent.setup()
    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      if (url.includes('/api/auth/login')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      return jsonResponse({ message: 'not found' }, 404)
    })


    renderAt('/login')

    await user.type(screen.getByLabelText('Kullanıcı adı'), 'support')
    await user.type(screen.getByLabelText('Parola'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Giriş yap' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Kullanıcı adı veya parola hatalı.',
    )
    expect(screen.getByLabelText('Parola')).toHaveFocus()
    expect(window.location.pathname).toBe('/login')
  })

  it('shows allow-listed expiry copy and removes reason from the URL', async () => {
    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      return jsonResponse({ message: 'not found' }, 404)
    })
    renderAt('/login?reason=session-expired')

    expect(screen.getByRole('status')).toHaveTextContent(
      'Oturumunuz sona erdi. Devam etmek için yeniden giriş yapın.',
    )

    await waitFor(() => {
      expect(window.location.search).toBe('')
    })
    expect(window.location.pathname).toBe('/login')
  })

  it('never renders an arbitrary reason query', async () => {
    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      return jsonResponse({ message: 'not found' }, 404)
    })
    renderAt('/login?reason=raw-backend-stack-trace')

    await waitFor(() => {
      expect(window.location.search).toBe('')
    })

    expect(document.body).not.toHaveTextContent('raw-backend-stack-trace')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })


  it('uses Turkish network and server failure copy', async () => {
    const user = userEvent.setup()

    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      throw new TypeError('Failed to fetch')
    })
    renderAt('/login')
    await user.type(screen.getByLabelText('Kullanıcı adı'), 'support')
    await user.type(screen.getByLabelText('Parola'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Giriş yap' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Giriş hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
    )

    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      if (url.includes('/api/auth/login')) {
        return jsonResponse({ message: 'boom' }, 500)
      }
      return jsonResponse({ message: 'not found' }, 404)
    })
    await user.click(screen.getByRole('button', { name: 'Giriş yap' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Giriş yapılamadı. Lütfen yeniden deneyin.',
    )
  })

  it('contains no implementation or sprint jargon', () => {
    mockFetch((url) => {
      if (url.includes('/api/auth/me')) {
        return jsonResponse({ message: 'Unauthorized' }, 401)
      }
      return jsonResponse({ message: 'not found' }, 404)
    })
    renderAt('/login')

    expect(document.body).not.toHaveTextContent(
      /UC-|JWT|sessionStorage|REST|Day|sprint/i,
    )
  })
})

