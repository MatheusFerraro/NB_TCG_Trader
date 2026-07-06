import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../features/auth/authContext'
import { isAdmin } from '../features/auth/types'

/**
 * Gate for admin routes: authenticated AND carrying the Admin role. This only
 * hides UI — every /admin endpoint enforces the Admin role policy server-side,
 * so a forged client gets 403 regardless. Non-admin users land on the home page.
 */
export function AdminRoute() {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return <p className="page-loading">Loading…</p>
  }

  if (status === 'anonymous') {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: `${location.pathname}${location.search}${location.hash}` }}
      />
    )
  }

  if (!isAdmin(user)) {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
