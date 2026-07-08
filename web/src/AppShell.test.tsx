// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AppShell } from './AppShell'
import { AuthContext, type AuthContextValue, type AuthStatus } from './features/auth/authContext'
import type { User } from './features/auth/types'

function makeUser(overrides: Partial<User> = {}): User {
  return {
    id: 'u1',
    email: 'user@example.com',
    displayName: 'Ash Ketchum',
    city: 'Moncton',
    country: 'Canada',
    contactEmail: null,
    discordHandle: null,
    instagramHandle: null,
    roles: [],
    ...overrides,
  }
}

function renderShell(status: AuthStatus, user: User | null) {
  const auth: AuthContextValue = {
    status,
    user,
    login: async () => {},
    register: async () => {},
    updateProfile: async () => {},
    logout: () => {},
  }
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={['/marketplace']}>
        <Routes>
          <Route element={<AppShell />}>
            <Route path="/marketplace" element={<p>market</p>} />
            <Route path="/binder" element={<p>binder</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

afterEach(() => {
  cleanup()
})

describe('AppShell mobile navigation', () => {
  it('renders the toggle collapsed with an accessible name', () => {
    renderShell('anonymous', null)
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(toggle.getAttribute('aria-controls')).toBe('primary-nav')
  })

  it('toggles the drawer open and closed, updating aria-expanded and the label', async () => {
    const user = userEvent.setup()
    renderShell('anonymous', null)
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })
    const nav = document.getElementById('primary-nav')!

    expect(nav.classList.contains('open')).toBe(false)

    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(toggle.getAttribute('aria-label')).toMatch(/close navigation menu/i)
    expect(nav.classList.contains('open')).toBe(true)

    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(nav.classList.contains('open')).toBe(false)
  })

  it('closes on Escape and returns focus to the toggle', async () => {
    const user = userEvent.setup()
    renderShell('anonymous', null)
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })

    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')

    await user.keyboard('{Escape}')
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(toggle)
  })

  it('collapses the drawer after navigating via a link', async () => {
    const user = userEvent.setup()
    renderShell('authenticated', makeUser())
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })
    const nav = document.getElementById('primary-nav')!

    await user.click(toggle)
    await user.click(within(nav).getByRole('link', { name: 'Binder' }))

    expect(nav.classList.contains('open')).toBe(false)
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
  })

  it('shows sign-in / register for anonymous and hides authed links', () => {
    renderShell('anonymous', null)
    expect(screen.getByRole('link', { name: 'Sign in' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Register' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Binder' })).toBeNull()
  })

  it('shows the Admin link only for admin users', () => {
    renderShell('authenticated', makeUser({ roles: ['Admin'], displayName: 'Prof. Oak' }))
    expect(screen.getByRole('link', { name: 'Admin' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Prof. Oak' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy()
  })

  it('renders a long display name without crashing the nav', () => {
    const longName = 'A'.repeat(120)
    renderShell('authenticated', makeUser({ displayName: longName }))
    expect(screen.getByRole('link', { name: longName })).toBeTruthy()
  })
})
