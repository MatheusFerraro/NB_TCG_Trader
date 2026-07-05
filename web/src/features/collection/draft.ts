import type { CardCondition, CollectionItem, Currency, UpdateItemRequest } from './types'

/** Editable string mirror of an item's mutable fields, so form inputs stay controlled. */
export interface Draft {
  quantity: string
  condition: CardCondition
  isForSale: boolean
  price: string
  currency: Currency
  isPrivate: boolean
  notes: string
}

/** Defaults for the add-card flow; matches what POST /collection/items creates. */
export function emptyDraft(): Draft {
  return {
    quantity: '1',
    condition: 'NM',
    isForSale: false,
    price: '',
    currency: 'CAD',
    isPrivate: false,
    notes: '',
  }
}

/**
 * Client-side mirror of the server rules that would otherwise only surface as
 * ProblemDetails after a request: quantity bounds and for-sale-requires-price.
 */
export function validateDraft(draft: Draft): string[] {
  const errors: string[] = []
  const quantity = Number(draft.quantity)
  if (!Number.isInteger(quantity) || quantity < 1 || quantity > 999) {
    errors.push('Quantity must be between 1 and 999.')
  }
  const priceText = draft.price.trim()
  if (draft.isForSale && priceText === '') {
    errors.push('Price is required when the item is for sale.')
  }
  if (priceText !== '') {
    const price = Number(priceText)
    if (!Number.isFinite(price) || price <= 0) {
      errors.push('Price must be greater than 0.')
    }
  }
  return errors
}

/**
 * True when the draft carries state POST /collection/items cannot accept, so
 * the add flow needs a follow-up PUT.
 */
export function needsDetailsUpdate(draft: Draft): boolean {
  return (
    draft.isForSale ||
    draft.isPrivate ||
    draft.price.trim() !== '' ||
    draft.currency !== 'CAD' ||
    draft.notes.trim() !== ''
  )
}

export function draftFrom(item: CollectionItem): Draft {
  return {
    quantity: String(item.quantity),
    condition: item.condition,
    isForSale: item.isForSale,
    price: item.price !== null ? item.price.toFixed(2) : '',
    currency: item.currency,
    isPrivate: item.isPrivate,
    notes: item.notes ?? '',
  }
}

/**
 * The PUT endpoint has full-replacement semantics, so the draft carries every
 * mutable field (including notes) and sends them all back on save.
 */
export function toUpdateRequest(draft: Draft): UpdateItemRequest {
  return {
    quantity: Number(draft.quantity),
    condition: draft.condition,
    isForSale: draft.isForSale,
    price: draft.price.trim() === '' ? null : Number(draft.price),
    currency: draft.currency,
    isPrivate: draft.isPrivate,
    notes: draft.notes.trim() === '' ? null : draft.notes,
  }
}
