import { Link } from 'react-router-dom'
import { CONDITION_LABELS, formatPrice } from '../collection/format'
import type { MarketplaceListing } from './types'

interface ListingCardProps {
  listing: MarketplaceListing
}

/**
 * Browse grid tile (BACKLOG #22): image, name, price, condition, and seller
 * location — the buyer's shortlist facts. Deliberately no contact handles here;
 * those live on the detail page only (CLAUDE.md §15).
 */
export function ListingCard({ listing }: ListingCardProps) {
  const { card, seller } = listing
  const location = [seller.city, seller.country].filter(Boolean).join(', ')

  return (
    <Link to={`/marketplace/${listing.id}`} className="listing-card">
      <img src={card.imageUrl} alt={card.name} loading="lazy" />
      <span className="listing-card-body">
        <span className="listing-card-name">{card.name}</span>
        <span className="listing-card-set">
          {card.setName ?? 'Unknown set'}
          {card.number !== null && ` · #${card.number}`}
        </span>
        <span className="listing-card-price">
          {listing.price !== null
            ? formatPrice(listing.price, listing.currency)
            : 'Ask the seller'}
        </span>
        <span className="listing-card-facts">
          <span>{CONDITION_LABELS[listing.condition]}</span>
          <span>×{listing.quantity}</span>
        </span>
        {location && <span className="listing-card-location">{location}</span>}
      </span>
    </Link>
  )
}
