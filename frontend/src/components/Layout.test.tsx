import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import { setSession } from '../auth/tokenStorage'
import { Layout } from './Layout'

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

  it('hides Parametreler nav when unauthenticated', () => {
    renderLayout()

    expect(
      screen.queryByRole('link', { name: 'Parametreler' }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('navigation', { name: 'Ana menü' }),
    ).not.toBeInTheDocument()
  })

  it('shows Parametreler nav when authenticated', () => {
    setSession('test-token', {
      userId: 'user-1',
      fullName: 'Destek Kullanıcısı',
      username: 'support',
    })
    renderLayout('/parameters')

    const nav = screen.getByRole('navigation', { name: 'Ana menü' })
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

