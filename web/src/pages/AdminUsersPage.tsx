import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { listUsers, type AdminUserFilters } from '../features/admin/adminApi'
import { formatDateTime } from '../features/admin/format'
import type { AdminUserList } from '../features/admin/types'

const FILTER_KEYS = ['search', 'city', 'country', 'locked'] as const

/** One settled fetch, keyed by the query it answered so stale results are ignored. */
interface UsersResult {
  key: string
  data: AdminUserList | null
  error: string | null
}

function filtersFromParams(params: URLSearchParams): AdminUserFilters {
  return {
    search: params.get('search') ?? '',
    city: params.get('city') ?? '',
    country: params.get('country') ?? '',
    locked: (params.get('locked') ?? '') as AdminUserFilters['locked'],
  }
}

/**
 * Admin user table with search/filter, URL-driven like the marketplace browse so
 * a filtered view survives refresh and back/forward.
 */
export function AdminUsersPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [result, setResult] = useState<UsersResult | null>(null)

  const filters = filtersFromParams(searchParams)
  const pageParam = Number(searchParams.get('page') ?? '1')
  const page = Number.isInteger(pageParam) && pageParam >= 1 ? pageParam : 1
  const queryKey = searchParams.toString()

  useEffect(() => {
    let cancelled = false
    const params = new URLSearchParams(queryKey)
    const pageValue = Number(params.get('page') ?? '1')
    listUsers(
      filtersFromParams(params),
      Number.isInteger(pageValue) && pageValue >= 1 ? pageValue : 1,
    )
      .then((data) => {
        if (!cancelled) setResult({ key: queryKey, data, error: null })
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setResult({
            key: queryKey,
            data: null,
            error: e instanceof ApiError ? e.message : 'Could not load users.',
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [queryKey])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const params = new URLSearchParams()
    for (const key of FILTER_KEYS) {
      const value = String(form.get(key) ?? '').trim()
      if (value) params.set(key, value)
    }
    // New filters restart at page 1.
    setSearchParams(params)
  }

  function goToPage(next: number) {
    const params = new URLSearchParams(searchParams)
    if (next > 1) {
      params.set('page', String(next))
    } else {
      params.delete('page')
    }
    setSearchParams(params)
  }

  const current = result?.key === queryKey ? result : null
  const data = current?.data ?? null
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <div>
      <form className="admin-filters" onSubmit={applyFilters} key={queryKey}>
        <input
          name="search"
          placeholder="Name, email, or id"
          defaultValue={filters.search}
          aria-label="Search users"
        />
        <input name="city" placeholder="City" defaultValue={filters.city} aria-label="City" />
        <input
          name="country"
          placeholder="Country"
          defaultValue={filters.country}
          aria-label="Country"
        />
        <select name="locked" defaultValue={filters.locked} aria-label="Lockout status">
          <option value="">All users</option>
          <option value="true">Locked only</option>
          <option value="false">Unlocked only</option>
        </select>
        <button type="submit">Filter</button>
      </form>

      {current === null && <p className="page-loading">Loading users…</p>}
      {current?.error && (
        <p className="form-errors" role="alert">
          {current.error}
        </p>
      )}

      {data && data.items.length === 0 && (
        <p className="market-empty">No users matched the filters.</p>
      )}

      {data && data.items.length > 0 && (
        <>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Email</th>
                  <th>Location</th>
                  <th>Contact</th>
                  <th>Status</th>
                  <th>Registered</th>
                  <th>Last seen</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <Link to={`/admin/users/${user.id}`}>{user.displayName}</Link>
                    </td>
                    <td>{user.email ?? '—'}</td>
                    <td>{[user.city, user.country].filter(Boolean).join(', ') || '—'}</td>
                    <td>
                      {/* Present/absent only — handles themselves stay private. */}
                      {[
                        user.hasContactEmail && 'email',
                        user.hasDiscordHandle && 'discord',
                        user.hasInstagramHandle && 'instagram',
                      ]
                        .filter(Boolean)
                        .join(', ') || 'none'}
                    </td>
                    <td>
                      {user.isLockedOut ? (
                        <span className="admin-badge admin-badge-locked">Locked</span>
                      ) : (
                        <span className="admin-badge">Active</span>
                      )}
                    </td>
                    <td>{formatDateTime(user.createdAt)}</td>
                    <td>{formatDateTime(user.lastSeenAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {totalPages > 1 && (
            <nav className="pager" aria-label="User pages">
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
