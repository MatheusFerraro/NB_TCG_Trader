import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { CURRENCIES, type Currency } from '../collection/types'
import { FieldHelp } from '../../components/FieldHelp'
import type { ListingFilters } from './marketplaceApi'

interface MarketplaceFiltersProps {
  /** Current applied filters; seeds the inputs. Key the component to reset it. */
  values: ListingFilters
  searching: boolean
  onApply: (filters: ListingFilters) => void
}

/**
 * Browse filter bar (BACKLOG #22): the name search stays front and center; the
 * rarer filters (set, price, location) fold away behind "More filters" so the
 * first view stays approachable. Mirrors the API rule that a price range only
 * makes sense within one currency.
 */
export function MarketplaceFilters({ values, searching, onApply }: MarketplaceFiltersProps) {
  const uid = useId()
  const [name, setName] = useState(values.name ?? '')
  const [game, setGame] = useState(values.game ?? '')
  const [set, setSet] = useState(values.set ?? '')
  const [minPrice, setMinPrice] = useState(values.minPrice ?? '')
  const [maxPrice, setMaxPrice] = useState(values.maxPrice ?? '')
  const [currency, setCurrency] = useState<Currency | ''>(values.currency ?? '')
  const [city, setCity] = useState(values.city ?? '')
  const [country, setCountry] = useState(values.country ?? '')
  const [error, setError] = useState<string | null>(null)
  const hasAdvanced = Boolean(
    values.game ||
      values.set ||
      values.minPrice ||
      values.maxPrice ||
      values.currency ||
      values.city ||
      values.country,
  )

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // Same rule the API enforces; catching it here gives a friendlier message.
    if ((minPrice.trim() || maxPrice.trim()) && !currency) {
      setError('Pick a currency (CAD or BRL) when filtering by price.')
      return
    }
    setError(null)
    onApply({
      name: name.trim(),
      game,
      set: set.trim(),
      minPrice: minPrice.trim(),
      maxPrice: maxPrice.trim(),
      currency,
      city: city.trim(),
      country: country.trim(),
    })
  }

  function handleClear() {
    setName('')
    setGame('')
    setSet('')
    setMinPrice('')
    setMaxPrice('')
    setCurrency('')
    setCity('')
    setCountry('')
    setError(null)
    onApply({})
  }

  return (
    <form className="market-filters" onSubmit={handleSubmit}>
      <div className="market-filters-main">
        <div className="field market-filters-name">
          <label htmlFor={`${uid}-name`} className="field-label">
            Card name
          </label>
          <input
            id={`${uid}-name`}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Charizard"
          />
        </div>
        <button type="submit" disabled={searching}>
          {searching ? 'Searching…' : 'Search'}
        </button>
      </div>

      <details className="market-filters-more" open={hasAdvanced}>
        <summary>More filters</summary>
        <div className="market-filters-grid">
          <div className="field">
            <label htmlFor={`${uid}-game`} className="field-label">
              Game
            </label>
            {/* The catalog ships Pokémon-only for now; the API filters by game slug. */}
            <select id={`${uid}-game`} value={game} onChange={(e) => setGame(e.target.value)}>
              <option value="">Any game</option>
              <option value="pokemon">Pokémon</option>
            </select>
          </div>
          <div className="field">
            <span className="field-label">
              <label htmlFor={`${uid}-set`}>Set</label>
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
            <label htmlFor={`${uid}-min`} className="field-label">
              Min price
            </label>
            <input
              id={`${uid}-min`}
              type="number"
              min="0"
              step="0.01"
              inputMode="decimal"
              value={minPrice}
              onChange={(e) => setMinPrice(e.target.value)}
              placeholder="0.00"
            />
          </div>
          <div className="field">
            <label htmlFor={`${uid}-max`} className="field-label">
              Max price
            </label>
            <input
              id={`${uid}-max`}
              type="number"
              min="0"
              step="0.01"
              inputMode="decimal"
              value={maxPrice}
              onChange={(e) => setMaxPrice(e.target.value)}
              placeholder="100.00"
            />
          </div>
          <div className="field">
            <span className="field-label">
              <label htmlFor={`${uid}-currency`}>Currency</label>
              <FieldHelp label="Why pick a currency?">
                Sellers price in Canadian dollars (CAD) or Brazilian reais (BRL).
                A price range only applies within the currency you pick.
              </FieldHelp>
            </span>
            <select
              id={`${uid}-currency`}
              value={currency}
              onChange={(e) => setCurrency(e.target.value as Currency | '')}
            >
              <option value="">Any</option>
              {CURRENCIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${uid}-city`} className="field-label">
              City
            </label>
            <input
              id={`${uid}-city`}
              value={city}
              onChange={(e) => setCity(e.target.value)}
              placeholder="Filter by city"
            />
          </div>
          <div className="field">
            <label htmlFor={`${uid}-country`} className="field-label">
              Country
            </label>
            <input
              id={`${uid}-country`}
              value={country}
              onChange={(e) => setCountry(e.target.value)}
              placeholder="e.g. Canada"
            />
          </div>
        </div>
        <button type="button" className="link-button" onClick={handleClear}>
          Clear all filters
        </button>
      </details>

      {error && (
        <p className="form-errors" role="alert">
          {error}
        </p>
      )}
    </form>
  )
}
