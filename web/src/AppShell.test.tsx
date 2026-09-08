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
    emailConfirmed: true,
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

function renderShell(status: AuthStatus, user: User | null, path = '/marketplace') {
  const auth: AuthContextValue = {
    status,
    user,
    login: async () => {},
    register: async () => {},
    updateProfile: async () => {},
    refreshUser: async () => {},
    logout: () => {},
  }
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<p>home</p>} />
            <Route path="/marketplace" element={<p>market</p>} />
            <Route path="/binder" element={<p>binder</p>} />
            <Route path="/binder/import" element={<p>import</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

/** The sidebar is the element the toggle controls; scope queries to it. */
function sidebar() {
  return document.getElementById('primary-nav')!
}

afterEach(() => {
  cleanup()
  document.body.style.overflow = ''
})

describe('AppShell sidebar navigation', () => {
  it('groups the anonymous sidebar and hides authenticated destinations', () => {
    renderShell('anonymous', null)
    const nav = within(sidebar()).getByRole('navigation', { name: 'Main' })

    expect(within(nav).getByRole('link', { name: 'Home' })).toBeTruthy()
    expect(within(nav).getByRole('link', { name: 'Marketplace' })).toBeTruthy()
    expect(within(nav).getByRole('link', { name: 'Sign in' })).toBeTruthy()
    expect(within(nav).getByRole('link', { name: 'Register' })).toBeTruthy()

    expect(within(nav).queryByRole('link', { name: 'My Binder' })).toBeNull()
    expect(within(nav).queryByRole('link', { name: 'Import Cards' })).toBeNull()
    expect(within(nav).queryByRole('link', { name: 'Profile' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Sign out' })).toBeNull()
  })

  it('shows the collection group and account actions when authenticated', () => {
    renderShell('authenticated', makeUser())
    const rail = within(sidebar())

    expect(rail.getByRole('link', { name: 'My Binder' })).toBeTruthy()
    expect(rail.getByRole('link', { name: 'Import Cards' })).toBeTruthy()
    expect(rail.getByRole('link', { name: 'Profile' })).toBeTruthy()
    // The signed-in name sits at the bottom and doubles as the profile door.
    expect(rail.getByRole('link', { name: /Ash Ketchum/ })).toBeTruthy()
    expect(rail.getByRole('button', { name: 'Sign out' })).toBeTruthy()

    expect(rail.queryByRole('link', { name: 'Sign in' })).toBeNull()
    expect(rail.queryByRole('link', { name: 'Register' })).toBeNull()
  })

  it('renders the three navigation groups only when they have destinations', () => {
    renderShell('anonymous', null)
    expect(screen.getByRole('heading', { name: 'Main' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Account' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Collection' })).toBeNull()

    cleanup()
    renderShell('authenticated', makeUser())
    expect(screen.getByRole('heading', { name: 'Collection' })).toBeTruthy()
  })

  it('shows the Admin link only for admin users', () => {
    renderShell('authenticated', makeUser({ roles: ['Admin'], displayName: 'Prof. Oak' }))
    expect(within(sidebar()).getByRole('link', { name: 'Admin' })).toBeTruthy()

    cleanup()
    renderShell('authenticated', makeUser())
    expect(within(sidebar()).queryByRole('link', { name: 'Admin' })).toBeNull()
  })

  it('marks the current route without relying on colour alone', () => {
    renderShell('authenticated', makeUser(), '/binder/import')
    const rail = within(sidebar())

    const importLink = rail.getByRole('link', { name: 'Import Cards' })
    expect(importLink.getAttribute('aria-current')).toBe('page')
    expect(importLink.classList.contains('active')).toBe(true)

    // `end` on /binder keeps the parent from lighting up for its child route.
    const binder = rail.getByRole('link', { name: 'My Binder' })
    expect(binder.getAttribute('aria-current')).toBeNull()
    expect(binder.classList.contains('active')).toBe(false)
  })

  it('does not mark Home active on a deeper route', () => {
    renderShell('anonymous', null, '/marketplace')
    const rail = within(sidebar())
    expect(rail.getByRole('link', { name: 'Marketplace' }).getAttribute('aria-current')).toBe('page')
    expect(rail.getByRole('link', { name: 'Home' }).getAttribute('aria-current')).toBeNull()
  })

  it('renders a long display name without crashing the rail', () => {
    const longName = 'A'.repeat(120)
    renderShell('authenticated', makeUser({ displayName: longName }))
    expect(within(sidebar()).getByRole('link', { name: longName })).toBeTruthy()
  })
})

describe('AppShell mobile drawer', () => {
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

    expect(sidebar().classList.contains('open')).toBe(false)

    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(toggle.getAttribute('aria-label')).toMatch(/close navigation menu/i)
    expect(sidebar().classList.contains('open')).toBe(true)

    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(sidebar().classList.contains('open')).toBe(false)
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

  it('closes when the backdrop is tapped and restores focus', async () => {
    const user = userEvent.setup()
    const { container } = renderShell('anonymous', null)
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })

    await user.click(toggle)
    expect(container.querySelector('.app-shell')!.classList.contains('menu-open')).toBe(true)

    await user.click(container.querySelector('.nav-scrim')!)
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(sidebar().classList.contains('open')).toBe(false)
    expect(document.activeElement).toBe(toggle)
  })

  it('locks page scrolling while the drawer is open and restores it after', async () => {
    const user = userEvent.setup()
    renderShell('anonymous', null)
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })

    await user.click(toggle)
    expect(document.body.style.overflow).toBe('hidden')

    await user.keyboard('{Escape}')
    expect(document.body.style.overflow).toBe('')
  })

  it('collapses the drawer after navigating via a link', async () => {
    const user = userEvent.setup()
    renderShell('authenticated', makeUser())
    const toggle = screen.getByRole('button', { name: /open navigation menu/i })

    await user.click(toggle)
    await user.click(within(sidebar()).getByRole('link', { name: 'My Binder' }))

    expect(sidebar().classList.contains('open')).toBe(false)
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
  })
})
