import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import { CONDITION_LABELS, formatPrice } from '../features/collection/format'
import { getListing } from '../features/marketplace/marketplaceApi'
import type { MarketplaceListingDetail } from '../features/marketplace/types'

type DetailState =
  | { status: 'loading' }
  | { status: 'notFound' }
  | { status: 'error'; message: string }
  | { status: 'done'; listing: MarketplaceListingDetail }

const DATE_FORMAT = new Intl.DateTimeFormat('en-CA', { dateStyle: 'medium' })

/**
 * Focused listing page (BACKLOG #22): card image and facts on one side, price,
 * seller, and contact channels on the other. This is the only surface that
 * shows contact handles, and only from a live GET /marketplace/{itemId} — a
 * private, sold, or missing listing 404s server-side and renders the same
 * not-found state, leaking nothing (CLAUDE.md §15).
 */
export function ListingDetailPage() {
  const { itemId } = useParams()
  // The API route is {itemId:int}; a non-numeric id can't exist, so skip the round trip.
  const id = /^\d+$/.test(itemId ?? '') ? Number(itemId) : null
  const [state, setState] = useState<DetailState>(
    id === null ? { status: 'notFound' } : { status: 'loading' },
  )

  useEffect(() => {
    if (id === null) return
    let cancelled = false
    getListing(id)
      .then((listing) => {
        if (!cancelled) setState({ status: 'done', listing })
      })
      .catch((e: unknown) => {
        if (cancelled) return
        if (e instanceof ApiError && e.status === 404) {
          setState({ status: 'notFound' })
        } else {
          setState({
            status: 'error',
            message:
              e instanceof ApiError
                ? e.message
                : 'Could not load this listing. Please try again.',
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [id])

  if (state.status === 'loading') {
    return <p className="page-loading">Loading listing…</p>
  }

  if (state.status === 'notFound') {
    return (
      <section>
        <h1>Listing not found</h1>
        <p>
          This listing doesn't exist or is no longer for sale.{' '}
          <Link to="/marketplace">Back to the marketplace</Link>
        </p>
      </section>
    )
  }

  if (state.status === 'error') {
    return (
      <section>
        <h1>Marketplace</h1>
        <p className="form-errors" role="alert">
          {state.message}
        </p>
        <p>
          <Link to="/marketplace">Back to the marketplace</Link>
        </p>
      </section>
    )
  }

  const { listing } = state
  const { card, seller } = listing
  const location = [seller.city, seller.country].filter(Boolean).join(', ')
  const posted = DATE_FORMAT.format(new Date(listing.createdAt))
  const updated = DATE_FORMAT.format(new Date(listing.updatedAt))
  const hasContact = Boolean(
    seller.contactEmail || seller.discordHandle || seller.instagramHandle,
  )

  return (
    <section>
      <nav className="listing-breadcrumb" aria-label="Breadcrumb">
        <Link to="/marketplace">← Marketplace</Link>
      </nav>

      <div className="listing-layout">
        <img className="listing-image" src={card.imageUrl} alt={card.name} />

        <div className="listing-info">
          <header className="listing-identity">
            <h1>{card.name}</h1>
            <p>
              {card.gameName} · {card.setName ?? 'Unknown set'}
              {card.number !== null && ` · #${card.number}`}
            </p>
            {card.rarity !== null && <span className="rarity-chip">{card.rarity}</span>}
          </header>

          <p className="listing-price">
            {listing.price !== null
              ? formatPrice(listing.price, listing.currency)
              : 'Price on request'}
          </p>

          <dl className="listing-facts">
            <div>
              <dt>Condition</dt>
              <dd>{CONDITION_LABELS[listing.condition]}</dd>
            </div>
            <div>
              <dt>Quantity</dt>
              <dd>{listing.quantity}</dd>
            </div>
            <div>
              <dt>Posted</dt>
              <dd>{posted}</dd>
            </div>
            {updated !== posted && (
              <div>
                <dt>Updated</dt>
                <dd>{updated}</dd>
              </div>
            )}
          </dl>

          {listing.notes !== null && <p className="listing-notes">{listing.notes}</p>}

          <div className="listing-seller">
            <h2>Seller</h2>
            <p className="listing-seller-name">
              {seller.displayName}
              {location && <span className="listing-seller-location"> · {location}</span>}
            </p>

            <h3>Contact seller</h3>
            {hasContact ? (
              <ul className="listing-contact">
                {seller.contactEmail && (
                  <li>
                    <span className="listing-contact-label">Email</span>
                    <a href={`mailto:${seller.contactEmail}`}>{seller.contactEmail}</a>
                  </li>
                )}
                {seller.discordHandle && (
                  <li>
                    <span className="listing-contact-label">Discord</span>
                    <span>{seller.discordHandle}</span>
                  </li>
                )}
                {seller.instagramHandle && (
                  <li>
                    <span className="listing-contact-label">Instagram</span>
                    <a
                      href={`https://instagram.com/${seller.instagramHandle.replace(/^@/, '')}`}
                      target="_blank"
                      rel="noreferrer"
                    >
                      {seller.instagramHandle}
                    </a>
                  </li>
                )}
              </ul>
            ) : (
              <p className="listing-contact-empty">
                This seller hasn't shared contact details yet.
              </p>
            )}
          </div>
        </div>
      </div>
    </section>
  )
}
