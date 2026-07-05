import type { Currency } from './types'

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
