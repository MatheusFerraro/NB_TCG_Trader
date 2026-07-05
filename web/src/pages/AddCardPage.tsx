import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { CATALOG_PAGE_SIZE, searchCatalog } from '../features/catalog/catalogApi'
import type { CatalogCard } from '../features/catalog/types'
import { addCard, updateItem } from '../features/collection/collectionApi'
import { emptyDraft, needsDetailsUpdate, toUpdateRequest, validateDraft } from '../features/collection/draft'
import type { Draft } from '../features/collection/draft'
import { CARD_CONDITIONS, CURRENCIES } from '../features/collection/types'
import type { CardCondition, Currency, Page } from '../features/collection/types'

type SearchState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; results: Page<CatalogCard> }

/** Manual add-card flow (BACKLOG #46): search the catalog, pick a card, set details, save. */
export function AddCardPage() {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [set, setSet] = useState('')
  const [number, setNumber] = useState('')
  const [search, setSearch] = useState<SearchState>({ status: 'idle' })
  const [selected, setSelected] = useState<CatalogCard | null>(null)
  const [draft, setDraft] = useState<Draft>(emptyDraft)
  const [errors, setErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  async function handleSearch(event: FormEvent) {
    event.preventDefault()
    setSelected(null)
    setErrors([])
    setSearch({ status: 'loading' })
    try {
      const results = await searchCatalog({ query, set, number })
      setSearch({ status: 'done', results })
    } catch (error) {
      setSearch({
        status: 'error',
        message:
          error instanceof ApiError
            ? error.message
            : 'Could not search the catalog. Please try again.',
      })
    }
  }

  function selectCard(card: CatalogCard) {
    setSelected(card)
    setDraft(emptyDraft())
    setErrors([])
  }

  async function handleAdd(event: FormEvent) {
    event.preventDefault()
    if (!selected) return

    const clientErrors = validateDraft(draft)
    if (clientErrors.length > 0) {
      setErrors(clientErrors)
      return
    }

    setErrors([])
    setSaving(true)
    try {
      const created = await addCard({
        cardExternalId: selected.externalId,
        quantity: Number(draft.quantity),
        condition: draft.condition,
      })

      // POST only accepts card/quantity/condition; sale, private, price, and
      // notes need a follow-up PUT with the full desired state.
      if (needsDetailsUpdate(draft)) {
        try {
          await updateItem(created.id, toUpdateRequest(draft))
        } catch {
          setErrors([
            'The card was added, but its sale/private details could not be saved. Edit it in your binder.',
          ])
          setSaving(false)
          return
        }
      }

      navigate('/binder')
    } catch (error) {
      if (error instanceof ApiError) {
        setErrors(error.fieldErrors.length > 0 ? error.fieldErrors : [error.message])
      } else {
        setErrors(['Could not reach the server. Please try again.'])
      }
      setSaving(false)
    }
  }

  return (
    <section>
      <header className="binder-header">
        <h1>Add a card</h1>
        <Link to="/binder">Back to binder</Link>
      </header>

      <form className="catalog-search" onSubmit={handleSearch}>
        <label>
          Card name
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="e.g. Charizard"
          />
        </label>
        <label>
          Set
          <input value={set} onChange={(e) => setSet(e.target.value)} placeholder="e.g. Base" />
        </label>
        <label>
          Number
          <input
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            placeholder="e.g. 4"
          />
        </label>
        <button type="submit" disabled={search.status === 'loading'}>
          {search.status === 'loading' ? 'Searching…' : 'Search'}
        </button>
      </form>

      {search.status === 'idle' && (
        <p className="binder-empty">Search the catalog to find the card you want to add.</p>
      )}
      {search.status === 'loading' && <p className="page-loading">Searching the catalog…</p>}
      {search.status === 'error' && (
        <p className="form-errors" role="alert">
          {search.message}
        </p>
      )}
      {search.status === 'done' && search.results.items.length === 0 && (
        <p className="binder-empty">No cards matched. Try a different name, set, or number.</p>
      )}

      {search.status === 'done' && search.results.items.length > 0 && (
        <>
          {search.results.totalCount > CATALOG_PAGE_SIZE && (
            <p className="catalog-hint">
              Showing the first {CATALOG_PAGE_SIZE} of {search.results.totalCount} matches —
              refine the search to narrow it down.
            </p>
          )}
          <div className="binder-grid">
            {search.results.items.map((card) => (
              <button
                key={card.externalId}
                type="button"
                className={
                  selected?.externalId === card.externalId
                    ? 'catalog-result selected'
                    : 'catalog-result'
                }
                onClick={() => selectCard(card)}
              >
                <img src={card.imageUrl} alt={card.name} loading="lazy" />
                <h3>{card.name}</h3>
                <p className="binder-card-set">
                  {card.set?.name ?? 'Unknown set'}
                  {card.number !== null && ` · #${card.number}`}
                </p>
                {card.rarity !== null && <p className="binder-card-set">{card.rarity}</p>}
              </button>
            ))}
          </div>
        </>
      )}

      {selected && (
        <form className="add-card-details" onSubmit={handleAdd}>
          <h2>
            Add {selected.name}
            {selected.set !== null && ` (${selected.set.name})`}
          </h2>
          <div className="add-card-fields">
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
            <label className="add-card-notes">
              Notes
              <textarea
                rows={2}
                maxLength={1000}
                value={draft.notes}
                onChange={(e) => setDraft({ ...draft, notes: e.target.value })}
              />
            </label>
          </div>
          {errors.length > 0 && (
            <ul className="form-errors" role="alert">
              {errors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          )}
          <div className="binder-card-actions">
            <button type="submit" disabled={saving}>
              {saving ? 'Adding…' : 'Add to binder'}
            </button>
            <button
              type="button"
              className="link-button"
              onClick={() => setSelected(null)}
              disabled={saving}
            >
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  )
}
