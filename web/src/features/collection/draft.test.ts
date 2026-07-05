import { describe, expect, it } from 'vitest'
import { draftFrom, emptyDraft, needsDetailsUpdate, toUpdateRequest, validateDraft } from './draft'
import type { CollectionItem } from './types'

const item: CollectionItem = {
  id: 1,
  card: {
    id: 10,
    externalId: 'base1-4',
    name: 'Charizard',
    number: '4',
    rarity: 'Rare Holo',
    imageUrl: 'https://img.example/base1-4.png',
    hasImage: true,
    setName: 'Base',
  },
  quantity: 2,
  condition: 'LP',
  isForSale: true,
  price: 350,
  currency: 'CAD',
  isPrivate: false,
  notes: 'shadowless',
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
}

describe('draftFrom / toUpdateRequest', () => {
  it('round-trips an unchanged item, preserving notes and price', () => {
    expect(toUpdateRequest(draftFrom(item))).toEqual({
      quantity: 2,
      condition: 'LP',
      isForSale: true,
      price: 350,
      currency: 'CAD',
      isPrivate: false,
      notes: 'shadowless',
    })
  })

  it('maps cleared price and notes inputs to null', () => {
    const draft = { ...draftFrom(item), price: '  ', notes: '' }

    const request = toUpdateRequest(draft)

    expect(request.price).toBeNull()
    expect(request.notes).toBeNull()
  })

  it('renders a null price as an empty input value', () => {
    const draft = draftFrom({ ...item, price: null, notes: null })

    expect(draft.price).toBe('')
    expect(draft.notes).toBe('')
  })
})

describe('validateDraft', () => {
  it('accepts the add-flow defaults', () => {
    expect(validateDraft(emptyDraft())).toEqual([])
  })

  it('rejects a for-sale draft without a price', () => {
    const draft = { ...emptyDraft(), isForSale: true }

    expect(validateDraft(draft)).toEqual(['Price is required when the item is for sale.'])
  })

  it('rejects non-numeric and non-positive prices when provided', () => {
    expect(validateDraft({ ...emptyDraft(), price: 'abc' })).toEqual([
      'Price must be greater than 0.',
    ])
    expect(validateDraft({ ...emptyDraft(), price: '0' })).toEqual([
      'Price must be greater than 0.',
    ])
    expect(validateDraft({ ...emptyDraft(), price: '-1' })).toEqual([
      'Price must be greater than 0.',
    ])
  })

  it('rejects out-of-range and non-integer quantities', () => {
    expect(validateDraft({ ...emptyDraft(), quantity: '0' })).toEqual([
      'Quantity must be between 1 and 999.',
    ])
    expect(validateDraft({ ...emptyDraft(), quantity: '2.5' })).toEqual([
      'Quantity must be between 1 and 999.',
    ])
  })
})

describe('needsDetailsUpdate', () => {
  it('is false for the POST-compatible defaults', () => {
    expect(needsDetailsUpdate(emptyDraft())).toBe(false)
  })

  it.each([
    { name: 'for sale', patch: { isForSale: true } },
    { name: 'private', patch: { isPrivate: true } },
    { name: 'price', patch: { price: '9.99' } },
    { name: 'currency', patch: { currency: 'BRL' as const } },
    { name: 'notes', patch: { notes: 'trade binder' } },
  ])('is true when $name is set', ({ patch }) => {
    expect(needsDetailsUpdate({ ...emptyDraft(), ...patch })).toBe(true)
  })
})
