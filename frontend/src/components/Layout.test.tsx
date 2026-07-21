import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UserRole } from '../api/types'
import { AuthProvider } from '../auth/AuthContext'
import { setStoredUser } from '../auth/tokenStorage'
import { Layout } from './Layout'

function seedAuthenticatedUser(role: UserRole = 'Support') {
  const user = {
    userId: 'user-1',
    fullName: 'Destek Kullanıcısı',
    username: 'support',
    role,
  }
  setStoredUser(user)
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/auth/me')) {
        return Promise.resolve(
          new Response(JSON.stringify(user), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
      }
      if (url.includes('/api/auth/logout')) {
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(new Response(null, { status: 404 }))
    }),
  )
}

function renderLayout(initialPath = '/') {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[initialPath]}>
        <Layout>
          <p>İçerik</p>
        </Layout>
      </MemoryRouter>
    </AuthProvider>,
  )
}

describe('Layout', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })

  it('renders skip-link header main and footer landmarks', () => {
    renderLayout()

    expect(
      screen.getByRole('link', { name: 'Ana içeriğe geç' }),
    ).toHaveAttribute('href', '#main-content')
    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main-content')
    expect(screen.getByRole('contentinfo')).toHaveTextContent(
      'VS Help Desk · Destek operasyonları',
    )
    expect(screen.getByText('Destek operasyonları')).toBeInTheDocument()
  })

  it('hides Admin nav links when unauthenticated', () => {
    // /me bootstrap fails → stays logged out
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
    )
    renderLayout()

    expect(
      screen.queryByRole('link', { name: 'Parametreler' }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Kullanıcılar' }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('navigation', { name: 'Ana menü' }),
    ).not.toBeInTheDocument()
  })

  it('hides Kullanıcılar and Parametreler nav for Support role', async () => {
    seedAuthenticatedUser('Support')
    renderLayout('/tickets')

    expect(await screen.findByRole('link', { name: 'Talepler' })).toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Parametreler' }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Kullanıcılar' }),
    ).not.toBeInTheDocument()
  })

  it('shows Kullanıcılar and Parametreler nav for Admin role', async () => {
    seedAuthenticatedUser('Admin')
    renderLayout('/users')

    const nav = await screen.findByRole('navigation', { name: 'Ana menü' })
    expect(
      await screen.findByRole('link', { name: 'Kullanıcılar' }),
    ).toHaveAttribute('href', '/users')
    expect(
      screen.getByRole('link', { name: 'Parametreler' }),
    ).toHaveAttribute('href', '/parameters')
    expect(screen.getByRole('link', { name: 'Talepler' })).toHaveAttribute(
      'href',
      '/tickets',
    )
    expect(nav).toBeInTheDocument()
  })

  it('contains no implementation or sprint jargon', () => {
    renderLayout()

    expect(document.body).not.toHaveTextContent(
      /UC-|JWT|sessionStorage|REST|Day|sprint/i,
    )
  })
})
