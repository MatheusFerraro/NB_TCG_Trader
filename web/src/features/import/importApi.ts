import { API_URL, apiFetch } from '../../lib/apiClient'
import type {
  ImportJob,
  ResolveRowResponse,
  SkipRowResponse,
  UnmatchedRowsResponse,
} from './types'

/** GET /import/template is anonymous static content — a plain link downloads it. */
export const importTemplateUrl = `${API_URL}/import/template`

/** Extensions that the API accepts; checked client-side for a friendlier early error. */
export const IMPORT_EXTENSIONS = ['.csv', '.xlsx'] as const

export function hasImportExtension(fileName: string): boolean {
  const lower = fileName.toLowerCase()
  return IMPORT_EXTENSIONS.some((extension) => lower.endsWith(extension))
}

/** Uploads a .csv/.xlsx as multipart form data; the API parses and auto-matches it. */
export function uploadImportJob(file: File): Promise<ImportJob> {
  const form = new FormData()
  form.append('file', file)
  return apiFetch<ImportJob>('/import/jobs', { method: 'POST', body: form })
}

/** Lists the rows still needing a decision, plus the job's current progress. */
export function getImportRows(jobId: number): Promise<UnmatchedRowsResponse> {
  return apiFetch<UnmatchedRowsResponse>(`/import/jobs/${jobId}/rows`)
}

/** Resolves an unmatched row to the catalog card with the given provider id. */
export function resolveImportRow(
  jobId: number,
  rowId: number,
  cardExternalId: string,
): Promise<ResolveRowResponse> {
  return apiFetch<ResolveRowResponse>(`/import/jobs/${jobId}/rows/${rowId}/resolve`, {
    method: 'POST',
    body: { cardExternalId },
  })
}

/** Skips an unmatched row — it will not be added to the binder. */
export function skipImportRow(jobId: number, rowId: number): Promise<SkipRowResponse> {
  return apiFetch<SkipRowResponse>(`/import/jobs/${jobId}/rows/${rowId}/skip`, {
    method: 'POST',
  })
}
