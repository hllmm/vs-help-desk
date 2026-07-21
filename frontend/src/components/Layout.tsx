import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function Layout({ children }: { children: React.ReactNode }) {
  const { user, logout, isAuthenticated } = useAuth()

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Ana içeriğe geç
      </a>
      <header className="app-header">
        <div className="header-brand-nav">
          <div className="brand">
            <Link to={isAuthenticated ? '/tickets' : '/login'}>
              VS Help Desk
            </Link>
            <span className="brand-sub">Destek operasyonları</span>
          </div>
          {isAuthenticated ? (
            <nav className="app-nav" aria-label="Ana menü">
              <NavLink
                to="/tickets"
                className={({ isActive }) =>
                  isActive ? 'app-nav__link app-nav__link--active' : 'app-nav__link'
                }
              >
                Talepler
              </NavLink>
              {user?.role === 'Admin' ? (
                <NavLink
                  to="/parameters"
                  className={({ isActive }) =>
                    isActive
                      ? 'app-nav__link app-nav__link--active'
                      : 'app-nav__link'
                  }
                >
                  Parametreler
                </NavLink>
              ) : null}
            </nav>
          ) : null}
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
