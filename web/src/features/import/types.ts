/** Mirrors the Import slice contracts (UploadImport.cs, ImportReconcileContracts.cs). */

import type { CardCondition } from '../collection/types'

export type ImportStatus = 'Pending' | 'Processing' | 'NeedsReview' | 'Completed' | 'Failed'

export type MatchStatus = 'Unmatched' | 'AutoMatched' | 'ManuallyMatched' | 'Skipped'

/** POST /import/jobs response: the created job with its auto-match counts. */
export interface ImportJob {
  id: number
  fileName: string
  status: ImportStatus
  rowsTotal: number
  rowsMatched: number
  rowsUnmatched: number
  createdAt: string
}

/** A job's reconciliation progress, returned by every reconcile endpoint. */
export interface ImportJobProgress {
  jobId: number
  status: ImportStatus
  rowsTotal: number
  rowsMatched: number
  rowsUnmatched: number
}

/**
 * A row awaiting reconciliation, with the raw values kept verbatim from the
 * upload so the user can identify the card and pick the right catalog entry.
 */
export interface UnmatchedRow {
  id: number
  rawName: string
  rawSet: string | null
  rawNumber: string | null
  quantity: number
  price: number | null
  condition: CardCondition | null
  isForSale: boolean
}

/** GET /import/jobs/{jobId}/rows response. */
export interface UnmatchedRowsResponse {
  job: ImportJobProgress
  rows: UnmatchedRow[]
}

/** POST .../rows/{rowId}/resolve response. */
export interface ResolveRowResponse {
  job: ImportJobProgress
  rowId: number
  matchStatus: MatchStatus
  createdItemId: number
}

/** POST .../rows/{rowId}/skip response. */
export interface SkipRowResponse {
  job: ImportJobProgress
  rowId: number
  matchStatus: MatchStatus
}

/**
 * Rows the progress counters do not name directly: matched and unmatched are
 * counted by the API, everything else (skipped rows) is the remainder.
 */
export function skippedRows(job: ImportJobProgress): number {
  return Math.max(0, job.rowsTotal - job.rowsMatched - job.rowsUnmatched)
}
