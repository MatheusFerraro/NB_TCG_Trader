import { useEffect, useId, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../../lib/apiClient'
import { FieldHelp } from '../../components/FieldHelp'
import { searchCatalog } from './catalogApi'
import type { CatalogCard } from './types'
import type { Page } from '../collection/types'

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

interface CatalogCardPickerProps {
  /** Pre-filled search filters, e.g. an import row's raw name/set/number. */
  initialQuery?: string
  initialSet?: string
  initialNumber?: string
  /** Run a search with the initial filters as soon as the picker mounts. */
  autoSearch?: boolean
  selected: CatalogCard | null
  onSelect: (card: CatalogCard) => void
}

/**
 * Search-and-pick over the public catalog (BACKLOG #21): the same search form,
 * set/number help copy, and result tiles as the manual add-card flow (#46), as
 * a reusable component. The parent owns what "picking" means — it renders the
 * confirm action next to its own context (e.g. resolving an import row).
 */
export function CatalogCardPicker({
  initialQuery = '',
  initialSet = '',
  initialNumber = '',
  autoSearch = false,
  selected,
  onSelect,
}: CatalogCardPickerProps) {
  const uid = useId()
  const [query, setQuery] = useState(initialQuery)
  const [set, setSet] = useState(initialSet)
  const [number, setNumber] = useState(initialNumber)
  const [active, setActive] = useState<ActiveFilters | null>(null)
  // With autoSearch the picker mounts already loading (the effect below only
  // fires the request), so no synchronous set-state runs inside the effect.
  const hasInitialFilters = Boolean(
    initialQuery.trim() || initialSet.trim() || initialNumber.trim(),
  )
  const [search, setSearch] = useState<SearchState>(
    autoSearch && hasInitialFilters ? { status: 'loading' } : { status: 'idle' },
  )
  const autoSearched = useRef(false)

  async function finishSearch(filters: ActiveFilters, page: number) {
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

  function runSearch(filters: ActiveFilters, page: number) {
    setSearch({ status: 'loading' })
    void finishSearch(filters, page)
  }

  // One automatic search from the pre-filled filters, so the user usually sees
  // candidate cards without typing anything. Settles asynchronously via the
  // promise chain; the mount state above already shows the loading message.
  useEffect(() => {
    if (!autoSearch || autoSearched.current) return
    autoSearched.current = true
    const filters: ActiveFilters = {
      query: initialQuery.trim(),
      set: initialSet.trim(),
      number: initialNumber.trim(),
    }
    if (!filters.query && !filters.set && !filters.number) return
    searchCatalog(filters, 1)
      .then((results) => {
        setActive(filters)
        setSearch({ status: 'done', results })
      })
      .catch((error: unknown) => {
        setSearch({
          status: 'error',
          message:
            error instanceof ApiError
              ? error.message
              : 'Could not search the catalog. Please try again.',
        })
      })
  }, [autoSearch, initialQuery, initialSet, initialNumber])

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
    runSearch(filters, 1)
  }

  const results = search.status === 'done' ? search.results : null
  const totalPages = results ? Math.max(1, Math.ceil(results.totalCount / results.pageSize)) : 1

  return (
    <div className="catalog-picker">
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
          Search the catalog to find the right card — the card name alone works.
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
                onClick={() => onSelect(card)}
              >
                <img src={card.imageUrl} alt={card.name} loading="lazy" />
                <span className="catalog-result-body">
                  <span className="catalog-result-name">{card.name}</span>
                  <span className="catalog-result-meta">
                    {card.set?.name ?? 'Unknown set'}
                    {card.number !== null && ` · #${card.number}`}
                  </span>
                  {card.rarity !== null && <span className="rarity-chip">{card.rarity}</span>}
                </span>
              </button>
            ))}
          </div>
          {totalPages > 1 && active && (
            <nav className="pager" aria-label="Search result pages">
              <button
                type="button"
                onClick={() => runSearch(active, results.page - 1)}
                disabled={results.page <= 1}
              >
                Previous
              </button>
              <span>
                Page {results.page} of {totalPages}
              </span>
              <button
                type="button"
                onClick={() => runSearch(active, results.page + 1)}
                disabled={results.page >= totalPages}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  )
}
