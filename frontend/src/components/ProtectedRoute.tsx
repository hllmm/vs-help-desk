import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/authState'

export function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isBootstrapping } = useAuth()
  const location = useLocation()

  if (isBootstrapping) {
    return (
      <p className="ticket-state ticket-state--loading" role="status">
        Oturum doğrulanıyor…
      </p>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return children
}
