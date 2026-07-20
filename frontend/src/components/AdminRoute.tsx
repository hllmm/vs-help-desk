import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function AdminRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, user } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (user?.role !== 'Admin') {
    return <Navigate to="/tickets" replace />
  }

  return children
}
