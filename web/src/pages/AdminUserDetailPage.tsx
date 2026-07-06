import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { getUserDetail, lockUser, unlockUser } from '../features/admin/adminApi'
import { formatDateTime } from '../features/admin/format'
import type { AdminUserDetail } from '../features/admin/types'

const ACTION_LABELS: Record<string, string> = {
  UserLocked: 'Locked',
  UserUnlocked: 'Unlocked',
  UserDetailViewed: 'Detail viewed',
  RoleChanged: 'Role changed',
}

/**
 * Single-user operational view with the lock/unlock controls. A lock demands a
 * reason (the API rejects it otherwise); both actions land in the audit log.
 */
export function AdminUserDetailPage() {
  const { userId } = useParams<{ userId: string }>()
  const [detail, setDetail] = useState<AdminUserDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    if (!userId) return
    getUserDetail(userId)
      .then(setDetail)
      .catch((e: unknown) => {
        setError(e instanceof ApiError ? e.message : 'Could not load the user.')
      })
  }, [userId])

  useEffect(load, [load])

  async function handleLock(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!userId) return
    const form = event.currentTarget
    const reason = String(new FormData(form).get('reason') ?? '').trim()
    if (!reason) {
      setActionError('A lock reason is required.')
      return
    }

    setBusy(true)
    setActionError(null)
    try {
      await lockUser(userId, reason)
      form.reset()
      load()
    } catch (e: unknown) {
      setActionError(e instanceof ApiError ? e.message : 'Lock failed.')
    } finally {
      setBusy(false)
    }
  }

  async function handleUnlock(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!userId) return
    const reason = String(new FormData(event.currentTarget).get('reason') ?? '').trim()

    setBusy(true)
    setActionError(null)
    try {
      await unlockUser(userId, reason || undefined)
      load()
    } catch (e: unknown) {
      setActionError(e instanceof ApiError ? e.message : 'Unlock failed.')
    } finally {
      setBusy(false)
    }
  }

  if (error) {
    return (
      <p className="form-errors" role="alert">
        {error}
      </p>
    )
  }
  if (!detail) {
    return <p className="page-loading">Loading user…</p>
  }

  const contact =
    [
      detail.hasContactEmail && 'email',
      detail.hasDiscordHandle && 'discord',
      detail.hasInstagramHandle && 'instagram',
    ]
      .filter(Boolean)
      .join(', ') || 'none'

  return (
    <div>
      <p>
        <Link to="/admin/users">← Back to users</Link>
      </p>

      <header className="market-header">
        <div>
          <h2>{detail.displayName}</h2>
          <p className="market-tagline">{detail.email ?? 'no email'}</p>
        </div>
        {detail.isLockedOut ? (
          <span className="admin-badge admin-badge-locked">Locked</span>
        ) : (
          <span className="admin-badge">Active</span>
        )}
      </header>

      <div className="admin-detail-grid">
        <dl className="admin-facts">
          <dt>User id</dt>
          <dd>
            <code>{detail.id}</code>
          </dd>
          <dt>Location</dt>
          <dd>{[detail.city, detail.country].filter(Boolean).join(', ') || '—'}</dd>
          <dt>Contact channels</dt>
          <dd>{contact}</dd>
          <dt>Roles</dt>
          <dd>{detail.roles.length > 0 ? detail.roles.join(', ') : 'none'}</dd>
          <dt>Registered</dt>
          <dd>{formatDateTime(detail.createdAt)}</dd>
          <dt>Last sign-in</dt>
          <dd>{formatDateTime(detail.lastLoginAt)}</dd>
          <dt>Last seen</dt>
          <dd>{formatDateTime(detail.lastSeenAt)}</dd>
          <dt>Failed sign-in attempts</dt>
          <dd>{detail.accessFailedCount}</dd>
          <dt>Binder items</dt>
          <dd>{detail.collectionItemCount}</dd>
          <dt>Active listings</dt>
          <dd>{detail.activeListingCount}</dd>
          <dt>Imports</dt>
          <dd>
            {detail.importJobs.total} total · {detail.importJobs.failed} failed ·{' '}
            {detail.importJobs.needsReview} needs review · {detail.importJobs.completed}{' '}
            completed
          </dd>
        </dl>

        <div className="admin-actions">
          {actionError && (
            <p className="form-errors" role="alert">
              {actionError}
            </p>
          )}
          {detail.isLockedOut ? (
            <form onSubmit={handleUnlock}>
              <h3>Unlock account</h3>
              <label>
                Reason (optional)
                <input name="reason" maxLength={500} placeholder="Resolved with the user" />
              </label>
              <button type="submit" disabled={busy}>
                {busy ? 'Unlocking…' : 'Unlock user'}
              </button>
            </form>
          ) : (
            <form onSubmit={handleLock}>
              <h3>Lock account</h3>
              <p className="field-help">
                Locking blocks sign-in and token refresh. The reason is required and is
                written to the audit log.
              </p>
              <label>
                Reason
                <input
                  name="reason"
                  maxLength={500}
                  required
                  placeholder="Why is this account being locked?"
                />
              </label>
              <button type="submit" disabled={busy}>
                {busy ? 'Locking…' : 'Lock user'}
              </button>
            </form>
          )}
        </div>
      </div>

      <h3>Recent audit entries for this user</h3>
      {detail.recentAuditEntries.length === 0 ? (
        <p className="market-empty">No audit entries yet.</p>
      ) : (
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Action</th>
                <th>Admin</th>
                <th>Reason</th>
              </tr>
            </thead>
            <tbody>
              {detail.recentAuditEntries.map((entry) => (
                <tr key={entry.id}>
                  <td>{formatDateTime(entry.createdAt)}</td>
                  <td>{ACTION_LABELS[entry.action] ?? entry.action}</td>
                  <td>{entry.adminDisplayName ?? entry.adminUserId}</td>
                  <td>{entry.reason ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
