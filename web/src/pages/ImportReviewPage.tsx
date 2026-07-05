import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { ReviewRow } from '../features/import/ReviewRow'
import { getImportRows } from '../features/import/importApi'
import { skippedRows } from '../features/import/types'
import type { ImportJobProgress, ImportStatus, UnmatchedRow } from '../features/import/types'

const STATUS_LABELS: Record<ImportStatus, string> = {
  Pending: 'Pending',
  Processing: 'Processing',
  NeedsReview: 'Needs review',
  Completed: 'Completed',
  Failed: 'Failed',
}

type ReviewState =
  | { status: 'loading' }
  | { status: 'error'; message: string; notFound: boolean }
  | { status: 'done'; job: ImportJobProgress; rows: UnmatchedRow[] }

/**
 * Import reconciliation screen (BACKLOG #21): the job's progress counts plus
 * each row auto-matching could not place. Resolving or skipping a row updates
 * the progress from the server's response; once nothing is left the user is
 * pointed back at the binder, where matched rows already landed.
 */
export function ImportReviewPage() {
  const { jobId: jobIdParam } = useParams()
  const jobId = Number(jobIdParam)
  // A malformed URL is decided synchronously — no fetch, no effect.
  const validJobId = Number.isInteger(jobId) && jobId > 0
  const [state, setState] = useState<ReviewState>(
    validJobId
      ? { status: 'loading' }
      : {
          status: 'error',
          notFound: true,
          message: 'This import does not exist, or it belongs to another account.',
        },
  )

  // Bumped to refetch (retry button, stale-row 409); the effect below re-runs.
  const [fetchNonce, setFetchNonce] = useState(0)

  useEffect(() => {
    if (!validJobId) return
    let cancelled = false
    getImportRows(jobId)
      .then(({ job, rows }) => {
        if (!cancelled) setState({ status: 'done', job, rows })
      })
      .catch((e: unknown) => {
        if (cancelled) return
        const notFound = e instanceof ApiError && e.status === 404
        setState({
          status: 'error',
          notFound,
          message: notFound
            ? 'This import does not exist, or it belongs to another account.'
            : e instanceof ApiError
              ? e.message
              : 'Could not load the import. Please try again.',
        })
      })
    return () => {
      cancelled = true
    }
  }, [jobId, validJobId, fetchNonce])

  function reload() {
    setState({ status: 'loading' })
    setFetchNonce((nonce) => nonce + 1)
  }

  function handleSettled(rowId: number, job: ImportJobProgress) {
    setState((prev) =>
      prev.status === 'done'
        ? { status: 'done', job, rows: prev.rows.filter((row) => row.id !== rowId) }
        : prev,
    )
  }

  return (
    <section>
      <header className="binder-header">
        <h1>Review your import</h1>
        <Link to="/binder">Back to binder</Link>
      </header>

      {state.status === 'loading' && <p className="page-loading">Loading your import…</p>}

      {state.status === 'error' && (
        <>
          <p className="form-errors" role="alert">
            {state.message}
          </p>
          <p>
            {state.notFound ? (
              <Link to="/binder/import">Start a new import</Link>
            ) : (
              <button type="button" className="link-button" onClick={reload}>
                Try again
              </button>
            )}
          </p>
        </>
      )}

      {state.status === 'done' && (
        <>
          <dl className="import-summary" aria-label="Import progress">
            <div>
              <dt>Status</dt>
              <dd>{STATUS_LABELS[state.job.status]}</dd>
            </div>
            <div>
              <dt>Total rows</dt>
              <dd>{state.job.rowsTotal}</dd>
            </div>
            <div>
              <dt>In your binder</dt>
              <dd>{state.job.rowsMatched}</dd>
            </div>
            <div>
              <dt>Need review</dt>
              <dd>{state.job.rowsUnmatched}</dd>
            </div>
            {skippedRows(state.job) > 0 && (
              <div>
                <dt>Skipped</dt>
                <dd>{skippedRows(state.job)}</dd>
              </div>
            )}
          </dl>

          {state.rows.length === 0 ? (
            <div className="import-done">
              <h2>All done!</h2>
              <p>
                {state.job.rowsMatched > 0
                  ? `${state.job.rowsMatched} ${
                      state.job.rowsMatched === 1 ? 'card is' : 'cards are'
                    } in your binder.`
                  : 'Nothing was added to your binder.'}
              </p>
              <Link to="/binder" className="cta">
                Go to your binder
              </Link>
            </div>
          ) : (
            <>
              <p className="catalog-hint">
                We could not match {state.rows.length}{' '}
                {state.rows.length === 1 ? 'row' : 'rows'} automatically. Find the
                right card for each, or skip the ones you do not want.
              </p>
              <div className="import-rows">
                {state.rows.map((row) => (
                  <ReviewRow
                    key={row.id}
                    jobId={jobId}
                    row={row}
                    onSettled={handleSettled}
                    onStale={reload}
                  />
                ))}
              </div>
            </>
          )}
        </>
      )}
    </section>
  )
}
