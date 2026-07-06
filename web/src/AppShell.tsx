import { startTransition } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from './features/auth/authContext'

export function AppShell() {
  const { status, user, logout } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    // navigate() is deferred as a React transition. Clearing auth state at
    // sync priority would re-render the still-mounted ProtectedRoute as
    // anonymous first, and its /login redirect would beat navigate('/').
    // Putting logout in the same transition lane commits both together.
    startTransition(() => {
      navigate('/', { replace: true })
      logout()
    })
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link to="/" className="brand">
          NB TCG Trader
        </Link>
        <nav>
          {status === 'authenticated' && (
            <>
              {/* end: /binder/import is a child path and would highlight both links. */}
              <NavLink to="/binder" end>
                Binder
              </NavLink>
              <NavLink to="/binder/import">Import CSV</NavLink>
              <span className="user-name">{user?.displayName}</span>
              <button type="button" className="link-button" onClick={handleLogout}>
                Sign out
              </button>
            </>
          )}
          {status === 'anonymous' && (
            <>
              <NavLink to="/login">Sign in</NavLink>
              <NavLink to="/register">Register</NavLink>
            </>
          )}
        </nav>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
