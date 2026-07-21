import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { AdminRoute } from './components/AdminRoute'
import { ProtectedRoute } from './components/ProtectedRoute'
import { LoginPage } from './pages/LoginPage'
import { ParametersPage } from './pages/ParametersPage'
import { TicketDetailPage } from './pages/TicketDetailPage'
import { TicketListPage } from './pages/TicketListPage'
import { UsersPage } from './pages/UsersPage'

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Layout>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route
              path="/tickets"
              element={
                <ProtectedRoute>
                  <TicketListPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/tickets/:ticketId"
              element={
                <ProtectedRoute>
                  <TicketDetailPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/users"
              element={
                <AdminRoute>
                  <UsersPage />
                </AdminRoute>
              }
            />
            <Route
              path="/parameters"
              element={
                <AdminRoute>
                  <ParametersPage />
                </AdminRoute>
              }
            />
            <Route path="/" element={<Navigate to="/tickets" replace />} />
            <Route path="*" element={<Navigate to="/tickets" replace />} />
          </Routes>
        </Layout>
      </BrowserRouter>
    </AuthProvider>
  )
}
