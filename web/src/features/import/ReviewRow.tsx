import { useState } from 'react'
import { ApiError } from '../../lib/apiClient'
import { CatalogCardPicker } from '../catalog/CatalogCardPicker'
import type { CatalogCard } from '../catalog/types'
import { resolveImportRow, skipImportRow } from './importApi'
import type { ImportJobProgress, UnmatchedRow } from './types'

interface ReviewRowProps {
  jobId: number
  row: UnmatchedRow
  /** The row was resolved or skipped; carries the job's fresh progress. */
  onSettled: (rowId: number, job: ImportJobProgress) => void
  /** The server said the row changed under us (409) — the list is stale. */
  onStale: () => void
}

type Panel = 'closed' | 'resolve' | 'skip'

/**
 * One unmatched import row (BACKLOG #21): shows the raw spreadsheet values so
 * the user can identify the card, then lets them pick the matching catalog
 * card (pre-searched from the raw values) or skip the row entirely.
 */
export function ReviewRow({ jobId, row, onSettled, onStale }: ReviewRowProps) {
  const [panel, setPanel] = useState<Panel>('closed')
  const [selected, setSelected] = useState<CatalogCard | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run(action: () => Promise<{ job: ImportJobProgress }>) {
    setBusy(true)
    setError(null)
    try {
      const response = await action()
      onSettled(row.id, response.job)
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        onStale()
        return
      }
      setError(
        e instanceof ApiError ? e.message : 'Could not reach the server. Please try again.',
      )
      setBusy(false)
    }
  }

  function openPanel(next: Panel) {
    setPanel(next)
    setError(null)
  }

  return (
    <article className="import-row" aria-label={`Imported row: ${row.rawName}`}>
      <div className="import-row-summary">
        <div className="import-row-identity">
          <h3>{row.rawName}</h3>
          <p className="catalog-result-meta">
            {row.rawSet ?? 'No set'}
            {row.rawNumber !== null && ` · #${row.rawNumber}`}
          </p>
        </div>
        <dl className="import-row-facts">
          <div>
            <dt>Quantity</dt>
            <dd>{row.quantity}</dd>
          </div>
          <div>
            <dt>Condition</dt>
            <dd>{row.condition ?? '—'}</dd>
          </div>
          <div>
            <dt>Price</dt>
            <dd>{row.price ?? '—'}</dd>
          </div>
          <div>
            <dt>For sale</dt>
            <dd>{row.isForSale ? 'Yes' : 'No'}</dd>
          </div>
        </dl>
        <div className="import-row-actions">
          {panel !== 'resolve' && (
            <button type="button" className="import-row-cta" onClick={() => openPanel('resolve')}>
              Find match
            </button>
          )}
          {panel === 'closed' && (
            <button type="button" className="link-button" onClick={() => openPanel('skip')}>
              Skip row
            </button>
          )}
          {panel !== 'closed' && (
            <button
              type="button"
              className="link-button"
              onClick={() => openPanel('closed')}
              disabled={busy}
            >
              Cancel
            </button>
          )}
        </div>
      </div>

      {panel === 'skip' && (
        <div className="manage-confirm">
          <p>
            Skip “{row.rawName}”? It will not be added to your binder. You can
            still add it later by hand.
          </p>
          <div className="binder-card-actions">
            <button
              type="button"
              className="manage-delete"
              onClick={() => void run(() => skipImportRow(jobId, row.id))}
              disabled={busy}
            >
              {busy ? 'Skipping…' : 'Skip this row'}
            </button>
          </div>
        </div>
      )}

      {panel === 'resolve' && (
        <div className="import-row-resolve">
          <CatalogCardPicker
            initialQuery={row.rawName}
            initialSet={row.rawSet ?? ''}
            initialNumber={row.rawNumber ?? ''}
            autoSearch
            selected={selected}
            onSelect={setSelected}
          />
          {selected && (
            <div className="binder-card-actions">
              <button
                type="button"
                className="button-primary"
                onClick={() => void run(() => resolveImportRow(jobId, row.id, selected.externalId))}
                disabled={busy}
              >
                {busy ? 'Adding…' : `Add as ${selected.name}`}
              </button>
            </div>
          )}
        </div>
      )}

      {error && (
        <p className="form-errors" role="alert">
          {error}
        </p>
      )}
    </article>
  )
}
