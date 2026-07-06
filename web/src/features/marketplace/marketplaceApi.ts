import { apiFetch } from '../../lib/apiClient'
import type { Currency, Page } from '../collection/types'
import type { MarketplaceListing, MarketplaceListingDetail } from './types'

/** Grid-friendly page size (divides evenly into 2/3/4/6 columns); API caps at 100. */
export const MARKETPLACE_PAGE_SIZE = 24

/**
 * Browse filters, straight from form inputs: prices stay strings so the query
 * echoes what the user typed and the API validates it. All optional, ANDed
 * server-side. The API requires a currency whenever a price bound is set.
 */
export interface ListingFilters {
  name?: string
  game?: string
  set?: string
  minPrice?: string
  maxPrice?: string
  currency?: Currency | ''
  city?: string
  country?: string
}

export function browseListings(
  filters: ListingFilters,
  page = 1,
): Promise<Page<MarketplaceListing>> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(MARKETPLACE_PAGE_SIZE),
  })
  if (filters.name?.trim()) params.set('name', filters.name.trim())
  if (filters.game?.trim()) params.set('game', filters.game.trim())
  if (filters.set?.trim()) params.set('set', filters.set.trim())
  if (filters.minPrice?.trim()) params.set('minPrice', filters.minPrice.trim())
  if (filters.maxPrice?.trim()) params.set('maxPrice', filters.maxPrice.trim())
  if (filters.currency) params.set('currency', filters.currency)
  if (filters.city?.trim()) params.set('city', filters.city.trim())
  if (filters.country?.trim()) params.set('country', filters.country.trim())
  // Public browse endpoint — no Bearer token needed.
  return apiFetch<Page<MarketplaceListing>>(`/marketplace?${params}`, { auth: false })
}

export function getListing(itemId: number): Promise<MarketplaceListingDetail> {
  return apiFetch<MarketplaceListingDetail>(`/marketplace/${itemId}`, { auth: false })
}
