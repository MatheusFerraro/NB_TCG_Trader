import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { getAuditLog } from '../features/admin/adminApi'
import { formatDateTime } from '../features/admin/format'
import type { AdminAuditLog } from '../features/admin/types'

const ACTION_LABELS: Record<string, string> = {
  UserLocked: 'Locked user',
  UserUnlocked: 'Unlocked user',
  UserDetailViewed: 'Viewed user detail',
  RoleChanged: 'Changed role',
}

/** Immutable admin action trail: who did what to whom, why, and when. */
export function AdminAuditLogPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [result, setResult] = useState<{
    page: number
    data: AdminAuditLog | null
    error: string | null
  } | null>(null)

  const pageParam = Number(searchParams.get('page') ?? '1')
  const page = Number.isInteger(pageParam) && pageParam >= 1 ? pageParam : 1

  useEffect(() => {
    let cancelled = false
    getAuditLog(page)
      .then((data) => {
        if (!cancelled) setResult({ page, data, error: null })
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setResult({
            page,
            data: null,
            error: e instanceof ApiError ? e.message : 'Could not load the audit log.',
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [page])

  function goToPage(next: number) {
    const params = new URLSearchParams(searchParams)
    if (next > 1) {
      params.set('page', String(next))
    } else {
      params.delete('page')
    }
    setSearchParams(params)
  }

  const current = result?.page === page ? result : null
  const data = current?.data ?? null
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <div>
      {current === null && <p className="page-loading">Loading audit log…</p>}
      {current?.error && (
        <p className="form-errors" role="alert">
          {current.error}
        </p>
      )}

      {data && data.items.length === 0 && (
        <p className="market-empty">No admin actions recorded yet.</p>
      )}

      {data && data.items.length > 0 && (
        <>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Admin</th>
                  <th>Action</th>
                  <th>Target user</th>
                  <th>Reason</th>
                  <th>Correlation id</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((entry) => (
                  <tr key={entry.id}>
                    <td>{formatDateTime(entry.createdAt)}</td>
                    <td>{entry.adminDisplayName ?? entry.adminUserId}</td>
                    <td>{ACTION_LABELS[entry.action] ?? entry.action}</td>
                    <td>
                      {entry.targetUserId ? (
                        <Link to={`/admin/users/${entry.targetUserId}`}>
                          {entry.targetDisplayName ?? entry.targetUserId}
                        </Link>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>{entry.reason ?? '—'}</td>
                    <td>
                      <code className="admin-muted">{entry.correlationId ?? '—'}</code>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {totalPages > 1 && (
            <nav className="pager" aria-label="Audit log pages">
              <button type="button" onClick={() => goToPage(page - 1)} disabled={page <= 1}>
                Previous
              </button>
              <span>
                Page {page} of {totalPages}
              </span>
              <button
                type="button"
                onClick={() => goToPage(page + 1)}
                disabled={page >= totalPages}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  )
}
