import { describe, expect, it } from 'vitest'
import { draftFrom, toUpdateRequest } from './draft'
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
