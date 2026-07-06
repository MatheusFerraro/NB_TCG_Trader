import type { CardCondition, Currency } from './types'

const LOCALE_BY_CURRENCY: Record<Currency, string> = {
  CAD: 'en-CA',
  BRL: 'pt-BR',
}

export function formatPrice(price: number, currency: Currency): string {
  return new Intl.NumberFormat(LOCALE_BY_CURRENCY[currency], {
    style: 'currency',
    currency,
  }).format(price)
}

/** Plain-English names for the TCG condition grades, for buyer-facing surfaces. */
export const CONDITION_LABELS: Record<CardCondition, string> = {
  NM: 'Near Mint',
  LP: 'Lightly Played',
  MP: 'Moderately Played',
  HP: 'Heavily Played',
  DMG: 'Damaged',
}
