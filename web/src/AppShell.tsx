import { startTransition, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './features/auth/authContext'
import { isAdmin } from './features/auth/types'

export function AppShell() {
  const { status, user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const lastPathRef = useRef(location.pathname)
  const toggleRef = useRef<HTMLButtonElement>(null)

  // Collapse the mobile menu whenever the route changes so a tapped link does
  // not leave the drawer covering the page it navigated to. useLayoutEffect
  // closes it before paint (no flash); the ref guard skips the initial mount.
  useLayoutEffect(() => {
    if (location.pathname === lastPathRef.current) return
    lastPathRef.current = location.pathname
    setMenuOpen(false)
  }, [location.pathname])

  // Escape closes the drawer and returns focus to the toggle that opened it.
  useEffect(() => {
    if (!menuOpen) return
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setMenuOpen(false)
        toggleRef.current?.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [menuOpen])

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
        <button
          ref={toggleRef}
          type="button"
          className="nav-toggle"
          aria-expanded={menuOpen}
          aria-controls="primary-nav"
          aria-label={menuOpen ? 'Close navigation menu' : 'Open navigation menu'}
          onClick={() => setMenuOpen((open) => !open)}
        >
          <span className="nav-toggle-bars" aria-hidden="true" />
        </button>
        <nav id="primary-nav" className={menuOpen ? 'open' : undefined}>
          {/* Public surface: visible signed in or not. */}
          <NavLink to="/marketplace">Marketplace</NavLink>
          {status === 'authenticated' && (
            <>
              {/* end: /binder/import is a child path and would highlight both links. */}
              <NavLink to="/binder" end>
                Binder
              </NavLink>
              <NavLink to="/binder/import">Import CSV</NavLink>
              {/* Client-side gate only; every /admin endpoint enforces the role. */}
              {isAdmin(user) && <NavLink to="/admin">Admin</NavLink>}
              {/* The signed-in name doubles as the door to profile settings. */}
              <NavLink to="/profile" className="user-name" title="Profile settings">
                {user?.displayName}
              </NavLink>
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
