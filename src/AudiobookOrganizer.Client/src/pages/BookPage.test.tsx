import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../App'
import { getGroup, searchMetadata } from '../services/api'
import type { GroupDetail } from '../types/groups'
import type { MetadataCandidate } from '../types/metadata'

vi.mock('../services/api', () => ({
  getGroup: vi.fn(),
  searchMetadata: vi.fn(),
}))

const getGroupMock = vi.mocked(getGroup)
const searchMock = vi.mocked(searchMetadata)

const group: GroupDetail = {
  id: 4,
  title: 'Project Hail Mary',
  author: 'Andy Weir',
  confidence: 'High',
  evidence: ['Shared album tag'],
  fileCount: 2,
  totalDurationMs: 3_723_000,
  userCorrected: false,
  files: [],
}

const audible: MetadataCandidate = {
  provider: 'audible',
  asin: 'B08G9PRS1K',
  title: 'Project Hail Mary',
  subtitle: 'A Novel',
  authors: ['Andy Weir'],
  narrators: ['Ray Porter'],
  series: { name: 'Hail Mary', part: '1' },
  publisher: 'Audible Studios',
  releaseDate: '2021-05-04',
  language: 'english',
  runtimeMinutes: 970,
  format: 'unabridged',
  coverUrl: 'https://example.test/phm.jpg',
  isbn: null,
}

const openLibrary: MetadataCandidate = {
  provider: 'openlibrary',
  asin: null,
  title: 'Project Hail Mary',
  subtitle: null,
  authors: ['Andy Weir'],
  narrators: [],
  series: null,
  publisher: null,
  releaseDate: '2021',
  language: null,
  runtimeMinutes: null,
  format: null,
  coverUrl: null,
  isbn: '9780593135204',
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

async function loadedPage() {
  getGroupMock.mockResolvedValue(group)
  renderAt('/books/4')
  await screen.findByRole('heading', { level: 1, name: 'Project Hail Mary' })
}

describe('BookPage', () => {
  beforeEach(() => {
    getGroupMock.mockReset()
    searchMock.mockReset()
  })

  it('loads the group, prefills the keywords, searches and lists a candidate', async () => {
    searchMock.mockResolvedValue({ ok: true, result: { candidates: [audible], providerErrors: [] } })
    await loadedPage()

    expect(getGroupMock).toHaveBeenCalledWith(4)
    expect(screen.getByText('Andy Weir', { selector: 'dd' })).toBeInTheDocument()
    expect(screen.getByText('2', { selector: 'dd' })).toBeInTheDocument()
    expect(screen.getByText('1:02:03', { selector: 'dd' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Catalog search' })).toBeInTheDocument()

    const input = screen.getByLabelText('Keywords')
    expect(input).toHaveValue('Project Hail Mary Andy Weir')
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    expect(searchMock).toHaveBeenCalledWith('Project Hail Mary Andy Weir')
    const item = (await screen.findByRole('heading', { level: 3, name: /Project Hail Mary/ })).closest('li')!
    expect(within(item).getByText('A Novel')).toBeInTheDocument()
    expect(within(item).getByText('Ray Porter')).toBeInTheDocument()
    expect(within(item).getByText('Hail Mary, book 1')).toBeInTheDocument()
    expect(within(item).getByText('Audible')).toBeInTheDocument()
    expect(within(item).getByText('B08G9PRS1K')).toBeInTheDocument()
    expect(within(item).getByText('16:10:00')).toBeInTheDocument()
    expect(within(item).getByText('unabridged')).toBeInTheDocument()
    expect(within(item).getByText('2021-05-04')).toBeInTheDocument()
    expect(within(item).getByRole('presentation')).toHaveAttribute('alt', '')
    expect(screen.getByText('1 match found.')).toHaveAttribute('role', 'status')
  })

  it('skips a blank author when prefilling', async () => {
    getGroupMock.mockResolvedValue({ ...group, author: null })
    renderAt('/books/4')

    expect(await screen.findByLabelText('Keywords')).toHaveValue('Project Hail Mary')
  })

  it('shows the provider-error notice beside Open Library candidates, and "No matches found." for none', async () => {
    searchMock.mockResolvedValueOnce({
      ok: true,
      result: { candidates: [openLibrary], providerErrors: ['Audible search failed: timeout'] },
    })
    await loadedPage()
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    const notice = await screen.findByText(/Audible search failed: timeout/)
    expect(notice.closest('[role="status"]')).not.toBeNull()
    const item = screen.getByRole('heading', { level: 3, name: 'Project Hail Mary' }).closest('li')!
    expect(within(item).getByText('Open Library')).toBeInTheDocument()
    expect(within(item).getByText('9780593135204')).toBeInTheDocument()

    searchMock.mockResolvedValueOnce({ ok: true, result: { candidates: [], providerErrors: [] } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    expect(await screen.findByText('No matches found.')).toBeInTheDocument()
    expect(screen.queryByText(/Audible search failed/)).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 3 })).not.toBeInTheDocument()
  })

  it('shows a 400 as the keywords field error', async () => {
    searchMock.mockResolvedValue({ ok: false, errors: { keywords: ['Enter a title, author, ASIN or ISBN.'] } })
    await loadedPage()
    fireEvent.change(screen.getByLabelText('Keywords'), { target: { value: ' ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    const error = await screen.findByText('Enter a title, author, ASIN or ISBN.')
    const input = screen.getByLabelText('Keywords')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input.getAttribute('aria-describedby')).toContain(error.closest('[id]')!.id)
  })

  it('shows a 502 detail and lets the user search again', async () => {
    searchMock.mockResolvedValueOnce({ ok: false, providerFailure: 'Audible search failed. Open Library search failed.' })
    await loadedPage()
    const button = screen.getByRole('button', { name: 'Search' })
    fireEvent.click(button)
    expect(button).toBeDisabled()

    expect(await screen.findByText('Audible search failed. Open Library search failed.')).toBeInTheDocument()
    expect(button).toBeEnabled()

    searchMock.mockResolvedValueOnce({ ok: true, result: { candidates: [audible], providerErrors: [] } })
    fireEvent.click(button)
    expect(await screen.findByRole('heading', { level: 3, name: /Project Hail Mary/ })).toBeInTheDocument()
    expect(screen.queryByText('Audible search failed. Open Library search failed.')).not.toBeInTheDocument()
  })

  it('shows an error when the search request itself fails', async () => {
    searchMock.mockRejectedValue(new TypeError('Failed to fetch'))
    await loadedPage()
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("The search couldn't be run.")
    await waitFor(() => expect(screen.getByRole('button', { name: 'Search' })).toBeEnabled())
  })

  it('shows "Book not found" with a link to Incoming for an unknown id', async () => {
    getGroupMock.mockRejectedValue(new Error('Loading the group failed with status 404'))
    renderAt('/books/99')

    expect(await screen.findByRole('heading', { level: 1, name: 'Book not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /back to incoming/i })).toHaveAttribute('href', '/incoming')
  })

  it('treats a non-numeric id as not found without calling the API', async () => {
    renderAt('/books/abc')

    expect(await screen.findByRole('heading', { level: 1, name: 'Book not found' })).toBeInTheDocument()
    expect(getGroupMock).not.toHaveBeenCalled()
  })
})
