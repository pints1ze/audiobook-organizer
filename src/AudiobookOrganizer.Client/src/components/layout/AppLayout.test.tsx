import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../../App'
import { getDashboard, getEntries, getGroups, getHealth, getSettings, getSuggestions } from '../../services/api'

vi.mock('../../services/api', () => ({
  getHealth: vi.fn(),
  getSettings: vi.fn(),
  saveSettings: vi.fn(),
  getDashboard: vi.fn(),
  getLatestScan: vi.fn(),
  startScan: vi.fn(),
  getGroups: vi.fn(),
  getEntries: vi.fn(),
  createEntry: vi.fn(),
  updateEntry: vi.fn(),
  getSuggestions: vi.fn(),
  mergeEntry: vi.fn(),
}))

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
    vi.mocked(getSettings).mockResolvedValue({
      incomingPath: null,
      libraryPath: null,
      directoryTemplate: '%album%',
      fileTemplate: '%title%',
      keepSourceFiles: false,
    })
    vi.mocked(getDashboard).mockResolvedValue({
      incomingPath: null,
      libraryPath: null,
      lastScan: null,
      audioFileCount: 0,
      filesWithErrors: 0,
      bookCount: 0,
      booksByConfidence: { low: 0, medium: 0, high: 0 },
      correctedBookCount: 0,
      unresolvedFileCount: 0,
      excludedFileCount: 0,
    })
    vi.mocked(getGroups).mockResolvedValue({ groups: [], unresolved: [], excluded: [] })
    vi.mocked(getEntries).mockResolvedValue([])
    vi.mocked(getSuggestions).mockResolvedValue([])
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

  it('marks the Incoming nav link as the current page on /incoming', async () => {
    renderAt('/incoming')

    const incoming = screen.getByRole('link', { name: 'Incoming' })
    expect(incoming).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('link', { name: 'Settings' })).not.toHaveAttribute('aria-current')
    const navLinks = screen.getAllByRole('link').filter((l) => l.closest('nav'))
    expect(navLinks.map((l) => l.textContent)).toEqual(['Dashboard', 'Incoming', 'People', 'Genres', 'Settings'])
    expect(await screen.findByRole('heading', { level: 1, name: 'Incoming' })).toBeInTheDocument()
  })

  it('marks the People nav link as the current page on /people', async () => {
    renderAt('/people')

    expect(screen.getByRole('link', { name: 'People' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Genres' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('heading', { level: 1, name: 'People' })).toBeInTheDocument()
    expect(await screen.findByText('No people yet.')).toBeInTheDocument()
  })

  it('marks the Genres nav link as the current page on /genres', async () => {
    renderAt('/genres')

    expect(screen.getByRole('link', { name: 'Genres' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('heading', { level: 1, name: 'Genres' })).toBeInTheDocument()
    expect(await screen.findByText('No genres yet.')).toBeInTheDocument()
  })

  it('shows "Page not found" with a link back to the dashboard for an unknown route', () => {
    renderAt('/nope')

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the dashboard' })).toHaveAttribute('href', '/')
  })
})
