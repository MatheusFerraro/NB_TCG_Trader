import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { getDashboard } from '../features/admin/adminApi'
import type { AdminDashboard } from '../features/admin/types'

/** Operational summary: user totals and growth, lockouts, listings, import failures. */
export function AdminDashboardPage() {
  const [data, setData] = useState<AdminDashboard | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getDashboard()
      .then((dashboard) => {
        if (!cancelled) setData(dashboard)
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setError(e instanceof ApiError ? e.message : 'Could not load the dashboard.')
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  if (error) {
    return (
      <p className="form-errors" role="alert">
        {error}
      </p>
    )
  }
  if (!data) {
    return <p className="page-loading">Loading dashboard…</p>
  }

  return (
    <div className="admin-stats">
      <div className="admin-stat">
        <span className="admin-stat-value">{data.totalUsers}</span>
        <span className="admin-stat-label">Total users</span>
      </div>
      <div className="admin-stat">
        <span className="admin-stat-value">{data.newUsersLast7Days}</span>
        <span className="admin-stat-label">New users (7 days)</span>
      </div>
      <div className="admin-stat">
        <span className="admin-stat-value">{data.newUsersLast30Days}</span>
        <span className="admin-stat-label">New users (30 days)</span>
      </div>
      <div className="admin-stat">
        <span className="admin-stat-value">{data.lockedUsers}</span>
        <span className="admin-stat-label">
          <Link to="/admin/users?locked=true">Locked users</Link>
        </span>
      </div>
      <div className="admin-stat">
        <span className="admin-stat-value">{data.activeListings}</span>
        <span className="admin-stat-label">Active listings</span>
      </div>
      <div className="admin-stat">
        <span className="admin-stat-value">{data.failedImportsLast7Days}</span>
        <span className="admin-stat-label">Failed imports (7 days)</span>
      </div>
    </div>
  )
}
