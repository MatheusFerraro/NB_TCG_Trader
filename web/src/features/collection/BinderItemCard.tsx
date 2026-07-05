import { formatPrice } from './format'
import type { CollectionItem } from './types'

interface BinderItemCardProps {
  item: CollectionItem
  onManage: (item: CollectionItem) => void
}

/**
 * Display-only binder grid tile (BACKLOG #20/#47). The whole tile is a button
 * that opens the focused manage dialog — editing no longer happens inline.
 */
export function BinderItemCard({ item, onManage }: BinderItemCardProps) {
  return (
    <button type="button" className="binder-card" onClick={() => onManage(item)}>
      <img src={item.card.imageUrl} alt={item.card.name} loading="lazy" />
      <span className="binder-card-body">
        <span className="binder-card-name">{item.card.name}</span>
        <span className="binder-card-set">
          {item.card.setName ?? 'Unknown set'}
          {item.card.number !== null && ` · #${item.card.number}`}
        </span>
        <span className="binder-card-facts">
          <span>×{item.quantity}</span>
          <span>{item.condition}</span>
          {item.price !== null && <span>{formatPrice(item.price, item.currency)}</span>}
        </span>
        <span className="binder-card-badges">
          {item.isForSale && <span className="badge badge-sale">For sale</span>}
          {item.isPrivate && <span className="badge badge-private">Private</span>}
          <span className="binder-card-manage">Manage</span>
        </span>
      </span>
    </button>
  )
}
