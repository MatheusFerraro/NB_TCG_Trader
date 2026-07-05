import { useEffect, useId, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { FieldHelp } from '../components/FieldHelp'
import { searchCatalog } from '../features/catalog/catalogApi'
import type { CatalogCard } from '../features/catalog/types'
import { addCard, updateItem } from '../features/collection/collectionApi'
import { emptyDraft, needsDetailsUpdate, toUpdateRequest, validateDraft } from '../features/collection/draft'
import type { Draft } from '../features/collection/draft'
import { CARD_CONDITIONS, CURRENCIES } from '../features/collection/types'
import type { CardCondition, Currency, Page } from '../features/collection/types'

/** The filters a search ran with, snapshotted so paging ignores later typing. */
interface ActiveFilters {
  query: string
  set: string
  number: string
}

type SearchState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; results: Page<CatalogCard> }

/** Manual add-card flow (BACKLOG #46): search the catalog, pick a card, set details, save. */
export function AddCardPage() {
  const navigate = useNavigate()
  const uid = useId()
  const [query, setQuery] = useState('')
  const [set, setSet] = useState('')
  const [number, setNumber] = useState('')
  const [active, setActive] = useState<ActiveFilters | null>(null)
  const [search, setSearch] = useState<SearchState>({ status: 'idle' })
  const [selected, setSelected] = useState<CatalogCard | null>(null)
  const [draft, setDraft] = useState<Draft>(emptyDraft)
  const [errors, setErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)
  const detailsRef = useRef<HTMLFormElement>(null)

  // The details panel renders below the results grid, which can put it off
  // screen — picking a card looked like a no-op. Bring it into view and put
  // focus in it so the next step is obvious.
  useEffect(() => {
    if (selected) {
      detailsRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      detailsRef.current?.querySelector('input')?.focus({ preventScroll: true })
    }
  }, [selected])

  async function runSearch(filters: ActiveFilters, page: number) {
    setSelected(null)
    setErrors([])
    setSearch({ status: 'loading' })
    try {
      const results = await searchCatalog(filters, page)
      setActive(filters)
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

  function handleSearch(event: FormEvent) {
    event.preventDefault()
    const filters: ActiveFilters = {
      query: query.trim(),
      set: set.trim(),
      number: number.trim(),
    }
    if (!filters.query && !filters.set && !filters.number) {
      setSearch({ status: 'error', message: 'Type a card name, set, or number to search.' })
      return
    }
    void runSearch(filters, 1)
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

  const results = search.status === 'done' ? search.results : null
  const totalPages = results ? Math.max(1, Math.ceil(results.totalCount / results.pageSize)) : 1

  return (
    <section>
      <header className="binder-header">
        <h1>Add a card</h1>
        <Link to="/binder">Back to binder</Link>
      </header>

      <form className="catalog-search" onSubmit={handleSearch}>
        <div className="field catalog-search-name">
          <label htmlFor={`${uid}-query`} className="field-label">
            Card name
          </label>
          <input
            id={`${uid}-query`}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="e.g. Charizard — the name is all you need"
          />
        </div>
        <div className="field">
          <span className="field-label">
            <label htmlFor={`${uid}-set`}>
              Set <span className="optional">(optional)</span>
            </label>
            <FieldHelp label="What is a set?">
              The expansion or collection the card was released in — for example
              “Base” or “Scarlet &amp; Violet”. Leave it empty if you are not sure.
            </FieldHelp>
          </span>
          <input
            id={`${uid}-set`}
            value={set}
            onChange={(e) => setSet(e.target.value)}
            placeholder="e.g. Base"
          />
        </div>
        <div className="field">
          <span className="field-label">
            <label htmlFor={`${uid}-number`}>
              Number <span className="optional">(optional)</span>
            </label>
            <FieldHelp label="What is a card number?">
              The collector number printed on the card itself, usually near the
              bottom — like “4” in “4/102”. Leave it empty if you are not sure.
            </FieldHelp>
          </span>
          <input
            id={`${uid}-number`}
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            placeholder="e.g. 4"
          />
        </div>
        <button type="submit" disabled={search.status === 'loading'}>
          {search.status === 'loading' ? 'Searching…' : 'Search'}
        </button>
      </form>

      {search.status === 'idle' && (
        <p className="binder-empty">
          Search the catalog to find the card you want to add — the card name alone works.
        </p>
      )}
      {search.status === 'loading' && <p className="page-loading">Searching the catalog…</p>}
      {search.status === 'error' && (
        <p className="form-errors" role="alert">
          {search.message}
        </p>
      )}
      {results && results.items.length === 0 && (
        <p className="binder-empty">No cards matched. Try a different name, set, or number.</p>
      )}

      {results && results.items.length > 0 && (
        <>
          <p className="catalog-hint">
            {results.totalCount} {results.totalCount === 1 ? 'match' : 'matches'}
          </p>
          <div className="catalog-grid">
            {results.items.map((card) => (
              <button
                key={card.externalId}
                type="button"
                className={
                  selected?.externalId === card.externalId
                    ? 'catalog-result selected'
                    : 'catalog-result'
                }
                aria-pressed={selected?.externalId === card.externalId}
                onClick={() => selectCard(card)}
              >
                <img src={card.imageUrl} alt={card.name} loading="lazy" />
                <span className="catalog-result-body">
                  <span className="catalog-result-name">{card.name}</span>
                  <span className="catalog-result-meta">
                    {card.set?.name ?? 'Unknown set'}
                    {card.number !== null && ` · #${card.number}`}
                  </span>
                  {card.rarity !== null && (
                    <span className="rarity-chip">{card.rarity}</span>
                  )}
                </span>
              </button>
            ))}
          </div>
          {totalPages > 1 && active && (
            <nav className="pager" aria-label="Search result pages">
              <button
                type="button"
                onClick={() => void runSearch(active, results.page - 1)}
                disabled={results.page <= 1}
              >
                Previous
              </button>
              <span>
                Page {results.page} of {totalPages}
              </span>
              <button
                type="button"
                onClick={() => void runSearch(active, results.page + 1)}
                disabled={results.page >= totalPages}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}

      {selected && (
        <form className="add-card-details" onSubmit={handleAdd} ref={detailsRef}>
          <h2>
            Add {selected.name}
            {selected.set !== null && ` (${selected.set.name})`}
          </h2>
          <div className="add-card-fields">
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
              <span className="field-label">
                <label htmlFor={`${uid}-condition`}>Condition</label>
                <FieldHelp label="What do the condition codes mean?">
                  How worn the card is: NM Near Mint, LP Lightly Played, MP
                  Moderately Played, HP Heavily Played, DMG Damaged. Pick NM if
                  it looks like new.
                </FieldHelp>
              </span>
              <select
                id={`${uid}-condition`}
                value={draft.condition}
                onChange={(e) => setDraft({ ...draft, condition: e.target.value as CardCondition })}
              >
                {CARD_CONDITIONS.map((condition) => (
                  <option key={condition} value={condition}>
                    {condition}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <span className="field-label">
                <label htmlFor={`${uid}-price`}>Price</label>
                <FieldHelp label="What is the price for?">
                  Your asking price. It is only required when you mark the card
                  for sale — buyers see it in the marketplace.
                </FieldHelp>
              </span>
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
              <FieldHelp label="What does private mean?">
                Private cards stay hidden from other users and never appear in
                the marketplace, even when marked for sale.
              </FieldHelp>
            </div>
            <div className="field add-card-notes">
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
