/** Mirrors the Marketplace slice contracts (MarketplaceContracts.cs). */

import type { CardCondition, Currency } from '../collection/types'

/**
 * Display projection of the catalog card a listing points at. ImageUrl is
 * never null — the API substitutes a placeholder and flags it via hasImage.
 */
export interface MarketplaceCard {
  id: number
  externalId: string
  name: string
  number: string | null
  rarity: string | null
  imageUrl: string
  hasImage: boolean
  setName: string | null
  gameName: string
}

/**
 * The public face of the seller on a browse card: identity and location only.
 * Contact channels are deliberately absent — the detail endpoint reveals them.
 */
export interface MarketplaceSeller {
  displayName: string
  city: string | null
  country: string | null
}

/** One public for-sale listing as returned by GET /marketplace. */
export interface MarketplaceListing {
  id: number
  card: MarketplaceCard
  quantity: number
  condition: CardCondition
  price: number | null
  currency: Currency
  notes: string | null
  seller: MarketplaceSeller
  createdAt: string
  updatedAt: string
}

/** Seller block on the detail page: public identity plus the public contact channels. */
export interface MarketplaceSellerContact extends MarketplaceSeller {
  contactEmail: string | null
  discordHandle: string | null
  instagramHandle: string | null
}

/** GET /marketplace/{itemId}: a listing with the seller's contact channels revealed. */
export interface MarketplaceListingDetail extends Omit<MarketplaceListing, 'seller'> {
  seller: MarketplaceSellerContact
}
