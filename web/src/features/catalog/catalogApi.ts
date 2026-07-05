import { apiFetch } from '../../lib/apiClient'
import type { Page } from '../collection/types'
import type { CatalogCard } from './types'

export const CATALOG_PAGE_SIZE = 20

export interface CatalogSearch {
  query?: string
  set?: string
  number?: string
}

export function searchCatalog(search: CatalogSearch, page = 1): Promise<Page<CatalogCard>> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(CATALOG_PAGE_SIZE) })
  if (search.query?.trim()) params.set('query', search.query.trim())
  if (search.set?.trim()) params.set('set', search.set.trim())
  if (search.number?.trim()) params.set('number', search.number.trim())
  // Public browse endpoint — no Bearer token needed.
  return apiFetch<Page<CatalogCard>>(`/catalog/cards?${params}`, { auth: false })
}
