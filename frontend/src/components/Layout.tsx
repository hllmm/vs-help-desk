import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function Layout({ children }: { children: React.ReactNode }) {
  const { user, logout, isAuthenticated } = useAuth()

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Ana içeriğe geç
      </a>
      <header className="app-header">
        <div className="brand">
          <Link to={isAuthenticated ? '/tickets' : '/login'}>
            VS Help Desk
          </Link>
          <span className="brand-sub">Destek operasyonları</span>
        </div>
        {isAuthenticated && user ? (
          <div className="header-user">
            <span className="user-name">{user.fullName}</span>
            <span className="user-handle">@{user.username}</span>
            <button
              type="button"
              className="button button--quiet"
              onClick={logout}
            >
              Çıkış yap
            </button>
          </div>
        ) : null}
      </header>
      <main id="main-content" className="app-main">
        {children}
      </main>
      <footer className="app-footer">
        VS Help Desk · Destek operasyonları
      </footer>
    </div>
  )
}
