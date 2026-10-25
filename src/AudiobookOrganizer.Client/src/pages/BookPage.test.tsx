import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../App'
import {
  getBookGenres,
  getBookPeople,
  getCovers,
  getGroup,
  getMatch,
  getMetadata,
  pickMatch,
  saveMetadata,
  searchMetadata,
} from '../services/api'
import type { GroupDetail } from '../types/groups'
import type { BookMatch, BookMetadata, MetadataCandidate, PickMatchResult } from '../types/metadata'
import { formatDateTime } from '../utils/formatDateTime'

vi.mock('../services/api', () => ({
  createEntry: vi.fn(),
  getBookGenres: vi.fn(),
  getBookPeople: vi.fn(),
  getCovers: vi.fn(),
  getGroup: vi.fn(),
  getMatch: vi.fn(),
  getMetadata: vi.fn(),
  pickMatch: vi.fn(),
  saveMetadata: vi.fn(),
  searchMetadata: vi.fn(),
  updateEntry: vi.fn(),
}))

const getGroupMock = vi.mocked(getGroup)
const searchMock = vi.mocked(searchMetadata)
const getMatchMock = vi.mocked(getMatch)
const pickMock = vi.mocked(pickMatch)
const getMetadataMock = vi.mocked(getMetadata)
const saveMetadataMock = vi.mocked(saveMetadata)
const getCoversMock = vi.mocked(getCovers)
const getBookPeopleMock = vi.mocked(getBookPeople)
const getBookGenresMock = vi.mocked(getBookGenres)

function metadataWith(title: string, origin: 'file' | 'provider', narrators: string[]): BookMetadata {
  const empty = { value: null, origin: null, provider: null }
  const provider = origin === 'provider' ? 'audible' : null
  return {
    groupId: 4,
    fields: {
      title: { value: title, origin, provider },
      subtitle: empty,
      authors: { value: ['Andy Weir'], origin, provider },
      narrators: narrators.length ? { value: narrators, origin, provider } : { value: [], origin: null, provider: null },
      genres: { value: [], origin: null, provider: null },
      series: empty,
      seriesPart: empty,
      publisher: empty,
      releaseDate: empty,
      language: empty,
      format: empty,
      asin: empty,
      isbn: empty,
    },
  }
}

function metadataSection() {
  return screen.getByRole('heading', { level: 2, name: 'Metadata' }).closest('section')!
}

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

const storedMatch: BookMatch = { groupId: 4, match: openLibrary, pickedAtUtc: '2026-10-18T09:30:00Z' }

function chosenSection() {
  return screen.getByRole('heading', { level: 2, name: 'Chosen match' }).closest('section')!
}

function resultItems() {
  const section = screen.getByRole('heading', { level: 2, name: 'Catalog search' }).closest('section')!
  return within(section)
    .getAllByRole('heading', { level: 3 })
    .map((heading) => heading.closest('li')!)
}

