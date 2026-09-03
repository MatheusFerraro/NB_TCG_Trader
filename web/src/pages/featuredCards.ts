/**
 * Static showcase data for the homepage.
 *
 * Deliberately a hardcoded array and not a marketplace query: the landing page
 * must paint without waiting on the API, and an empty database would otherwise
 * render an empty shop window. The prices are illustrative — every surface that
 * shows them also renders a "Sample listing" marker so they are never mistaken
 * for live market valuations.
 *
 * Images are the pokemontcg.io CDN's standard-resolution files (~40KB each),
 * not the *_hires.png variants, and the URLs are stable per card id.
 */
export interface FeaturedCard {
  id: string
  name: string
  set: string
  condition: string
  /** Pre-formatted for display; these are demo figures, not Intl-formatted money. */
  price: string
  imageUrl: string
}

export const FEATURED_CARDS: readonly FeaturedCard[] = [
  {
    id: 'base1-4',
    name: 'Charizard',
    set: 'Base Set',
    condition: 'Lightly Played',
    price: '$420.00 CAD',
    imageUrl: 'https://images.pokemontcg.io/base1/4.png',
  },
  {
    id: 'base1-2',
    name: 'Blastoise',
    set: 'Base Set',
    condition: 'Near Mint',
    price: '$180.00 CAD',
    imageUrl: 'https://images.pokemontcg.io/base1/2.png',
  },
  {
    id: 'base1-15',
    name: 'Venusaur',
    set: 'Base Set',
    condition: 'Near Mint',
    price: '$160.00 CAD',
    imageUrl: 'https://images.pokemontcg.io/base1/15.png',
  },
  {
    id: 'base1-1',
    name: 'Alakazam',
    set: 'Base Set',
    condition: 'Moderately Played',
    price: '$45.00 CAD',
    imageUrl: 'https://images.pokemontcg.io/base1/1.png',
  },
]

/**
 * The hero fan reuses the first three entries so the browser fetches four card
 * images for the whole page rather than seven.
 */
export const HERO_CARDS = FEATURED_CARDS.slice(0, 3)
