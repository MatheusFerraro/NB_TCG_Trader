import { NavLink, Outlet } from 'react-router-dom'

/** Shared frame for the admin hub: heading + section tabs above each page. */
export function AdminLayout() {
  return (
    <section>
      <header className="market-header">
        <div>
          <h1>Admin hub</h1>
          <p className="market-tagline">Operational controls — actions here are audited.</p>
        </div>
      </header>
      <nav className="admin-tabs" aria-label="Admin sections">
        <NavLink to="/admin" end>
          Dashboard
        </NavLink>
        <NavLink to="/admin/users">Users</NavLink>
        <NavLink to="/admin/activity">Activity</NavLink>
        <NavLink to="/admin/audit">Audit log</NavLink>
      </nav>
      <Outlet />
    </section>
  )
}
