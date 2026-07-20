import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function Layout({ children }: { children: React.ReactNode }) {
  const { user, logout, isAuthenticated } = useAuth()

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand">
          <Link to={isAuthenticated ? '/tickets' : '/login'}>VS Help Desk</Link>
          <span className="brand-sub">Support portal</span>
        </div>
        {isAuthenticated && user ? (
          <div className="header-user">
            <span className="user-name">{user.fullName}</span>
            <span className="user-handle">@{user.username}</span>
            <button type="button" className="btn btn-ghost" onClick={logout}>
              Log out
            </button>
          </div>
        ) : null}
      </header>
      <main className="app-main">{children}</main>
      <footer className="app-footer">
        REST-only React SPA · API JWT bearer · Day 14
      </footer>
    </div>
  )
}
