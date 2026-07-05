/** Mirrors the Collection slice contracts (CollectionContracts.cs, GetBinder.cs). */

export type CardCondition = 'NM' | 'LP' | 'MP' | 'HP' | 'DMG'
export type Currency = 'CAD' | 'BRL'

export const CARD_CONDITIONS: readonly CardCondition[] = ['NM', 'LP', 'MP', 'HP', 'DMG']
export const CURRENCIES: readonly Currency[] = ['CAD', 'BRL']

/** Display projection of the catalog card a binder row points at. ImageUrl is never null — the API substitutes a placeholder and flags it via hasImage. */
export interface CollectionCard {
  id: number
  externalId: string
  name: string
  number: string | null
  rarity: string | null
  imageUrl: string
  hasImage: boolean
  setName: string | null
}

export interface CollectionItem {
  id: number
  card: CollectionCard
  quantity: number
  condition: CardCondition
  isForSale: boolean
  price: number | null
  currency: Currency
  isPrivate: boolean
  notes: string | null
  createdAt: string
  updatedAt: string
}

/** CatalogPage<T> from the API. */
export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

/**
 * PUT /collection/items/{id} body. Full-replacement semantics: every field is
 * the complete desired state, so the edit form must send notes/price back even
 * when untouched or the server clears them.
 */
export interface UpdateItemRequest {
  quantity: number
  condition: CardCondition
  isForSale: boolean
  price: number | null
  currency: Currency
  isPrivate: boolean
  notes: string | null
}
