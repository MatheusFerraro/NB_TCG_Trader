import { useEffect, useId, useRef, useState } from 'react'
import type { FormEvent, MouseEvent } from 'react'
import { ApiError } from '../../lib/apiClient'
import { deleteItem, updateItem } from './collectionApi'
import { draftFrom, toUpdateRequest, validateDraft } from './draft'
import type { Draft } from './draft'
import { CARD_CONDITIONS, CURRENCIES } from './types'
import type { CardCondition, CollectionItem, Currency } from './types'

interface ManageItemDialogProps {
  item: CollectionItem
  onSaved: (item: CollectionItem) => void
  onDeleted: (id: number) => void
  onClose: () => void
}

/**
 * Focused manage view for one binder item (BACKLOG #47): a native <dialog> so
 * Escape, focus containment, and the backdrop come from the platform. Ownership
 * fields are grouped apart from listing fields; delete sits behind an in-dialog
 * confirmation step and uses DELETE /collection/items/{id}.
 */
export function ManageItemDialog({ item, onSaved, onDeleted, onClose }: ManageItemDialogProps) {
  const uid = useId()
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [draft, setDraft] = useState<Draft>(() => draftFrom(item))
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState<'idle' | 'saving' | 'deleting'>('idle')
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  useEffect(() => {
    const dialog = dialogRef.current
    dialog?.showModal()
    return () => dialog?.close()
  }, [])

  function handleBackdropClick(event: MouseEvent<HTMLDialogElement>) {
    // Clicks on the ::backdrop hit the <dialog> element itself; clicks on the
    // content hit its children.
    if (event.target === dialogRef.current && busy === 'idle') {
      onClose()
    }
  }

  function showApiError(error: unknown) {
    if (error instanceof ApiError) {
      setErrors(error.fieldErrors.length > 0 ? error.fieldErrors : [error.message])
    } else {
      setErrors(['Could not reach the server. Please try again.'])
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const clientErrors = validateDraft(draft)
    if (clientErrors.length > 0) {
      setErrors(clientErrors)
      return
    }
    setErrors([])
    setBusy('saving')
    try {
      const updated = await updateItem(item.id, toUpdateRequest(draft))
      onSaved(updated)
      onClose()
    } catch (error) {
      showApiError(error)
      setBusy('idle')
    }
  }

  async function handleDelete() {
    setErrors([])
    setBusy('deleting')
    try {
      await deleteItem(item.id)
      onDeleted(item.id)
    } catch (error) {
      showApiError(error)
      setBusy('idle')
    }
  }

  const card = item.card

  return (
    <dialog
      ref={dialogRef}
      className="manage-dialog"
      aria-labelledby={`${uid}-title`}
      // No onClose handler on purpose: the mount effect's StrictMode cleanup
      // calls dialog.close(), and a close-event -> onClose chain would unmount
      // the dialog the moment it opened. Escape arrives via onCancel instead.
      onCancel={(event) => {
        if (busy !== 'idle') {
          event.preventDefault()
          return
        }
        onClose()
      }}
      onClick={handleBackdropClick}
    >
      <div className="manage-layout">
        <img className="manage-image" src={card.imageUrl} alt={card.name} />

        <div className="manage-body">
          <header className="manage-identity">
            <h2 id={`${uid}-title`}>{card.name}</h2>
            <p>
              {card.setName ?? 'Unknown set'}
              {card.number !== null && ` · #${card.number}`}
              {card.rarity !== null && ` · ${card.rarity}`}
            </p>
          </header>

          <form onSubmit={handleSubmit}>
            <fieldset className="manage-group">
              <legend>Your copy</legend>
              <div className="manage-fields">
                <div className="field">
                  <label htmlFor={`${uid}-quantity`} className="field-label">
                    Quantity
                  </label>
                  <input
                    id={`${uid}-quantity`}
                    type="number"
                    min={1}
                    max={999}
                    value={draft.quantity}
                    onChange={(e) => setDraft({ ...draft, quantity: e.target.value })}
                    required
                  />
                </div>
                <div className="field">
                  <label htmlFor={`${uid}-condition`} className="field-label">
                    Condition
                  </label>
                  <select
                    id={`${uid}-condition`}
                    value={draft.condition}
                    onChange={(e) =>
                      setDraft({ ...draft, condition: e.target.value as CardCondition })
                    }
                  >
                    {CARD_CONDITIONS.map((condition) => (
                      <option key={condition} value={condition}>
                        {condition}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field manage-notes">
                  <label htmlFor={`${uid}-notes`} className="field-label">
                    Notes
                  </label>
                  <textarea
                    id={`${uid}-notes`}
                    rows={2}
                    maxLength={1000}
                    value={draft.notes}
                    onChange={(e) => setDraft({ ...draft, notes: e.target.value })}
                  />
                </div>
              </div>
            </fieldset>

            <fieldset className="manage-group">
              <legend>Selling</legend>
              <div className="manage-fields">
                <div className="field field-check">
                  <label htmlFor={`${uid}-for-sale`}>
                    <input
                      id={`${uid}-for-sale`}
                      type="checkbox"
                      checked={draft.isForSale}
                      onChange={(e) => setDraft({ ...draft, isForSale: e.target.checked })}
                    />
                    For sale
                  </label>
                </div>
                <div className="field field-check">
                  <label htmlFor={`${uid}-private`}>
                    <input
                      id={`${uid}-private`}
                      type="checkbox"
                      checked={draft.isPrivate}
                      onChange={(e) => setDraft({ ...draft, isPrivate: e.target.checked })}
                    />
                    Private
                  </label>
                </div>
                <div className="field">
                  <label htmlFor={`${uid}-price`} className="field-label">
                    Price
                  </label>
                  <input
                    id={`${uid}-price`}
                    type="number"
                    min="0.01"
                    step="0.01"
                    value={draft.price}
                    onChange={(e) => setDraft({ ...draft, price: e.target.value })}
                    placeholder="No price"
                  />
                </div>
                <div className="field">
                  <label htmlFor={`${uid}-currency`} className="field-label">
                    Currency
                  </label>
                  <select
                    id={`${uid}-currency`}
                    value={draft.currency}
                    onChange={(e) => setDraft({ ...draft, currency: e.target.value as Currency })}
                  >
                    {CURRENCIES.map((currency) => (
                      <option key={currency} value={currency}>
                        {currency}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            </fieldset>

            {errors.length > 0 && (
              <ul className="form-errors" role="alert">
                {errors.map((error) => (
                  <li key={error}>{error}</li>
                ))}
              </ul>
            )}

            <div className="manage-actions">
              <button type="submit" className="manage-save" disabled={busy !== 'idle'}>
                {busy === 'saving' ? 'Saving…' : 'Save changes'}
              </button>
              <button
                type="button"
                className="link-button"
                onClick={onClose}
                disabled={busy !== 'idle'}
              >
                Cancel
              </button>
            </div>
          </form>

          <div className="manage-danger">
            {!confirmingDelete && (
              <button
                type="button"
                className="manage-delete"
                onClick={() => setConfirmingDelete(true)}
                disabled={busy !== 'idle'}
              >
                Delete from binder
              </button>
            )}
            {confirmingDelete && (
              <div className="manage-confirm" role="alertdialog" aria-label="Confirm deletion">
                <p>
                  Delete <strong>{card.name}</strong> from your binder? This cannot be undone.
                </p>
                <div className="manage-actions">
                  <button
                    type="button"
                    className="manage-delete"
                    onClick={() => void handleDelete()}
                    disabled={busy !== 'idle'}
                  >
                    {busy === 'deleting' ? 'Deleting…' : 'Yes, delete it'}
                  </button>
                  <button
                    type="button"
                    className="link-button"
                    onClick={() => setConfirmingDelete(false)}
                    disabled={busy !== 'idle'}
                  >
                    Keep the card
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </dialog>
  )
}
