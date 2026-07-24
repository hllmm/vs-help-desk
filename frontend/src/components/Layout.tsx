import {
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/authState'

const MOBILE_NAV_QUERY = '(max-width: 47.99rem)'

function useMobileNavigation(): boolean {
  const getSnapshot = () =>
    typeof window.matchMedia === 'function' &&
    window.matchMedia(MOBILE_NAV_QUERY).matches
  const [isMobile, setIsMobile] = useState(getSnapshot)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') {
      return
    }

    const media = window.matchMedia(MOBILE_NAV_QUERY)
    const onChange = (event: MediaQueryListEvent) => {
      setIsMobile(event.matches)
    }
    setIsMobile(media.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  return isMobile
}

export function Layout({ children }: { children: ReactNode }) {
  const { user, logout, isAuthenticated } = useAuth()
  const location = useLocation()
  const isMobile = useMobileNavigation()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const previousPath = useRef(location.pathname)
  const navigationHidden = isMobile && !menuOpen

  useEffect(() => {
    if (previousPath.current !== location.pathname) {
      previousPath.current = location.pathname
      setMenuOpen(false)
    }
  }, [location.pathname])

  useEffect(() => {
    if (!isMobile) {
      setMenuOpen(false)
    }
  }, [isMobile])

  useEffect(() => {
    if (!menuOpen) {
      return
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') {
        return
      }
      event.preventDefault()
      setMenuOpen(false)
      queueMicrotask(() => menuButtonRef.current?.focus())
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [menuOpen])

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

        {isAuthenticated && isMobile ? (
          <button
            ref={menuButtonRef}
            type="button"
            className="button button--quiet app-header__menu-trigger"
            aria-expanded={menuOpen}
            aria-controls="app-navigation-panel"
            onClick={() => setMenuOpen((current) => !current)}
          >
            {menuOpen ? 'Menüyü kapat' : 'Menüyü aç'}
          </button>
        ) : null}

        {isAuthenticated && user ? (
          <div
            id="app-navigation-panel"
            className="app-header__panel"
            hidden={navigationHidden}
          >
            <nav className="app-nav" aria-label="Ana menü">
              <NavLink
                to="/tickets"
                className={({ isActive }) =>
                  isActive
                    ? 'app-nav__link app-nav__link--active'
                    : 'app-nav__link'
                }
              >
                Talepler
              </NavLink>
              {user.role === 'Admin' ? (
                <>
                  <NavLink
                    to="/users"
                    className={({ isActive }) =>
                      isActive
                        ? 'app-nav__link app-nav__link--active'
                        : 'app-nav__link'
                    }
                  >
                    Kullanıcılar
                  </NavLink>
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
                </>
              ) : null}
            </nav>
            <div className="header-user">
              <span className="user-name">{user.fullName}</span>
              <span className="user-handle">@{user.username}</span>
              <button
                type="button"
                className="button button--quiet"
                onClick={() => void logout()}
              >
                Çıkış yap
              </button>
            </div>
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
