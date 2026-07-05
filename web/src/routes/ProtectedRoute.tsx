import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../features/auth/authContext'

/**
 * Gate for authenticated routes. While the boot refresh is in flight we render
 * a placeholder instead of redirecting, so a hard page refresh on a protected
 * page doesn't bounce a still-valid session to /login.
 */
export function ProtectedRoute() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return <p className="page-loading">Loading…</p>
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}
