// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { FEATURED_CARDS } from './featuredCards'
import { AuthContext, type AuthContextValue, type AuthStatus } from '../features/auth/authContext'
import type { User } from '../features/auth/types'

function makeUser(): User {
  return {
    id: 'u1',
    email: 'user@example.com',
    displayName: 'Ash Ketchum',
    city: null,
    country: null,
    contactEmail: null,
    discordHandle: null,
    instagramHandle: null,
    roles: [],
  }
}

function renderHome(status: AuthStatus) {
  const auth: AuthContextValue = {
    status,
    user: status === 'authenticated' ? makeUser() : null,
    login: async () => {},
    register: async () => {},
    updateProfile: async () => {},
    logout: () => {},
  }
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

const fetchSpy = vi.fn()

beforeEach(() => {
  fetchSpy.mockClear()
  vi.stubGlobal('fetch', fetchSpy)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('HomePage', () => {
  it('leads with the benefit headline and both calls to action', () => {
    renderHome('anonymous')
    expect(
      screen.getByRole('heading', { level: 1, name: /turn your spreadsheet into a card shop/i }),
    ).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Browse the marketplace' }).getAttribute('href')).toBe(
      '/marketplace',
    )
  })

  it('points an anonymous visitor at registration', () => {
    renderHome('anonymous')
    // Hero and closing panel both invite registration when signed out, so scope
    // to the hero rather than asserting a single match.
    const hero = screen.getByRole('heading', { level: 1 }).closest('section')!
    expect(
      within(hero).getByRole('link', { name: 'Create your free account' }).getAttribute('href'),
    ).toBe('/register')
    expect(screen.queryByRole('link', { name: 'Upload your cards' })).toBeNull()
  })

  it('points a signed-in collector at the importer and their binder', () => {
    renderHome('authenticated')
    expect(screen.getByRole('link', { name: 'Upload your cards' }).getAttribute('href')).toBe(
      '/binder/import',
    )
    // Closing panel swaps to the binder once there is an account behind it.
    expect(screen.getByRole('link', { name: 'Open your binder' }).getAttribute('href')).toBe(
      '/binder',
    )
    expect(screen.queryByRole('link', { name: 'Create your free account' })).toBeNull()
  })

  it('renders every fixed featured card with a sample-price disclaimer', () => {
    renderHome('anonymous')
    const featured = screen.getByRole('heading', { name: /cards collectors are looking for/i })
      .closest('section')!

    for (const card of FEATURED_CARDS) {
      const name = within(featured).getByText(card.name)
      const entry = name.closest('li')!
      expect(within(entry).getByText(card.set)).toBeTruthy()
      expect(within(entry).getByText(card.condition)).toBeTruthy()
      expect(within(entry).getByText(card.price)).toBeTruthy()
      // Never let a demo figure read as a live valuation.
      expect(within(entry).getByText('Sample listing')).toBeTruthy()
    }
  })

  it('never fetches: the landing page must paint without the marketplace API', () => {
    renderHome('anonymous')
    expect(fetchSpy).not.toHaveBeenCalled()
    // No card links to listing ids that do not exist — one section link instead.
    expect(screen.getByRole('link', { name: 'See real listings' }).getAttribute('href')).toBe(
      '/marketplace',
    )
  })

  it('sizes card art up front and defers everything but the front hero card', () => {
    renderHome('anonymous')
    const images = screen.getAllByRole('img')
    const eager = images.filter((img) => img.getAttribute('loading') === 'eager')

    expect(eager.length).toBe(1)
    for (const img of images) {
      expect(img.getAttribute('width')).toBe('245')
      expect(img.getAttribute('height')).toBe('342')
      expect(img.getAttribute('decoding')).toBe('async')
    }
  })

  it('falls back to a named placeholder when card art fails to load', () => {
    renderHome('anonymous')
    const before = screen.getAllByRole('img').length
    const charizard = screen.getAllByRole('img', { name: /Charizard/ })

    for (const img of charizard) fireEvent.error(img)

    expect(screen.getAllByRole('img').length).toBe(before - charizard.length)
    expect(screen.getAllByText('Charizard').length).toBeGreaterThan(0)
  })

  it('spells out the three-step flow', () => {
    renderHome('anonymous')
    expect(screen.getByRole('heading', { name: 'Upload your list' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Review your binder' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'List cards for sale' })).toBeTruthy()
  })
})
