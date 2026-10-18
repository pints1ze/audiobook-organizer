import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../../App'
import { getHealth, getSettings } from '../../services/api'

vi.mock('../../services/api', () => ({ getHealth: vi.fn(), getSettings: vi.fn(), saveSettings: vi.fn() }))

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

describe('AppLayout', () => {
  beforeEach(() => {
    vi.mocked(getHealth).mockResolvedValue({ status: 'ok', database: 'ok' })
    vi.mocked(getSettings).mockResolvedValue({ incomingPath: null, libraryPath: null, keepSourceFiles: false })
  })

  it('renders the skip link first, the header and the main landmark', async () => {
    renderAt('/')

    const skip = screen.getByRole('link', { name: 'Skip to main content' })
    expect(skip).toHaveAttribute('href', '#main')
    expect(document.querySelector('a, button, input')).toBe(skip)
    expect(screen.getByRole('link', { name: 'Audiobook Organizer' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument()
  })

  it('marks the Settings nav link as the current page on /settings', async () => {
    renderAt('/settings')

    const nav = screen.getByRole('navigation', { name: 'Main' })
    expect(nav).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Settings' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument()
  })

  it('shows "Page not found" with a link back to the dashboard for an unknown route', () => {
    renderAt('/nope')

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the dashboard' })).toHaveAttribute('href', '/')
  })
})
