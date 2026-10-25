import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../../App'
import {
  getDashboard,
  getEntries,
  getGroups,
  getHealth,
  getJobs,
  getLatestScan,
  getPreview,
  getReview,
  getSettings,
  getSuggestions,
} from '../../services/api'

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
  getPreview: vi.fn(),
  getJobs: vi.fn(),
  approveBook: vi.fn(),
  getReview: vi.fn(),
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
    vi.mocked(getPreview).mockResolvedValue({
      libraryPath: null,
      books: [],
      counts: { files: 0, missing: 0, errors: 0, duplicates: 0, existing: 0 },
    })
    vi.mocked(getJobs).mockResolvedValue([])
    vi.mocked(getLatestScan).mockResolvedValue(null)
    vi.mocked(getReview).mockResolvedValue({
      books: [],
      counts: { needsMatch: 0, needsReview: 0, ready: 0, queued: 0, failed: 0 },
    })
  })

  it('renders the skip link first, the header and the main landmark', async () => {
    renderAt('/')

    const skip = screen.getByRole('link', { name: 'Skip to main content' })
    expect(skip).toHaveAttribute('href', '#main')
    expect(document.querySelector('a, button, input')).toBe(skip)
    expect(screen.getByRole('link', { name: 'Audiobook Organizer' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
    expect(screen.getByRole('heading', { level: 1, name: 'Review' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Review' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByText('No books to review yet.', { exact: false })).toBeInTheDocument()
  })

  it('shows the Dashboard on /dashboard with More marked current', async () => {
    renderAt('/dashboard')

    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('link', { name: 'Review' })).not.toHaveAttribute('aria-current')
    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument()
  })

  it('lists only Review and Jobs in the Main nav, with the rest under More', async () => {
    renderAt('/incoming')

    const nav = screen.getByRole('navigation', { name: 'Main' })
    const navLinks = screen.getAllByRole('link').filter((l) => nav.contains(l))
    expect(navLinks.map((l) => l.textContent)).toEqual(['Review', 'Jobs'])
    expect(screen.getByRole('link', { name: 'Review' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/jobs')
    for (const name of ['Incoming', 'Preview', 'Dashboard', 'People', 'Genres', 'Settings']) {
      expect(screen.queryByRole('link', { name })).not.toBeInTheDocument()
    }
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-haspopup', 'menu')
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('link', { name: 'Review' })).not.toHaveAttribute('aria-current')
    expect(await screen.findByRole('heading', { level: 1, name: 'Incoming' })).toBeInTheDocument()
  })

  it('lists Incoming, Preview, Dashboard, People, Genres and Settings under More, as links to their routes', async () => {
    renderAt('/')

    const more = screen.getByRole('button', { name: 'More' })
    more.focus()
    fireEvent.keyDown(more, { key: 'Enter' })

    await screen.findByRole('menu')
    const items = screen.getAllByRole('menuitem')
    expect(items.map((i) => [i.textContent, i.getAttribute('href')])).toEqual([
      ['Incoming', '/incoming'],
      ['Preview', '/preview'],
      ['Dashboard', '/dashboard'],
      ['People', '/people'],
      ['Genres', '/genres'],
      ['Settings', '/settings'],
    ])
    expect(items.every((i) => i.tagName === 'A')).toBe(true)

    fireEvent.click(screen.getByRole('menuitem', { name: 'Incoming' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Incoming' })).toBeInTheDocument()
  })

  it('opens More with Enter, and choosing Settings goes to the Settings page', async () => {
    renderAt('/')

    const more = screen.getByRole('button', { name: 'More' })
    expect(more).toHaveAttribute('aria-expanded', 'false')
    more.focus()
    fireEvent.keyDown(more, { key: 'Enter' })

    const menu = await screen.findByRole('menu')
    expect(more).toHaveAttribute('aria-expanded', 'true')
    const items = screen.getAllByRole('menuitem')
    expect(menu).toContainElement(items[items.length - 1])

    fireEvent.click(screen.getByRole('menuitem', { name: 'Settings' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'true')
  })

  it('closes the More menu with Escape and returns focus to More', async () => {
    renderAt('/')

    const more = screen.getByRole('button', { name: 'More' })
    more.focus()
    fireEvent.keyDown(more, { key: 'Enter' })
    const menu = await screen.findByRole('menu')

    fireEvent.keyDown(menu, { key: 'Escape' })

    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument())
    expect(more).toHaveAttribute('aria-expanded', 'false')
    expect(more).toHaveFocus()
  })

  it('marks More as current on /people, and People inside its menu', async () => {
    renderAt('/people')

    const more = screen.getByRole('button', { name: 'More' })
    expect(more).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('link', { name: 'Jobs' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('heading', { level: 1, name: 'People' })).toBeInTheDocument()
    expect(await screen.findByText('No people yet.')).toBeInTheDocument()

    fireEvent.keyDown(more, { key: 'Enter' })
    expect(await screen.findByRole('menuitem', { name: 'People' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('menuitem', { name: 'Genres' })).not.toHaveAttribute('aria-current')
  })

  it('shows the Genres page on /genres with More marked current', async () => {
    renderAt('/genres')

    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('heading', { level: 1, name: 'Genres' })).toBeInTheDocument()
    expect(await screen.findByText('No genres yet.')).toBeInTheDocument()
  })

  it('shows the Preview page on /preview with More marked current, and Preview inside its menu', async () => {
    renderAt('/preview')

    const more = screen.getByRole('button', { name: 'More' })
    expect(more).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('heading', { level: 1, name: 'Preview' })).toBeInTheDocument()
    expect(await screen.findByText('No books to preview yet.')).toBeInTheDocument()

    fireEvent.keyDown(more, { key: 'Enter' })
    expect(await screen.findByRole('menuitem', { name: 'Preview' })).toHaveAttribute('aria-current', 'page')
  })

  it('marks the Jobs nav link as the current page on /jobs', async () => {
    renderAt('/jobs')

    const link = screen.getByRole('link', { name: 'Jobs' })
    expect(link).toHaveAttribute('href', '/jobs')
    expect(link).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'More' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('heading', { level: 1, name: 'Jobs' })).toBeInTheDocument()
    expect(await screen.findByText(/No jobs yet\./)).toBeInTheDocument()
  })

  it('shows "Page not found" with a link to the dashboard for an unknown route', () => {
    renderAt('/nope')

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the dashboard' })).toHaveAttribute('href', '/dashboard')
  })
})
