/** Mirrors the Catalog slice contracts (CatalogContracts.cs). */

export interface CatalogSet {
  externalId: string
  name: string
  code: string
  releaseDate: string | null
}

/** ImageUrl is never null — the API substitutes a placeholder and flags it via hasImage. */
export interface CatalogCard {
  externalId: string
  name: string
  number: string | null
  rarity: string | null
  imageUrl: string
  hasImage: boolean
  set: CatalogSet | null
}
