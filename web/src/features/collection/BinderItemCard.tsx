import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../../lib/apiClient'
import { updateItem } from './collectionApi'
import { draftFrom, toUpdateRequest } from './draft'
import type { Draft } from './draft'
import { formatPrice } from './format'
import { CARD_CONDITIONS, CURRENCIES } from './types'
import type { CardCondition, CollectionItem, Currency } from './types'

interface BinderItemCardProps {
  item: CollectionItem
  onSaved: (item: CollectionItem) => void
}

export function BinderItemCard({ item, onSaved }: BinderItemCardProps) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<Draft>(() => draftFrom(item))
  const [errors, setErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  function startEdit() {
    setDraft(draftFrom(item))
    setErrors([])
    setEditing(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])
    setSaving(true)
    try {
      const updated = await updateItem(item.id, toUpdateRequest(draft))
      onSaved(updated)
      setEditing(false)
    } catch (error) {
      if (error instanceof ApiError) {
        setErrors(error.fieldErrors.length > 0 ? error.fieldErrors : [error.message])
      } else {
        setErrors(['Could not reach the server. Please try again.'])
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <article className="binder-card">
      <img src={item.card.imageUrl} alt={item.card.name} loading="lazy" />
      <h3>{item.card.name}</h3>
      <p className="binder-card-set">
        {item.card.setName ?? 'Unknown set'}
        {item.card.number !== null && ` · #${item.card.number}`}
      </p>

      {!editing && (
        <>
          <p className="binder-card-facts">
            <span>×{item.quantity}</span>
            <span>{item.condition}</span>
            {item.price !== null && <span>{formatPrice(item.price, item.currency)}</span>}
          </p>
          <p className="binder-card-badges">
            {item.isForSale && <span className="badge badge-sale">For sale</span>}
            {item.isPrivate && <span className="badge badge-private">Private</span>}
          </p>
          <button type="button" className="binder-card-edit" onClick={startEdit}>
            Edit
          </button>
        </>
      )}

      {editing && (
        <form onSubmit={handleSubmit} noValidate>
          <label>
            Quantity
            <input
              type="number"
              min={1}
              max={999}
              value={draft.quantity}
              onChange={(e) => setDraft({ ...draft, quantity: e.target.value })}
              required
            />
          </label>
          <label>
            Condition
            <select
              value={draft.condition}
              onChange={(e) => setDraft({ ...draft, condition: e.target.value as CardCondition })}
            >
              {CARD_CONDITIONS.map((condition) => (
                <option key={condition} value={condition}>
                  {condition}
                </option>
              ))}
            </select>
          </label>
          <label>
            Price
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={draft.price}
              onChange={(e) => setDraft({ ...draft, price: e.target.value })}
              placeholder="No price"
            />
          </label>
          <label>
            Currency
            <select
              value={draft.currency}
              onChange={(e) => setDraft({ ...draft, currency: e.target.value as Currency })}
            >
              {CURRENCIES.map((currency) => (
                <option key={currency} value={currency}>
                  {currency}
                </option>
              ))}
            </select>
          </label>
          <label className="binder-card-check">
            <input
              type="checkbox"
              checked={draft.isForSale}
              onChange={(e) => setDraft({ ...draft, isForSale: e.target.checked })}
            />
            For sale
          </label>
          <label className="binder-card-check">
            <input
              type="checkbox"
              checked={draft.isPrivate}
              onChange={(e) => setDraft({ ...draft, isPrivate: e.target.checked })}
            />
            Private
          </label>
          <label>
            Notes
            <textarea
              rows={2}
              maxLength={1000}
              value={draft.notes}
              onChange={(e) => setDraft({ ...draft, notes: e.target.value })}
            />
          </label>
          {errors.length > 0 && (
            <ul className="form-errors" role="alert">
              {errors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          )}
          <div className="binder-card-actions">
            <button type="submit" disabled={saving}>
              {saving ? 'Saving…' : 'Save'}
            </button>
            <button
              type="button"
              className="link-button"
              onClick={() => setEditing(false)}
              disabled={saving}
            >
              Cancel
            </button>
          </div>
        </form>
      )}
    </article>
  )
}
