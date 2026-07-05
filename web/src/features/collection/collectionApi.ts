import { apiFetch } from '../../lib/apiClient'
import type { CollectionItem, Page, UpdateItemRequest } from './types'

/** Grid-friendly page size (divides evenly into 2/3/4/6 columns); API caps at 100. */
export const BINDER_PAGE_SIZE = 24

export function getBinder(page: number): Promise<Page<CollectionItem>> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(BINDER_PAGE_SIZE),
  })
  return apiFetch<Page<CollectionItem>>(`/collection/me?${params}`)
}

export function updateItem(id: number, request: UpdateItemRequest): Promise<CollectionItem> {
  return apiFetch<CollectionItem>(`/collection/items/${id}`, {
    method: 'PUT',
    body: request,
  })
}
