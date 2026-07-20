import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { resolveSafeReturnPath } from '../auth/safeReturnPath'
import { MailWorkflowIllustration } from '../components/MailWorkflowIllustration'

function getLoginErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 401) {
    return 'Kullanıcı adı veya parola hatalı.'
  }
  if (error instanceof TypeError) {
    return 'Giriş hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
  }
  return 'Giriş yapılamadı. Lütfen yeniden deneyin.'
}

function getSessionExpiredMessage(search: string): string | null {
  return new URLSearchParams(search).get('reason') === 'session-expired'
    ? 'Oturumunuz sona erdi. Devam etmek için yeniden giriş yapın.'
    : null
}

export function LoginPage() {
  const { login, isAuthenticated } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = resolveSafeReturnPath(
    (location.state as { from?: string } | null)?.from,
  )

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [sessionNotice] = useState(() =>
    getSessionExpiredMessage(location.search),
  )
  const passwordRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!new URLSearchParams(location.search).has('reason')) {
      return
    }
    const params = new URLSearchParams(location.search)
    params.delete('reason')
    navigate(
      {
        pathname: '/login',
        search: params.size > 0 ? `?${params.toString()}` : '',
      },
      { replace: true, state: location.state },
    )
  }, [location.search, location.state, navigate])

  if (isAuthenticated) {
    return <Navigate to="/tickets" replace />
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      await login(username.trim(), password)
      navigate(from, { replace: true })
    } catch (err) {
      setError(getLoginErrorMessage(err))
      passwordRef.current?.focus()
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="login-layout portal-enter">
      <section className="login-intro">
        <h1 className="login-intro__title">
          Gelen e-postaları iş sırasına dönüştürün.
        </h1>
        <p className="login-intro__lead">
          Yeni talepleri, müşteri yanıtlarını ve son hareketleri tek ekranda
          izleyin.
        </p>
        <MailWorkflowIllustration />
      </section>

      <section className="login-card" aria-labelledby="login-title">
        <h2 id="login-title">Hesabınıza giriş yapın</h2>
        <p className="login-card__lead">
          Destek taleplerine erişmek için kullanıcı bilgilerinizi girin.
        </p>

        {sessionNotice ? (
          <p className="notice notice--info" role="status">
            {sessionNotice}
          </p>
        ) : null}

        <form
          onSubmit={onSubmit}
          className="form"
          aria-busy={submitting}
        >
          <label className="field">
            <span>Kullanıcı adı</span>
            <input
              name="username"
              autoComplete="username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              required
            />
          </label>
          <label className="field">
            <span>Parola</span>
            <input
              ref={passwordRef}
              name="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>
          {error ? (
            <p className="notice notice--error" role="alert">
              {error}
            </p>
          ) : null}
          <button
            type="submit"
            className="button button--primary"
            disabled={submitting}
          >
            {submitting ? 'Giriş yapılıyor…' : 'Giriş yap'}
          </button>
        </form>
      </section>
    </div>
  )
}
