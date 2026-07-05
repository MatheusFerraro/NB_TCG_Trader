import { useEffect, useState } from 'react'
import { ApiError } from '../lib/apiClient'
import { BinderItemCard } from '../features/collection/BinderItemCard'
import { getBinder } from '../features/collection/collectionApi'
import type { CollectionItem, Page } from '../features/collection/types'

/** One settled fetch, keyed by the page it answered so stale results are ignored. */
interface BinderResult {
  page: number
  data: Page<CollectionItem> | null
  error: string | null
}

/** Card-grid binder view (BACKLOG #20). */
export function BinderPage() {
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<BinderResult | null>(null)

  useEffect(() => {
    let cancelled = false
    getBinder(page)
      .then((data) => {
        if (!cancelled) setResult({ page, data, error: null })
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setResult({
            page,
            data: null,
            error:
              e instanceof ApiError
                ? e.message
                : 'Could not load your binder. Please try again.',
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [page])

  function handleSaved(updated: CollectionItem) {
    setResult((prev) =>
      prev?.data
        ? {
            ...prev,
            data: {
              ...prev.data,
              items: prev.data.items.map((item) => (item.id === updated.id ? updated : item)),
            },
          }
        : prev,
    )
  }

  // A result for a different page is stale: show the loading state instead.
  const current = result?.page === page ? result : null
  const data = current?.data ?? null
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <section>
      <header className="binder-header">
        <h1>Binder</h1>
        {data && <span className="binder-count">{data.totalCount} cards</span>}
      </header>

      {current === null && <p className="page-loading">Loading your binder…</p>}
      {current?.error && (
        <p className="form-errors" role="alert">
          {current.error}
        </p>
      )}

      {data && data.items.length === 0 && (
        <p className="binder-empty">
          Your binder is empty. Import a CSV or add cards to see them here.
        </p>
      )}

      {data && data.items.length > 0 && (
        <>
          <div className="binder-grid">
            {data.items.map((item) => (
              <BinderItemCard key={item.id} item={item} onSaved={handleSaved} />
            ))}
          </div>
          {totalPages > 1 && (
            <nav className="pager" aria-label="Binder pages">
              <button type="button" onClick={() => setPage(page - 1)} disabled={page <= 1}>
                Previous
              </button>
              <span>
                Page {page} of {totalPages}
              </span>
              <button
                type="button"
                onClick={() => setPage(page + 1)}
                disabled={page >= totalPages}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </section>
  )
}
