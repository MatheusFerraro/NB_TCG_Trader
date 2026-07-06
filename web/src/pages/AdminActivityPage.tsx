import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { getActivity } from '../features/admin/adminApi'
import { formatDateTime } from '../features/admin/format'
import type { AdminActivity } from '../features/admin/types'

/**
 * Operational activity derived from data the app already stores — activity
 * timestamps, Identity's failed-attempt counters, import statuses. No request
 * tracking or IPs are collected.
 */
export function AdminActivityPage() {
  const [data, setData] = useState<AdminActivity | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getActivity()
      .then((activity) => {
        if (!cancelled) setData(activity)
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setError(e instanceof ApiError ? e.message : 'Could not load activity.')
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
    return <p className="page-loading">Loading activity…</p>
  }

  return (
    <div className="admin-activity">
      <section>
        <h3>Recent sign-ins</h3>
        {data.recentSignIns.length === 0 ? (
          <p className="market-empty">No sign-ins recorded yet.</p>
        ) : (
          <ul className="admin-list">
            {data.recentSignIns.map((user) => (
              <li key={user.id}>
                <Link to={`/admin/users/${user.id}`}>{user.displayName}</Link>{' '}
                <span className="admin-muted">{formatDateTime(user.timestamp)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h3>Recent registrations</h3>
        {data.recentRegistrations.length === 0 ? (
          <p className="market-empty">No registrations yet.</p>
        ) : (
          <ul className="admin-list">
            {data.recentRegistrations.map((user) => (
              <li key={user.id}>
                <Link to={`/admin/users/${user.id}`}>{user.displayName}</Link>{' '}
                <span className="admin-muted">{formatDateTime(user.timestamp)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h3>Failed sign-in attempts</h3>
        {data.failedSignIns.length === 0 ? (
          <p className="market-empty">No accounts with failed attempts.</p>
        ) : (
          <ul className="admin-list">
            {data.failedSignIns.map((user) => (
              <li key={user.id}>
                <Link to={`/admin/users/${user.id}`}>{user.displayName}</Link>{' '}
                <span className="admin-muted">
                  {user.accessFailedCount} failed attempt
                  {user.accessFailedCount === 1 ? '' : 's'}
                  {user.isLockedOut ? ' · locked' : ''}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h3>Recent failed imports</h3>
        {data.recentFailedImports.length === 0 ? (
          <p className="market-empty">No failed imports.</p>
        ) : (
          <ul className="admin-list">
            {data.recentFailedImports.map((job) => (
              <li key={job.id}>
                <Link to={`/admin/users/${job.userId}`}>{job.userDisplayName}</Link>{' '}
                <span className="admin-muted">
                  {job.fileName} · {formatDateTime(job.createdAt)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
