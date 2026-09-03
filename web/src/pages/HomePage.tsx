import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../features/auth/authContext'
import { FEATURED_CARDS, HERO_CARDS, type FeaturedCard } from './featuredCards'

const STEPS = [
  {
    title: 'Upload your list',
    body: 'Import a CSV or Excel spreadsheet instead of adding every card manually.',
  },
  {
    title: 'Review your binder',
    body: 'Match cards, confirm quantities and conditions, and organize the collection visually.',
  },
  {
    title: 'List cards for sale',
    body: 'Choose a price, publish the card, and let interested collectors contact you.',
  },
] as const

const BENEFITS = ['CSV & Excel import', 'Visual card binder', 'Simple marketplace listings'] as const

/**
 * Card art with a typographic fallback. The images come from a third-party CDN
 * we do not control, so a 404 or a blocked request must not leave a torn image
 * icon in the middle of the landing page. The wrapper owns the 63:88 box, so the
 * swap costs no layout shift.
 */
function CardArt({ card, eager = false }: { card: FeaturedCard; eager?: boolean }) {
  const [failed, setFailed] = useState(false)

  if (failed) {
    return (
      <span className="card-art card-art-fallback" aria-hidden="true">
        <span className="card-art-fallback-name">{card.name}</span>
      </span>
    )
  }

  return (
    <img
      className="card-art"
      src={card.imageUrl}
      alt={`${card.name} — ${card.set}`}
      width={245}
      height={342}
      decoding="async"
      loading={eager ? 'eager' : 'lazy'}
      fetchPriority={eager ? 'high' : undefined}
      onError={() => setFailed(true)}
    />
  )
}

export function HomePage() {
  const { status } = useAuth()
  const authenticated = status === 'authenticated'

  const primaryCta = authenticated
    ? { to: '/binder/import', label: 'Upload your cards' }
    : { to: '/register', label: 'Create your free account' }

  return (
    <div className="home">
      <section className="hero" aria-labelledby="hero-heading">
        <div className="hero-copy">
          <p className="hero-eyebrow">Collection manager &amp; marketplace</p>
          <h1 id="hero-heading">Turn your spreadsheet into a card shop.</h1>
          <p className="hero-lede">
            Upload your Excel or CSV card list, organize your collection in a visual binder, and
            put your cards up for sale in minutes.
          </p>
          <div className="hero-actions">
            <Link className="cta" to={primaryCta.to}>
              {primaryCta.label}
            </Link>
            <Link className="cta cta-outline" to="/marketplace">
              Browse the marketplace
            </Link>
          </div>
          <ul className="hero-benefits">
            {BENEFITS.map((benefit) => (
              <li key={benefit}>
                <svg
                  viewBox="0 0 16 16"
                  width="14"
                  height="14"
                  aria-hidden="true"
                  focusable="false"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="m3 8.5 3.2 3.2L13 5" />
                </svg>
                {benefit}
              </li>
            ))}
          </ul>
        </div>

        {/* Decorative fan. The alt text on each image still names the card, but
            the group is presentational — the copy beside it carries the pitch. */}
        <div className="hero-fan">
          {HERO_CARDS.map((card, index) => (
            <div key={card.id} className={`hero-fan-card hero-fan-card-${index + 1}`}>
              <CardArt card={card} eager={index === 1} />
            </div>
          ))}
        </div>
      </section>

      <section className="home-section" aria-labelledby="featured-heading">
        <header className="home-section-head">
          <div>
            <h2 id="featured-heading">Cards collectors are looking for</h2>
            <p className="home-section-sub">
              A taste of what a published binder looks like. Prices below are samples, not market
              valuations.
            </p>
          </div>
          <Link className="home-section-link" to="/marketplace">
            See real listings
          </Link>
        </header>

        <ul className="featured-grid">
          {FEATURED_CARDS.map((card) => (
            <li key={card.id} className="featured-card">
              <div className="featured-card-art">
                <CardArt card={card} />
              </div>
              <div className="featured-card-body">
                <p className="featured-card-name">{card.name}</p>
                <p className="featured-card-set">{card.set}</p>
                <p className="featured-card-condition">{card.condition}</p>
                <p className="featured-card-price">
                  {card.price}
                  <span className="featured-card-tag">Sample listing</span>
                </p>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section className="home-section" aria-labelledby="how-heading">
        <h2 id="how-heading">How it works</h2>
        <ol className="steps">
          {STEPS.map((step, index) => (
            <li key={step.title} className="step">
              <span className="step-number" aria-hidden="true">
                {index + 1}
              </span>
              <h3>{step.title}</h3>
              <p>{step.body}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="home-closing" aria-labelledby="closing-heading">
        <div>
          <h2 id="closing-heading">
            {authenticated ? 'Your binder is waiting.' : 'Ready to open your binder?'}
          </h2>
          <p>
            {authenticated
              ? 'Pick up where you left off, or import another spreadsheet.'
              : 'Create an account, import your list, and start listing cards today.'}
          </p>
        </div>
        <Link className="cta" to={authenticated ? '/binder' : '/register'}>
          {authenticated ? 'Open your binder' : 'Create your free account'}
        </Link>
      </section>
    </div>
  )
}