async function searchFor(candidates: MetadataCandidate[]) {
  searchMock.mockResolvedValue({ ok: true, result: { candidates, providerErrors: [] } })
  fireEvent.click(screen.getByRole('button', { name: 'Search' }))
  await screen.findByText(`${candidates.length} matches found.`)
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
    getMatchMock.mockReset()
    getMatchMock.mockResolvedValue(null)
    pickMock.mockReset()
    getMetadataMock.mockReset()
    getMetadataMock.mockResolvedValue(metadataWith('Project Hail Mary', 'file', []))
    saveMetadataMock.mockReset()
    getCoversMock.mockReset()
    getCoversMock.mockResolvedValue({ groupId: 4, chosen: null, embedded: [], provider: null })
    getBookPeopleMock.mockReset()
    getBookPeopleMock.mockResolvedValue({ groupId: 4, authors: [], narrators: [] })
    getBookGenresMock.mockReset()
    getBookGenresMock.mockResolvedValue({ groupId: 4, genres: [] })
  })

  it('shows the Cover, Metadata, People and Genres sections between Chosen match and Catalog search', async () => {
    await loadedPage()

    const headings = screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)
    expect(headings).toEqual(['Chosen match', 'Cover', 'Metadata', 'People', 'Genres', 'Catalog search'])
    // The sections load after the page heading renders, so their calls can land a tick later.
    await waitFor(() => {
      expect(getBookPeopleMock).toHaveBeenCalledWith(4)
      expect(getBookGenresMock).toHaveBeenCalledWith(4)
      expect(getCoversMock).toHaveBeenCalledWith(4)
    })
    const cover = screen.getByRole('heading', { level: 2, name: 'Cover' }).closest('section')!
    expect(await within(cover).findByText('No cover chosen yet.')).toBeInTheDocument()
    expect(await within(metadataSection()).findAllByText('From files')).toHaveLength(2)
  })

  it('reloads the metadata after "Accept match" succeeds', async () => {
    pickMock.mockResolvedValue({ ok: true, match: { groupId: 4, match: audible, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    await loadedPage()
    await within(metadataSection()).findAllByText('From files')
    await searchFor([openLibrary, audible])

    getMetadataMock.mockResolvedValue(metadataWith('Project Hail Mary', 'provider', ['Ray Porter']))
    fireEvent.click(screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' }))

    expect(await within(metadataSection()).findByText('Ray Porter')).toBeInTheDocument()
    expect(within(metadataSection()).getAllByText('From Audible').length).toBeGreaterThan(0)
    expect(getMetadataMock).toHaveBeenCalledTimes(2)
  })

  it('reloads the people section after "Accept match" succeeds', async () => {
    pickMock.mockResolvedValue({ ok: true, match: { groupId: 4, match: audible, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    await loadedPage()
    const people = screen.getByRole('heading', { level: 2, name: 'People' }).closest('section')!
    await within(people).findByText('No authors or narrators yet.')
    await searchFor([openLibrary, audible])

    getBookPeopleMock.mockResolvedValue({
      groupId: 4,
      authors: [],
      narrators: [{ name: 'Ray Porter', entry: { id: 3, name: 'Ray Porter', aliases: [] }, suggestions: [] }],
    })
    fireEvent.click(screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' }))

    expect(await within(people).findByText('Normalized: Ray Porter')).toBeInTheDocument()
    expect(getBookPeopleMock).toHaveBeenCalledTimes(2)
  })

  it('reloads the people section after the metadata editor saves', async () => {
    saveMetadataMock.mockResolvedValue({ ok: true, metadata: metadataWith('Project Hail Mary', 'file', ['Ray Porter']) })
    await loadedPage()
    const people = screen.getByRole('heading', { level: 2, name: 'People' }).closest('section')!
    await within(people).findByText('No authors or narrators yet.')

    fireEvent.click(await within(metadataSection()).findByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(within(metadataSection()).getByLabelText('Narrators'), { target: { value: 'Ray Porter' } })
    getBookPeopleMock.mockResolvedValue({
      groupId: 4,
      authors: [],
      narrators: [{ name: 'Ray Porter', entry: null, suggestions: [] }],
    })
    fireEvent.click(within(metadataSection()).getByRole('button', { name: 'Save' }))

    expect(await within(people).findByText('Not linked')).toBeInTheDocument()
    expect(getBookPeopleMock).toHaveBeenCalledTimes(2)
  })

  it('reloads the genres section after the metadata editor saves', async () => {
    saveMetadataMock.mockResolvedValue({ ok: true, metadata: metadataWith('Project Hail Mary', 'file', []) })
    await loadedPage()
    const genres = screen.getByRole('heading', { level: 2, name: 'Genres' }).closest('section')!
    await within(genres).findByText('No genres yet.')

    fireEvent.click(await within(metadataSection()).findByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(within(metadataSection()).getByLabelText('Genres'), { target: { value: 'Horror' } })
    getBookGenresMock.mockResolvedValue({ groupId: 4, genres: [{ name: 'Horror', entry: null, suggestions: [] }] })
    fireEvent.click(within(metadataSection()).getByRole('button', { name: 'Save' }))

    expect(await within(genres).findByText('Not linked')).toBeInTheDocument()
    expect(getBookGenresMock).toHaveBeenCalledTimes(2)
  })

  it('reloads the covers after "Accept match" succeeds, so the match cover follows it', async () => {
    pickMock.mockResolvedValue({ ok: true, match: { groupId: 4, match: audible, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    await loadedPage()
    const cover = screen.getByRole('heading', { level: 2, name: 'Cover' }).closest('section')!
    await within(cover).findByText('No cover chosen yet.')
    await searchFor([openLibrary, audible])

    getCoversMock.mockResolvedValue({
      groupId: 4,
      chosen: null,
      embedded: [],
      provider: { provider: 'audible', coverUrl: 'https://example.test/phm.jpg', isChosen: false },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' }))

    expect(await within(cover).findByRole('button', { name: "Use the match's cover: Audible" })).toBeInTheDocument()
    expect(getCoversMock).toHaveBeenCalledTimes(2)
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

  it('shows the stored match under "Chosen match" with its picked date, above the search', async () => {
    getMatchMock.mockResolvedValue(storedMatch)
    await loadedPage()

    expect(getMatchMock).toHaveBeenCalledWith(4)
    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual(['Chosen match', 'Cover', 'Metadata', 'People', 'Genres', 'Catalog search'])
    const section = chosenSection()
    expect(await within(section).findByRole('heading', { level: 3, name: 'Project Hail Mary' })).toBeInTheDocument()
    expect(within(section).getByText('Open Library')).toBeInTheDocument()
    expect(within(section).getByText('9780593135204')).toBeInTheDocument()
    expect(within(section).getByText(`Picked ${formatDateTime(storedMatch.pickedAtUtc)}`)).toBeInTheDocument()
  })

  it('says "No match chosen yet." when the book has none', async () => {
    await loadedPage()

    expect(await within(chosenSection()).findByText('No match chosen yet.')).toBeInTheDocument()
  })

  it('picks the second result, announces it, focuses the Chosen match heading and marks it current', async () => {
    let resolvePick!: (value: PickMatchResult) => void
    pickMock.mockReturnValue(new Promise((resolve) => (resolvePick = resolve)))
    await loadedPage()
    await within(chosenSection()).findByText('No match chosen yet.')
    await searchFor([openLibrary, audible])

    const first = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 1 of 2' })
    const second = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' })
    fireEvent.click(second)
    expect(pickMock).toHaveBeenCalledWith(4, audible)
    expect(first).toBeDisabled()
    expect(second).toBeDisabled()

    resolvePick({ ok: true, match: { groupId: 4, match: audible, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    expect(await screen.findByText('Match saved: Project Hail Mary')).toHaveAttribute('role', 'status')
    expect(screen.getByRole('heading', { level: 2, name: 'Chosen match' })).toHaveFocus()
    expect(within(chosenSection()).getByText('B08G9PRS1K')).toBeInTheDocument()

    const items = resultItems()
    expect(within(items[1]).getByText('Current match')).toBeInTheDocument()
    expect(within(items[1]).queryByRole('button')).not.toBeInTheDocument()
    expect(within(items[0]).getByRole('button', { name: /Accept match/ })).toBeEnabled()
  })

  it('replaces the stored match, which reads "Current match" until another is picked', async () => {
    getMatchMock.mockResolvedValue(storedMatch)
    pickMock.mockResolvedValue({ ok: true, match: { groupId: 4, match: audible, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    await loadedPage()
    await within(chosenSection()).findByText('9780593135204')
    await searchFor([openLibrary, audible])

    expect(within(resultItems()[0]).getByText('Current match')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' }))

    expect(await screen.findByText('Match saved: Project Hail Mary')).toBeInTheDocument()
    expect(within(chosenSection()).queryByText('9780593135204')).not.toBeInTheDocument()
    expect(within(chosenSection()).getByText('B08G9PRS1K')).toBeInTheDocument()
    const items = resultItems()
    expect(within(items[0]).getByRole('button', { name: /Accept match/ })).toBeEnabled()
    expect(within(items[1]).getByText('Current match')).toBeInTheDocument()
  })

  it('shows a 400 beside the result and keeps the previous match and the results', async () => {
    getMatchMock.mockResolvedValue(storedMatch)
    pickMock.mockResolvedValue({ ok: false, errors: { coverUrl: ['The cover URL must be an absolute http or https URL.'] } })
    await loadedPage()
    await within(chosenSection()).findByText('9780593135204')
    await searchFor([openLibrary, audible])

    const button = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' })
    fireEvent.click(button)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The cover URL must be an absolute http or https URL.')
    expect(button.closest('li')).toContainElement(alert)
    expect(within(chosenSection()).getByText('9780593135204')).toBeInTheDocument()
    expect(screen.getByText('2 matches found.')).toBeInTheDocument()
    expect(button).toBeEnabled()
  })

  it('shows "This book no longer exists" with a link to Incoming on a 404', async () => {
    pickMock.mockResolvedValue({ ok: false, notFound: true })
    await loadedPage()
    await searchFor([audible, openLibrary])
    fireEvent.click(screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 1 of 2' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('This book no longer exists')
    expect(within(alert).getByRole('link', { name: /incoming/i })).toHaveAttribute('href', '/incoming')
    expect(screen.getByText('2 matches found.')).toBeInTheDocument()
  })

  it('shows a retryable error when the pick request fails', async () => {
    pickMock.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await loadedPage()
    await searchFor([audible, openLibrary])
    const button = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 1 of 2' })
    fireEvent.click(button)

    expect(await screen.findByRole('alert')).toHaveTextContent("The match couldn't be saved.")
    await waitFor(() => expect(button).toBeEnabled())

    pickMock.mockResolvedValueOnce({ ok: true, match: { groupId: 4, match: audible, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    fireEvent.click(button)
    expect(await screen.findByText('Match saved: Project Hail Mary')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('focuses and highlights the first result after a search; the others are outline', async () => {
    await loadedPage()
    await within(chosenSection()).findByText('No match chosen yet.')
    await searchFor([audible, openLibrary])

    const first = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 1 of 2' })
    const second = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' })
    expect(first).toHaveTextContent('Accept match')
    await waitFor(() => expect(first).toHaveFocus())
    expect(first).toHaveClass('bg-foreground')
    expect(second).not.toHaveClass('bg-foreground')
    const items = resultItems()
    expect(items[0]).toHaveAttribute('data-highlighted', 'true')
    expect(items[1]).not.toHaveAttribute('data-highlighted')
  })

  it('skips the current match when focusing the first result', async () => {
    getMatchMock.mockResolvedValue(storedMatch)
    await loadedPage()
    await within(chosenSection()).findByText('9780593135204')
    await searchFor([openLibrary, audible])

    const second = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' })
    await waitFor(() => expect(second).toHaveFocus())
    expect(second).toHaveClass('bg-foreground')
    expect(resultItems()[1]).toHaveAttribute('data-highlighted', 'true')
    expect(resultItems()[0]).not.toHaveAttribute('data-highlighted')
  })

  it('moves focus and the highlight with ArrowDown and ArrowUp, stopping at the ends, and accepts the focused one', async () => {
    pickMock.mockResolvedValue({ ok: true, match: { groupId: 4, match: openLibrary, pickedAtUtc: '2026-10-19T10:00:00Z' } })
    await loadedPage()
    await within(chosenSection()).findByText('No match chosen yet.')
    await searchFor([audible, openLibrary])
    const first = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 1 of 2' })
    const second = screen.getByRole('button', { name: 'Accept match: Project Hail Mary, result 2 of 2' })
    await waitFor(() => expect(first).toHaveFocus())

    expect(fireEvent.keyDown(first, { key: 'ArrowDown' })).toBe(false)
    expect(second).toHaveFocus()
    expect(second).toHaveClass('bg-foreground')
    expect(first).not.toHaveClass('bg-foreground')
    expect(resultItems()[1]).toHaveAttribute('data-highlighted', 'true')

    expect(fireEvent.keyDown(second, { key: 'ArrowDown' })).toBe(false)
    expect(second).toHaveFocus()

    fireEvent.keyDown(second, { key: 'ArrowUp' })
    expect(first).toHaveFocus()
    expect(first).toHaveClass('bg-foreground')

    fireEvent.focus(second)
    expect(second).toHaveClass('bg-foreground')
    expect(first).not.toHaveClass('bg-foreground')

    fireEvent.click(second)
    expect(pickMock).toHaveBeenCalledWith(4, openLibrary)
    expect(await screen.findByText('Match saved: Project Hail Mary')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Chosen match' })).toHaveFocus()
  })

  it('leaves focus in Keywords after a search with no results or a failure', async () => {
    await loadedPage()
    const input = screen.getByLabelText('Keywords')
    input.focus()

    searchMock.mockResolvedValueOnce({ ok: true, result: { candidates: [], providerErrors: [] } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByText('No matches found.')
    expect(input).toHaveFocus()

    searchMock.mockResolvedValueOnce({ ok: false, providerFailure: 'Audible search failed.' })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByText('Audible search failed.')
    expect(input).toHaveFocus()
  })

  it('still lets the user search when loading the match fails', async () => {
    getMatchMock.mockRejectedValue(new Error('Loading the match failed with status 500'))
    await loadedPage()

    expect(await within(chosenSection()).findByText("The chosen match couldn't be loaded.")).toBeInTheDocument()
    await searchFor([audible, openLibrary])
    expect(screen.getAllByRole('button', { name: /Accept match/ })).toHaveLength(2)
  })
})
