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
