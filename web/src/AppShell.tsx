import { startTransition, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './features/auth/authContext'
import { isAdmin } from './features/auth/types'
import {
  AdminIcon,
  BinderIcon,
  BrandMark,
  HomeIcon,
  ImportIcon,
  MarketIcon,
  ProfileIcon,
  RegisterIcon,
  SignInIcon,
  SignOutIcon,
} from './components/NavIcons'

/** A labelled block of sidebar rows ("Main", "Collection", "Account"). */
function NavGroup({ title, children }: { title: string; children: ReactNode }) {
  const id = useId()
  return (
    <div className="nav-group">
      <h2 className="nav-group-title" id={id}>
        {title}
      </h2>
      <ul className="nav-list" aria-labelledby={id}>
        {children}
      </ul>
    </div>
  )
}

/**
 * One navigation row. NavLink supplies both the `active` class and
 * aria-current="page", so the active state is carried by shape (filled pill +
 * accent rail) and by assistive tech — never by colour alone.
 */
function NavRow({ to, icon, children, end }: { to: string; icon: ReactNode; children: ReactNode; end?: boolean }) {
  return (
    <li>
      <NavLink to={to} end={end} className="nav-row">
        {icon}
        <span className="nav-row-label">{children}</span>
      </NavLink>
    </li>
  )
}

export function AppShell() {
  const { status, user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const lastPathRef = useRef(location.pathname)
  const toggleRef = useRef<HTMLButtonElement>(null)

  // Collapse the mobile drawer whenever the route changes so a tapped link does
  // not leave it covering the page it navigated to. useLayoutEffect closes it
  // before paint (no flash); the ref guard skips the initial mount.
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

  // Lock the page behind the drawer. Without this the body scrolls under the
  // overlay on touch, and closing the drawer strands the user mid-page. The
  // toggle only exists below the sidebar breakpoint, so this cannot fire on
  // desktop. Restores the previous inline value rather than assuming ''.
  useEffect(() => {
    if (!menuOpen) return
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = previous
    }
  }, [menuOpen])

  function closeMenu() {
    setMenuOpen(false)
    toggleRef.current?.focus()
  }

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

  const authenticated = status === 'authenticated'

  return (
    <div className={`app-shell${menuOpen ? ' menu-open' : ''}`}>
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>

      {/* Compact bar: the only chrome below the sidebar breakpoint. */}
      <header className="mobile-bar">
        <Link to="/" className="brand">
          <BrandMark />
          <span>NB TCG Trader</span>
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
      </header>

      {/* Tap-anywhere-to-dismiss backdrop. Purely decorative: Escape and the
          toggle both close the drawer, so it needs no role of its own. */}
      <div className="nav-scrim" onClick={closeMenu} aria-hidden="true" />

      <aside id="primary-nav" className={`app-sidebar${menuOpen ? ' open' : ''}`}>
        <Link to="/" className="brand sidebar-brand">
          <BrandMark />
          <span>NB TCG Trader</span>
        </Link>

        <nav className="sidebar-nav" aria-label="Main">
          <NavGroup title="Main">
            {/* end: every route starts with "/" and would match otherwise. */}
            <NavRow to="/" end icon={<HomeIcon />}>
              Home
            </NavRow>
            {/* Public surface: visible signed in or not. */}
            <NavRow to="/marketplace" icon={<MarketIcon />}>
              Marketplace
            </NavRow>
          </NavGroup>

          {authenticated && (
            <NavGroup title="Collection">
              {/* end: /binder/import is a child path and would highlight both. */}
              <NavRow to="/binder" end icon={<BinderIcon />}>
                My Binder
              </NavRow>
              <NavRow to="/binder/import" icon={<ImportIcon />}>
                Import Cards
              </NavRow>
            </NavGroup>
          )}

          <NavGroup title="Account">
            {authenticated && (
              <NavRow to="/profile" icon={<ProfileIcon />}>
                Profile
              </NavRow>
            )}
            {/* Client-side gate only; every /admin endpoint enforces the role. */}
            {authenticated && isAdmin(user) && (
              <NavRow to="/admin" icon={<AdminIcon />}>
                Admin
              </NavRow>
            )}
            {status === 'anonymous' && (
              <>
                <NavRow to="/login" icon={<SignInIcon />}>
                  Sign in
                </NavRow>
                <NavRow to="/register" icon={<RegisterIcon />}>
                  Register
                </NavRow>
              </>
            )}
          </NavGroup>
        </nav>

        {authenticated && (
          <div className="sidebar-footer">
            {/* The signed-in name doubles as the door to profile settings. */}
            <Link to="/profile" className="sidebar-user" title="Profile settings">
              <span className="sidebar-avatar" aria-hidden="true">
                {user?.displayName?.trim().charAt(0).toUpperCase() || '?'}
              </span>
              <span className="sidebar-user-name">{user?.displayName}</span>
            </Link>
            <button type="button" className="sidebar-signout" onClick={handleLogout}>
              <SignOutIcon />
              <span>Sign out</span>
            </button>
          </div>
        )}
      </aside>

      <main id="main-content" className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
