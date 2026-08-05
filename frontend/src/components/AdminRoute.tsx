import { Navigate, useLocation } from 'react-router'
import { useAuth } from '../auth/authState'

export function AdminRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isBootstrapping, user } = useAuth()
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

  if (user?.role !== 'Admin') {
    return <Navigate to="/tickets" replace />
  }

  return children
}
