import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import { Layout } from './Layout'

function renderLayout() {
  return render(
    <AuthProvider>
      <MemoryRouter>
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

  it('contains no implementation or sprint jargon', () => {
    renderLayout()

    expect(document.body).not.toHaveTextContent(
      /UC-|JWT|sessionStorage|REST|Day|sprint/i,
    )
  })
})
