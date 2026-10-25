import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  acceptSuggested,
  approveBook,
  chooseCover,
  chosenCoverUrl,
  fileCoverUrl,
  getBookPreview,
  getCandidates,
  getCovers,
  getLatestScan,
  getMatch,
  getMetadata,
  getReview,
  getSettings,
  pickMatch,
  saveMetadata,
  searchMetadata,
  startScan,
} from '../services/api'
import type { ScanRun } from '../types/dashboard'
import type { BookCovers, BookMetadata, MetadataCandidate } from '../types/metadata'
import type { BookPreview, FilePreview } from '../types/preview'
import type { ReviewBook, ReviewList } from '../types/review'
import { formatDateTime } from '../utils/formatDateTime'
import { formatDuration } from '../utils/formatDuration'
import ReviewPage from './ReviewPage'

vi.mock('../services/api', () => ({
  getReview: vi.fn(),
  acceptSuggested: vi.fn(),
  approveBook: vi.fn(),
  getMatch: vi.fn(),
  getCandidates: vi.fn(),
  findCandidates: vi.fn(),
  searchMetadata: vi.fn(),
  pickMatch: vi.fn(),
  getCovers: vi.fn(),
  chooseCover: vi.fn(),
  uploadCover: vi.fn(),
  getMetadata: vi.fn(),
  saveMetadata: vi.fn(),
  getBookPreview: vi.fn(),
  getLatestScan: vi.fn(),
  startScan: vi.fn(),
  getSettings: vi.fn(),
  chosenCoverUrl: (groupId: number) => `/api/groups/${groupId}/cover/image`,
  fileCoverUrl: (groupId: number, fileId: number) => `/api/groups/${groupId}/files/${fileId}/cover`,
}))

const getReviewMock = vi.mocked(getReview)
const acceptSuggestedMock = vi.mocked(acceptSuggested)
const getMatchMock = vi.mocked(getMatch)
const getCandidatesMock = vi.mocked(getCandidates)
const searchMock = vi.mocked(searchMetadata)
const pickMock = vi.mocked(pickMatch)
const getCoversMock = vi.mocked(getCovers)
const chooseCoverMock = vi.mocked(chooseCover)
const getMetadataMock = vi.mocked(getMetadata)
const saveMetadataMock = vi.mocked(saveMetadata)
const getBookPreviewMock = vi.mocked(getBookPreview)
const approveMock = vi.mocked(approveBook)
const getLatestScanMock = vi.mocked(getLatestScan)
const startScanMock = vi.mocked(startScan)
const getSettingsMock = vi.mocked(getSettings)

const completedScan: ScanRun = {
  id: 1,
  startedAt: '2026-10-25T10:00:00Z',
  finishedAt: '2026-10-25T10:01:00Z',
  status: 'Completed',
  filesFound: 12,
  filesAdded: 3,
  filesUpdated: 0,
  filesUnchanged: 9,
  filesMissing: 0,
  filesFailed: 0,
  error: null,
}

const runningScan: ScanRun = {
  ...completedScan,
  id: 2,
  finishedAt: null,
  status: 'Running',
  filesFound: 5,
  filesAdded: 0,
  filesUnchanged: 0,
}

const settings = {
  incomingPath: '/incoming',
  libraryPath: '/library',
  directoryTemplate: '%album%',
  fileTemplate: '%title%',
  keepSourceFiles: false,
}

/** A book preview whose files go under /lib/<folder>/ with the given problems on each file. */
function previewFor(groupId: number, folder: string, files: Partial<FilePreview>[] = [{}]): BookPreview {
  return {
    groupId,
    libraryPath: '/lib',
    title: null,
    author: null,
    files: files.map((overrides, index) => ({
      fileId: groupId * 10 + index,
      sourcePath: `/incoming/${groupId}/${String(index + 1).padStart(2, '0')}.mp3`,
      relativePath: `${folder}/${index + 1}.mp3`,
      destinationPath: `/lib/${folder}/${index + 1}.mp3`,
      missing: [],
      error: null,
      duplicate: false,
      exists: false,
      ...overrides,
    })),
  }
}

function noCovers(groupId: number): BookCovers {
  return { groupId, chosen: null, embedded: [], provider: null }
}

/** A book's metadata with only a file-origin title. */
function metadataFor(groupId: number, title: string | null, origin: 'file' | 'user' = 'file'): BookMetadata {
  const empty = { value: null, origin: null, provider: null }
  const emptyList = { value: [], origin: null, provider: null }
  return {
    groupId,
    fields: {
      title: { value: title, origin: title ? origin : null, provider: null },
      subtitle: empty,
      authors: emptyList,
      narrators: emptyList,
      genres: emptyList,
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

function book(overrides: Partial<ReviewBook> & { groupId: number }): ReviewBook {
  return {
    title: 'Untitled',
    author: null,
    narrator: null,
    durationMs: 3_600_000,
    fileCount: 1,
    stage: 'NeedsMatch',
    coverSource: null,
    coverFileId: null,
    coverChosenAtUtc: null,
    suggestedScore: null,
    ...overrides,
  }
}

const hobbit = book({
  groupId: 1,
  title: 'The Hobbit',
  author: 'J.R.R. Tolkien',
  narrator: 'Andy Serkis',
  durationMs: 37_800_000,
  stage: 'Ready',
  coverSource: 'chosen',
  coverChosenAtUtc: '2026-10-20T08:00:00Z',
})
const dune = book({
  groupId: 2,
  title: 'Dune',
  author: 'Frank Herbert',
  durationMs: 75_600_000,
  coverSource: 'embedded',
  coverFileId: 20,
})
const lostWorld = book({ groupId: 3, title: 'The Lost World', author: 'Arthur Conan Doyle', durationMs: 1_800_000 })

const review: ReviewList = {
  books: [hobbit, dune, lostWorld],
  counts: { needsMatch: 2, needsReview: 0, ready: 1, queued: 0, failed: 0 },
}

function candidate(overrides: Partial<MetadataCandidate>): MetadataCandidate {
  return {
    provider: 'audible',
    asin: 'B002V1OF70',
    title: 'Dune',
    subtitle: null,
    authors: ['Frank Herbert'],
    narrators: ['Scott Brick'],
    series: null,
    publisher: null,
    releaseDate: '2007-01-01',
    language: 'english',
    runtimeMinutes: 1263,
    format: 'unabridged',
    coverUrl: null,
    isbn: null,
    ...overrides,
  }
}

const duneCandidate = candidate({})

/** The review list after Dune's match was picked: Dune is Ready. */
const reviewAfterPick: ReviewList = {
  books: [hobbit, { ...dune, stage: 'Ready' }, lostWorld],
  counts: { needsMatch: 1, needsReview: 0, ready: 2, queued: 0, failed: 0 },
}

function LocationDisplay() {
  const location = useLocation()
  return <div data-testid="location">{location.pathname + location.search}</div>
}

function renderAt(path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ReviewPage />
      <LocationDisplay />
    </MemoryRouter>,
  )
}

function bookList() {
  return screen.getByRole('list', { name: 'Books' })
}

function bookLink(name: RegExp) {
  return within(bookList()).getByRole('link', { name })
}

function search() {
  return new URLSearchParams(screen.getByTestId('location').textContent!.split('?')[1] ?? '')
}

describe('ReviewPage', () => {
  beforeEach(() => {
    getReviewMock.mockReset()
    acceptSuggestedMock.mockReset()
    getMatchMock.mockReset()
    getMatchMock.mockResolvedValue(null)
    getCandidatesMock.mockReset()
    getCandidatesMock.mockResolvedValue(null)
    searchMock.mockReset()
    pickMock.mockReset()
    getCoversMock.mockReset()
    getCoversMock.mockImplementation(async (groupId) => noCovers(groupId))
    chooseCoverMock.mockReset()
    getMetadataMock.mockReset()
    getMetadataMock.mockImplementation(async (groupId) => metadataFor(groupId, null))
    saveMetadataMock.mockReset()
    getBookPreviewMock.mockReset()
    getBookPreviewMock.mockImplementation(async (groupId) => previewFor(groupId, `Book ${groupId}`))
    approveMock.mockReset()
    getLatestScanMock.mockReset()
    getLatestScanMock.mockResolvedValue(null)
    startScanMock.mockReset()
    getSettingsMock.mockReset()
    getSettingsMock.mockResolvedValue(settings)
  })

  it('lists the books with title, author, narrator, runtime, stage and cover', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt()

    expect(screen.getByRole('heading', { level: 1, name: 'Review' })).toBeInTheDocument()
    expect(screen.getByText('Loading books…')).toHaveAttribute('role', 'status')

    const items = await within(await screen.findByRole('list', { name: 'Books' })).findAllByRole('listitem')
    expect(items).toHaveLength(3)

    expect(items[0]).toHaveTextContent('The Hobbit')
    expect(items[0]).toHaveTextContent('J.R.R. Tolkien')
    expect(items[0]).toHaveTextContent('Narrated by Andy Serkis')
    expect(items[0]).toHaveTextContent(formatDuration(hobbit.durationMs))
    expect(items[0]).toHaveTextContent('Ready')
    const hobbitCover = items[0].querySelector('img')!
    expect(hobbitCover).toHaveAttribute('src', `${chosenCoverUrl(1)}?v=2026-10-20T08%3A00%3A00Z`)
    expect(hobbitCover).toHaveAttribute('alt', '')

    expect(items[1]).toHaveTextContent('Dune')
    expect(items[1]).toHaveTextContent('Frank Herbert')
    expect(items[1]).not.toHaveTextContent('Narrated by')
    expect(items[1]).toHaveTextContent('Needs match')
    expect(items[1].querySelector('img')).toHaveAttribute('src', fileCoverUrl(2, 20))

    expect(items[2].querySelector('img')).toBeNull()
    expect(within(items[2]).getByText('TL')).toBeInTheDocument()
  })

  it('shows the initials when a cover fails to load, in the list and the pane', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt('/?book=2')

    const items = await within(await screen.findByRole('list', { name: 'Books' })).findAllByRole('listitem')
    fireEvent.error(items[1].querySelector('img')!)
    expect(items[1].querySelector('img')).toBeNull()
    expect(within(items[1]).getByText('D')).toBeInTheDocument()

    const pane = screen.getByRole('region', { name: 'Dune' })
    fireEvent.error(pane.querySelector('img')!)
    expect(pane.querySelector('img')).toBeNull()
    expect(within(pane).getByText('D')).toBeInTheDocument()
  })

  it('filters by stage through the URL and says when a stage is empty', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt('/?book=1')
    await screen.findByRole('list', { name: 'Books' })

    const all = screen.getByRole('button', { name: 'All 3' })
    expect(all).toHaveAttribute('aria-pressed', 'true')

    const needsMatch = screen.getByRole('button', { name: 'Needs match 2' })
    expect(needsMatch).toHaveAttribute('aria-pressed', 'false')
    fireEvent.click(needsMatch)

    expect(needsMatch).toHaveAttribute('aria-pressed', 'true')
    expect(all).toHaveAttribute('aria-pressed', 'false')
    expect(search().get('stage')).toBe('NeedsMatch')
    expect(search().get('book')).toBe('1')
    const items = within(bookList()).getAllByRole('listitem')
    expect(items.map((i) => within(i).getByRole('link').textContent)).toEqual([
      expect.stringContaining('Dune'),
      expect.stringContaining('The Lost World'),
    ])
    // The book link keeps the filter.
    expect(bookLink(/Dune/)).toHaveAttribute('href', '/?stage=NeedsMatch&book=2')

    fireEvent.click(screen.getByRole('button', { name: 'Failed 0' }))
    expect(screen.getByText('No books in this stage.')).toBeInTheDocument()
    expect(within(bookList()).queryAllByRole('listitem')).toHaveLength(0)

    fireEvent.click(all)
    expect(search().get('stage')).toBeNull()
    expect(within(bookList()).getAllByRole('listitem')).toHaveLength(3)
  })

  it('shows the chosen book in the pane and marks it in the list', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt()
    await screen.findByRole('list', { name: 'Books' })

    expect(screen.getByText('Choose a book from the list.')).toBeInTheDocument()
    expect(bookLink(/Dune/)).not.toHaveAttribute('aria-current')

    fireEvent.click(bookLink(/Dune/))

    expect(search().get('book')).toBe('2')
    expect(bookLink(/Dune/)).toHaveAttribute('aria-current', 'true')
    expect(bookLink(/The Hobbit/)).not.toHaveAttribute('aria-current')
    const pane = screen.getByRole('region', { name: 'Dune' })
    expect(within(pane).getByRole('heading', { level: 2, name: 'Dune' })).toBeInTheDocument()
    expect(pane).toHaveTextContent('Frank Herbert')
    expect(pane.querySelector('img')).toHaveAttribute('src', fileCoverUrl(2, 20))
    expect(within(pane).getByRole('link', { name: 'Open book page' })).toHaveAttribute('href', '/books/2')
    expect(screen.queryByText('Choose a book from the list.')).not.toBeInTheDocument()

    fireEvent.click(within(pane).getByRole('link', { name: 'Back to list' }))
    expect(search().get('book')).toBeNull()
    expect(screen.getByText('Choose a book from the list.')).toBeInTheDocument()
  })

  it('selects the book in the URL on load', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt('/?book=3')

    const pane = await screen.findByRole('region', { name: 'The Lost World' })
    expect(within(pane).getByText('TL')).toBeInTheDocument()
    expect(bookLink(/The Lost World/)).toHaveAttribute('aria-current', 'true')
  })

  it('shows an alert when the list fails to load, and Retry loads it again', async () => {
    getReviewMock.mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(review)
    renderAt()

    expect(await screen.findByRole('alert')).toHaveTextContent("The books couldn't be loaded.")
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('list', { name: 'Books' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(getReviewMock).toHaveBeenCalledTimes(2)
  })

  it('shows the match section with no match yet and the search prefilled with title and author', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt('/?book=2')

    const match = await screen.findByRole('region', { name: 'Match' })
    expect(await within(match).findByText('No match chosen yet.')).toBeInTheDocument()
    expect(getMatchMock).toHaveBeenCalledWith(2)
    expect(within(match).getByRole('searchbox', { name: 'Keywords' })).toHaveValue('Dune Frank Herbert')
  })

  it("shows the book's stored match under Match", async () => {
    getReviewMock.mockResolvedValue(review)
    getMatchMock.mockResolvedValue({
      groupId: 1,
      match: candidate({ title: 'The Hobbit', authors: ['J.R.R. Tolkien'], narrators: ['Andy Serkis'] }),
      pickedAtUtc: '2026-10-19T10:00:00Z',
    })
    renderAt('/?book=1')

    const match = await screen.findByRole('region', { name: 'Match' })
    expect(await within(match).findByText('The Hobbit')).toBeInTheDocument()
    expect(match).toHaveTextContent('Andy Serkis')
    expect(screen.queryByText('No match chosen yet.')).not.toBeInTheDocument()
  })

  it("shows the selected book's stored suggestions under Match", async () => {
    getReviewMock.mockResolvedValue(review)
    getCandidatesMock.mockResolvedValue({
      groupId: 2,
      searchedAtUtc: '2026-10-25T10:00:00Z',
      candidates: [{ candidate: candidate({ title: 'Dune', authors: ['Frank Herbert'] }), score: 92 }],
      errors: [],
    })
    renderAt('/?book=2')

    const suggestions = await screen.findByRole('region', { name: 'Suggested matches' })
    expect(within(screen.getByRole('region', { name: 'Match' })).getByRole('region', { name: 'Suggested matches' })).toBe(suggestions)
    expect(await within(suggestions).findByRole('button', { name: 'Accept match: Dune, score 92 of 100' })).toBeInTheDocument()
    expect(getCandidatesMock).toHaveBeenCalledWith(2)
  })

  it('shows an error line when the match fails to load', async () => {
    getReviewMock.mockResolvedValue(review)
    getMatchMock.mockRejectedValue(new Error('boom'))
    renderAt('/?book=2')

    const match = await screen.findByRole('region', { name: 'Match' })
    expect(await within(match).findByText("The chosen match couldn't be loaded.")).toBeInTheDocument()
  })

  async function searchAndPick() {
    searchMock.mockResolvedValue({ ok: true, result: { candidates: [duneCandidate], providerErrors: [] } })
    pickMock.mockResolvedValue({ ok: true, match: { groupId: 2, match: duneCandidate, pickedAtUtc: '2026-10-25T10:00:00Z' } })
    const match = await screen.findByRole('region', { name: 'Match' })
    await within(match).findByText('No match chosen yet.')
    fireEvent.click(within(match).getByRole('button', { name: 'Search' }))
    fireEvent.click(await within(match).findByRole('button', { name: 'Accept match: Dune, result 1 of 1' }))
    return match
  }

  it('saves a picked match, focuses the Match heading and reloads the list in the background', async () => {
    getReviewMock.mockResolvedValueOnce(review).mockResolvedValueOnce(reviewAfterPick)
    renderAt('/?book=2')

    const match = await searchAndPick()

    expect(await within(match).findByText('Match saved: Dune')).toHaveAttribute('role', 'status')
    expect(pickMock).toHaveBeenCalledWith(2, duneCandidate)
    expect(within(match).getByRole('heading', { level: 2, name: 'Match' })).toHaveFocus()
    expect(within(match).getByText('Current match')).toBeInTheDocument()

    await waitFor(() => expect(within(bookList()).getAllByRole('listitem')[1]).toHaveTextContent('Ready'))
    expect(getReviewMock).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: 'Needs match 1' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ready 2' })).toBeInTheDocument()
    expect(screen.queryByText('Loading books…')).not.toBeInTheDocument()
  })

  it('keeps the pane open when the picked book leaves the stage filter', async () => {
    getReviewMock.mockResolvedValueOnce(review).mockResolvedValueOnce(reviewAfterPick)
    renderAt('/?stage=NeedsMatch&book=2')

    await searchAndPick()

    await waitFor(() => expect(within(bookList()).queryByRole('link', { name: /Dune/ })).not.toBeInTheDocument())
    expect(screen.getByRole('region', { name: 'Dune' })).toBeInTheDocument()
    expect(screen.getByText('Match saved: Dune')).toBeInTheDocument()
    expect(search().get('book')).toBe('2')
  })

  it('keeps the list and the pane when the background reload fails', async () => {
    getReviewMock.mockResolvedValueOnce(review).mockRejectedValueOnce(new Error('boom'))
    renderAt('/?book=2')

    await searchAndPick()

    expect(await screen.findByText('Match saved: Dune')).toBeInTheDocument()
    await waitFor(() => expect(getReviewMock).toHaveBeenCalledTimes(2))
    expect(within(bookList()).getAllByRole('listitem')).toHaveLength(3)
    expect(screen.queryByText("The books couldn't be loaded.")).not.toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Dune' })).toBeInTheDocument()
  })

  it('resets the pane when another book is chosen', async () => {
    getReviewMock.mockResolvedValue(review)
    searchMock.mockResolvedValue({ ok: true, result: { candidates: [duneCandidate], providerErrors: [] } })
    renderAt('/?book=2')

    const match = await screen.findByRole('region', { name: 'Match' })
    fireEvent.change(within(match).getByRole('searchbox', { name: 'Keywords' }), { target: { value: 'dune herbert' } })
    fireEvent.click(within(match).getByRole('button', { name: 'Search' }))
    expect(await within(match).findByText('1 match found.')).toBeInTheDocument()

    fireEvent.click(bookLink(/The Lost World/))

    const next = await screen.findByRole('region', { name: 'Match' })
    expect(within(next).getByRole('searchbox', { name: 'Keywords' })).toHaveValue('The Lost World Arthur Conan Doyle')
    expect(screen.queryByText('1 match found.')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Accept match/ })).not.toBeInTheDocument()
    expect(getMatchMock).toHaveBeenLastCalledWith(3)
  })
  it('shows the Cover and Metadata sections; a chosen cover reloads the list and becomes its thumbnail', async () => {
    const duneCovers: BookCovers = { groupId: 2, chosen: null, embedded: [{ fileId: 20, relativePath: 'Dune/01.mp3' }], provider: null }
    getCoversMock.mockResolvedValue(duneCovers)
    getReviewMock
      .mockResolvedValueOnce(review)
      .mockResolvedValueOnce({ ...review, books: [hobbit, { ...dune, coverSource: 'chosen', coverFileId: null, coverChosenAtUtc: '2026-10-25T10:00:00Z' }, lostWorld] })
    chooseCoverMock.mockResolvedValue({
      ok: true,
      cover: { ...duneCovers, chosen: { source: 'embedded', fileId: 20, mimeType: 'image/jpeg', chosenAtUtc: '2026-10-25T10:00:00Z' } },
    })
    renderAt('/?book=2')
    const duneChosen = `${chosenCoverUrl(2)}?v=2026-10-25T10%3A00%3A00Z`

    const cover = await screen.findByRole('region', { name: 'Cover' })
    expect(screen.getByRole('region', { name: 'Metadata' })).toBeInTheDocument()
    expect(getCoversMock).toHaveBeenCalledWith(2)
    expect(getMetadataMock).toHaveBeenCalledWith(2)

    fireEvent.click(await within(cover).findByRole('button', { name: 'Use this cover: Dune/01.mp3' }))

    expect(await within(cover).findByText('Cover saved.')).toBeInTheDocument()
    await waitFor(() => expect(getReviewMock).toHaveBeenCalledTimes(2))
    await waitFor(() =>
      expect(within(bookList()).getAllByRole('listitem')[1].querySelector('img')).toHaveAttribute('src', duneChosen),
    )
    expect(screen.getByRole('region', { name: 'Dune' }).querySelector('img')).toHaveAttribute('src', duneChosen)
  })

  it('a second cover change shows the new image in the list and pane, not the cached one', async () => {
    const at = (time: string) => ({ ...dune, coverSource: 'chosen' as const, coverFileId: null, coverChosenAtUtc: time })
    const duneCovers: BookCovers = {
      groupId: 2,
      chosen: { source: 'embedded', fileId: 20, mimeType: 'image/jpeg', chosenAtUtc: '2026-10-25T10:00:00Z' },
      embedded: [{ fileId: 20, relativePath: 'Dune/01.mp3' }],
      provider: null,
    }
    getCoversMock.mockResolvedValue({ ...duneCovers, embedded: [...duneCovers.embedded, { fileId: 21, relativePath: 'Dune/02.mp3' }] })
    getReviewMock
      .mockResolvedValueOnce({ ...review, books: [hobbit, at('2026-10-25T10:00:00Z'), lostWorld] })
      .mockResolvedValueOnce({ ...review, books: [hobbit, at('2026-10-25T11:00:00Z'), lostWorld] })
    chooseCoverMock.mockResolvedValue({
      ok: true,
      cover: { ...duneCovers, chosen: { source: 'embedded', fileId: 21, mimeType: 'image/jpeg', chosenAtUtc: '2026-10-25T11:00:00Z' } },
    })
    renderAt('/?book=2')

    const pane = await screen.findByRole('region', { name: 'Dune' })
    const before = pane.querySelector('img')!.getAttribute('src')
    fireEvent.click(await within(await screen.findByRole('region', { name: 'Cover' })).findByRole('button', { name: 'Use this cover: Dune/02.mp3' }))

    const after = `${chosenCoverUrl(2)}?v=2026-10-25T11%3A00%3A00Z`
    await waitFor(() => expect(within(bookList()).getAllByRole('listitem')[1].querySelector('img')).toHaveAttribute('src', after))
    expect(screen.getByRole('region', { name: 'Dune' }).querySelector('img')).toHaveAttribute('src', after)
    expect(after).not.toBe(before)
  })

  it('saving metadata for a Needs match book reloads the list and its chip shows the new stage', async () => {
    getReviewMock
      .mockResolvedValueOnce(review)
      .mockResolvedValueOnce({ ...review, books: [hobbit, dune, { ...lostWorld, stage: 'Ready' }] })
    saveMetadataMock.mockResolvedValue({ ok: true, metadata: metadataFor(3, 'The Lost World', 'user') })
    renderAt('/?book=3')

    const metadata = await screen.findByRole('region', { name: 'Metadata' })
    fireEvent.click(await within(metadata).findByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(within(metadata).getByLabelText('Title'), { target: { value: 'The Lost World' } })
    fireEvent.click(within(metadata).getByRole('button', { name: 'Save' }))

    expect(await within(metadata).findByText('Metadata saved.')).toBeInTheDocument()
    expect(saveMetadataMock).toHaveBeenCalledWith(3, { title: 'The Lost World' })
    await waitFor(() => expect(within(bookList()).getAllByRole('listitem')[2]).toHaveTextContent('Ready'))
    expect(getReviewMock).toHaveBeenCalledTimes(2)
  })

  it("picking a match reloads the covers and metadata, and the editor shows the match's proposed values", async () => {
    getReviewMock.mockResolvedValueOnce(review).mockResolvedValueOnce(reviewAfterPick)
    renderAt('/?book=2')
    const metadata = await screen.findByRole('region', { name: 'Metadata' })
    await within(metadata).findByRole('button', { name: 'Edit metadata' })
    expect(metadata).not.toHaveTextContent('Match: Scott Brick')

    await searchAndPick()

    await waitFor(() => expect(getCoversMock).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(getMetadataMock).toHaveBeenCalledTimes(2))
    expect(await within(metadata).findByText('Match: Scott Brick')).toBeInTheDocument()
  })

  it('gives the pane title and its sections h2 headings with nothing deeper than h3', async () => {
    getReviewMock.mockResolvedValue(review)
    searchMock.mockResolvedValue({ ok: true, result: { candidates: [duneCandidate], providerErrors: [] } })
    renderAt('/?book=2')
    const pane = await screen.findByRole('region', { name: 'Dune' })
    await within(pane).findByRole('button', { name: 'Edit metadata' })
    fireEvent.click(within(pane).getByRole('button', { name: 'Search' }))
    await within(pane).findByRole('button', { name: 'Accept match: Dune, result 1 of 1' })

    const headings = within(pane)
      .getAllByRole('heading')
      .map((h) => ({ level: Number(h.tagName.slice(1)), name: h.textContent }))
    expect(headings.filter((h) => h.level === 2).map((h) => h.name)).toEqual([
      'Dune',
      'Match',
      'Catalog search',
      'Cover',
      'Metadata',
      'Destination',
    ])
    expect(headings[1].level).toBe(2)
    expect(headings.every((h) => h.level === 2 || h.level === 3)).toBe(true)
  })
  it('shows the destination folder, the file count and "No problems with the destination." for a clean book', async () => {
    getReviewMock.mockResolvedValue(review)
    let resolvePreview: (preview: BookPreview) => void = () => {}
    getBookPreviewMock.mockReturnValue(new Promise((resolve) => (resolvePreview = resolve)))
    renderAt('/?book=1')

    const destination = await screen.findByRole('region', { name: 'Destination' })
    expect(within(destination).getByRole('status')).toHaveTextContent('Loading the destination…')
    resolvePreview(previewFor(1, 'J.R.R. Tolkien/The Hobbit', [{}, {}]))
    expect(await within(destination).findByText('No problems with the destination.')).toBeInTheDocument()
    expect(getBookPreviewMock).toHaveBeenCalledWith(1)
    expect(destination).toHaveTextContent('/lib/J.R.R. Tolkien/The Hobbit')
    expect(destination).toHaveTextContent('2 files')
    expect(within(destination).getByText('Show all 2 files')).toBeInTheDocument()
    expect(within(destination).getByText('J.R.R. Tolkien/The Hobbit/2.mp3')).toBeInTheDocument()
  })

  it('lists a missing token with its file name outside the collapsed file list', async () => {
    getReviewMock.mockResolvedValue(review)
    getBookPreviewMock.mockResolvedValue(previewFor(2, 'Frank Herbert/Dune', [{}, { missing: ['Series'] }]))
    renderAt('/?book=2')

    const destination = await screen.findByRole('region', { name: 'Destination' })
    const problem = await within(destination).findByText('02.mp3: Missing: Series')
    expect(problem.closest('details')).toBeNull()
    expect(destination).not.toHaveTextContent('No problems with the destination.')
  })

  it('reloads the destination after a metadata save and shows the new folder', async () => {
    getReviewMock.mockResolvedValue(review)
    getBookPreviewMock
      .mockResolvedValueOnce(previewFor(3, 'Arthur Conan Doyle/Lost World'))
      .mockResolvedValueOnce(previewFor(3, 'Arthur Conan Doyle/The Lost World'))
    saveMetadataMock.mockResolvedValue({ ok: true, metadata: metadataFor(3, 'The Lost World', 'user') })
    renderAt('/?book=3')

    const destination = await screen.findByRole('region', { name: 'Destination' })
    expect(await within(destination).findByText('/lib/Arthur Conan Doyle/Lost World')).toBeInTheDocument()

    const metadata = screen.getByRole('region', { name: 'Metadata' })
    fireEvent.click(await within(metadata).findByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(within(metadata).getByLabelText('Title'), { target: { value: 'The Lost World' } })
    fireEvent.click(within(metadata).getByRole('button', { name: 'Save' }))

    expect(await within(destination).findByText('/lib/Arthur Conan Doyle/The Lost World')).toBeInTheDocument()
    expect(getBookPreviewMock).toHaveBeenCalledTimes(2)
    expect(await within(metadata).findByText('Metadata saved.')).toBeInTheDocument()
  })

  it('shows an inline alert when the destination fails to load', async () => {
    getReviewMock.mockResolvedValue(review)
    getBookPreviewMock.mockRejectedValue(new Error('boom'))
    renderAt('/?book=2')

    const destination = await screen.findByRole('region', { name: 'Destination' })
    expect(await within(destination).findByRole('alert')).toHaveTextContent("The destination couldn't be loaded.")
  })

  it('says "Show the file" for a one-file book', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt('/?book=2')

    const destination = await screen.findByRole('region', { name: 'Destination' })
    expect(await within(destination).findByText('Show the file')).toBeInTheDocument()
    expect(destination).toHaveTextContent('1 file')
    expect(destination).not.toHaveTextContent('Show all')
  })

  describe('Approve & next', () => {
    const queuedJob = { ok: true as const, job: {} as never }
    const ready = (b: ReviewBook) => ({ ...b, stage: 'Ready' as const })
    const allReady: ReviewList = {
      books: [hobbit, ready(dune), ready(lostWorld)],
      counts: { needsMatch: 0, needsReview: 0, ready: 3, queued: 0, failed: 0 },
    }

    it('shows the filled button for a Ready book, described by its title', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?book=1')

      const pane = await screen.findByRole('region', { name: 'The Hobbit' })
      const button = within(pane).getByRole('button', { name: 'Approve & next' })
      expect(button).toHaveClass('bg-foreground')
      expect(button).toHaveAccessibleDescription('The Hobbit')
    })

    it('is the only filled button in the pane, also while the metadata is being edited', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?book=1')

      const pane = await screen.findByRole('region', { name: 'The Hobbit' })
      fireEvent.click(await within(pane).findByRole('button', { name: 'Edit metadata' }))
      await within(pane).findByRole('button', { name: 'Save' })

      const filled = within(pane)
        .getAllByRole('button')
        .filter((b) => b.classList.contains('bg-foreground'))
        .map((b) => b.textContent)
      expect(filled).toEqual(['Approve & next'])
    })

    it.each([
      ['NeedsReview', 'Fix the problems under Destination to approve.', false],
      ['NeedsMatch', 'Pick a match or enter the metadata to approve.', false],
      ['Queued', 'Queued for processing.', true],
      ['Failed', 'Processing failed.', true],
    ] as const)('shows the %s line and no button', async (stage, line, jobsLink) => {
      getReviewMock.mockResolvedValue({ ...review, books: [{ ...hobbit, stage }, dune, lostWorld] })
      renderAt('/?book=1')

      const pane = await screen.findByRole('region', { name: 'The Hobbit' })
      expect(within(pane).getByText(line)).toBeInTheDocument()
      expect(within(pane).queryByRole('button', { name: 'Approve & next' })).not.toBeInTheDocument()
      if (jobsLink) expect(within(pane).getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/jobs')
      else expect(within(pane).queryByRole('link', { name: 'Jobs' })).not.toBeInTheDocument()
    })

    it('queues the book, selects the next one, announces it, focuses its title and reloads the list', async () => {
      getReviewMock.mockResolvedValueOnce(allReady).mockResolvedValueOnce({
        books: [{ ...hobbit, stage: 'Queued' }, ready(dune), ready(lostWorld)],
        counts: { ...allReady.counts, ready: 2, queued: 1 },
      })
      approveMock.mockResolvedValue(queuedJob)
      renderAt('/?book=1')

      fireEvent.click(await screen.findByRole('button', { name: 'Approve & next' }))

      const next = await screen.findByRole('region', { name: 'Dune' })
      expect(approveMock).toHaveBeenCalledWith(1)
      expect(search().get('book')).toBe('2')
      expect(screen.getByText('Queued The Hobbit.')).toHaveAttribute('role', 'status')
      expect(within(next).getByRole('heading', { level: 2, name: 'Dune' })).toHaveFocus()
      await waitFor(() => expect(within(bookList()).getAllByRole('listitem')[0]).toHaveTextContent('Queued'))
      expect(getReviewMock).toHaveBeenCalledTimes(2)
      expect(screen.getByRole('button', { name: 'Queued 1' })).toBeInTheDocument()

      // Choosing another book, even the approved one, clears the status line.
      fireEvent.click(bookLink(/The Hobbit/))
      expect(screen.queryByText('Queued The Hobbit.')).not.toBeInTheDocument()
    })

    it('under a stage filter, approving the last shown book selects the one before it', async () => {
      getReviewMock.mockResolvedValue(allReady)
      approveMock.mockResolvedValue(queuedJob)
      renderAt('/?stage=Ready&book=3')

      fireEvent.click(await screen.findByRole('button', { name: 'Approve & next' }))

      const next = await screen.findByRole('region', { name: 'Dune' })
      expect(search().get('book')).toBe('2')
      expect(search().get('stage')).toBe('Ready')
      expect(within(next).getByRole('heading', { level: 2, name: 'Dune' })).toHaveFocus()
    })

    it('approving the only shown book clears the selection and focuses the status line', async () => {
      getReviewMock.mockResolvedValue(review)
      approveMock.mockResolvedValue(queuedJob)
      renderAt('/?stage=Ready&book=1')

      fireEvent.click(await screen.findByRole('button', { name: 'Approve & next' }))

      const status = await screen.findByText('Queued The Hobbit.')
      expect(search().get('book')).toBeNull()
      expect(search().get('stage')).toBe('Ready')
      expect(screen.getByText('Choose a book from the list.')).toBeInTheDocument()
      await waitFor(() => expect(status).toHaveFocus())
    })

    it('shows "Approving…" while pending and a refusal as an alert, keeping the book selected', async () => {
      getReviewMock.mockResolvedValue(review)
      let resolveApprove: (result: Awaited<ReturnType<typeof approveBook>>) => void = () => {}
      approveMock.mockReturnValue(new Promise((resolve) => (resolveApprove = resolve)))
      renderAt('/?book=1')

      fireEvent.click(await screen.findByRole('button', { name: 'Approve & next' }))
      expect(screen.getByRole('button', { name: 'Approving…' })).toBeDisabled()

      resolveApprove({ ok: false, message: 'This book was already processed.' })

      const pane = screen.getByRole('region', { name: 'The Hobbit' })
      expect(await within(pane).findByRole('alert')).toHaveTextContent('This book was already processed.')
      expect(within(pane).getByRole('button', { name: 'Approve & next' })).toBeEnabled()
      expect(search().get('book')).toBe('1')
      expect(getReviewMock).toHaveBeenCalledTimes(1)
    })

    it('shows an alert when the approval request fails', async () => {
      getReviewMock.mockResolvedValue(review)
      approveMock.mockRejectedValue(new Error('network'))
      renderAt('/?book=1')

      fireEvent.click(await screen.findByRole('button', { name: 'Approve & next' }))

      const pane = screen.getByRole('region', { name: 'The Hobbit' })
      expect(await within(pane).findByRole('alert')).toHaveTextContent("The book couldn't be approved.")
      expect(search().get('book')).toBe('1')
    })
  })

  describe('keyboard shortcuts', () => {
    const key = (k: string, init: KeyboardEventInit = {}) => fireEvent.keyDown(document.activeElement ?? document.body, { key: k, ...init })

    it('j selects the first book, then the next, focusing its link; k goes back', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt()
      await screen.findByRole('list', { name: 'Books' })

      key('j')
      expect(search().get('book')).toBe('1')
      await waitFor(() => expect(bookLink(/The Hobbit/)).toHaveFocus())

      key('j')
      expect(search().get('book')).toBe('2')
      await waitFor(() => expect(bookLink(/Dune/)).toHaveFocus())
      expect(await screen.findByRole('region', { name: 'Dune' })).toBeInTheDocument()

      key('k')
      expect(search().get('book')).toBe('1')
      await waitFor(() => expect(bookLink(/The Hobbit/)).toHaveFocus())
    })

    it('steps once per press even when presses come faster than renders (key repeat)', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt()
      await screen.findByRole('list', { name: 'Books' })

      act(() => {
        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'j', bubbles: true }))
        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'j', bubbles: true }))
      })
      expect(search().get('book')).toBe('2')
      await waitFor(() => expect(bookLink(/Dune/)).toHaveFocus())
    })

    it('k with no book selects the last; j on the last shown book stays put and keeps the stage', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?stage=NeedsMatch')
      await screen.findByRole('list', { name: 'Books' })

      key('k')
      expect(search().get('book')).toBe('3')
      expect(search().get('stage')).toBe('NeedsMatch')

      key('j')
      expect(search().get('book')).toBe('3')

      key('k')
      expect(search().get('book')).toBe('2')
      key('k')
      expect(search().get('book')).toBe('2')
      expect(search().get('stage')).toBe('NeedsMatch')
    })

    it('ignores keys typed in the Keywords field and keys with Ctrl, Alt or Meta', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?book=2')
      const keywords = await screen.findByLabelText('Keywords')

      fireEvent.keyDown(keywords, { key: 'j' })
      expect(search().get('book')).toBe('2')

      fireEvent.keyDown(document.body, { key: 'j', ctrlKey: true })
      fireEvent.keyDown(document.body, { key: 'j', altKey: true })
      fireEvent.keyDown(document.body, { key: 'j', metaKey: true })
      expect(search().get('book')).toBe('2')
    })

    it('a approves a Ready book and moves on; Ctrl+a does not', async () => {
      getReviewMock.mockResolvedValue(review)
      approveMock.mockResolvedValue({ ok: true as const, job: {} as never })
      renderAt('/?book=1')
      await screen.findByRole('button', { name: 'Approve & next' })

      fireEvent.keyDown(document.body, { key: 'a', ctrlKey: true })
      expect(approveMock).not.toHaveBeenCalled()

      fireEvent.keyDown(document.body, { key: 'a' })
      const next = await screen.findByRole('region', { name: 'Dune' })
      expect(approveMock).toHaveBeenCalledWith(1)
      expect(search().get('book')).toBe('2')
      expect(within(next).getByRole('heading', { level: 2, name: 'Dune' })).toHaveFocus()
    })

    it('a does nothing on a Needs match book', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?book=2')
      await screen.findByText('Pick a match or enter the metadata to approve.')

      fireEvent.keyDown(document.body, { key: 'a' })
      expect(approveMock).not.toHaveBeenCalled()
      expect(search().get('book')).toBe('2')
    })

    it('/ focuses the Keywords field without typing a slash', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?book=2')
      const keywords = await screen.findByLabelText('Keywords')
      const before = (keywords as HTMLInputElement).value

      const notPrevented = fireEvent.keyDown(document.body, { key: '/' })
      expect(notPrevented).toBe(false)
      expect(keywords).toHaveFocus()
      expect(keywords).toHaveValue(before)
    })

    it('/, type, search, then pressing the focused Accept match saves it and focuses the Match heading', async () => {
      getReviewMock.mockResolvedValueOnce(review).mockResolvedValueOnce(reviewAfterPick)
      searchMock.mockResolvedValue({ ok: true, result: { candidates: [duneCandidate], providerErrors: [] } })
      pickMock.mockResolvedValue({ ok: true, match: { groupId: 2, match: duneCandidate, pickedAtUtc: '2026-10-25T10:00:00Z' } })
      renderAt('/?book=2')
      const match = await screen.findByRole('region', { name: 'Match' })
      await within(match).findByText('No match chosen yet.')

      fireEvent.keyDown(document.body, { key: '/' })
      const keywords = within(match).getByRole('searchbox', { name: 'Keywords' })
      expect(keywords).toHaveFocus()
      fireEvent.change(keywords, { target: { value: 'dune herbert' } })
      fireEvent.submit(keywords.closest('form')!)

      const accept = await within(match).findByRole('button', { name: 'Accept match: Dune, result 1 of 1' })
      await waitFor(() => expect(accept).toHaveFocus())
      fireEvent.click(document.activeElement!)

      expect(await within(match).findByText('Match saved: Dune')).toBeInTheDocument()
      expect(pickMock).toHaveBeenCalledWith(2, duneCandidate)
      expect(within(match).getByRole('heading', { level: 2, name: 'Match' })).toHaveFocus()
    })

    it('? toggles the shortcuts popover, which lists every key; Esc closes it and returns focus', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt()
      await screen.findByRole('list', { name: 'Books' })
      const trigger = screen.getByRole('button', { name: 'Keyboard shortcuts' })

      fireEvent.keyDown(document.body, { key: '?' })
      const dialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })
      const keys = Array.from(dialog.querySelectorAll('dt kbd')).map((k) => k.textContent)
      expect(keys).toEqual(['j', 'k', 'a', '/', 'Enter', '?'])
      expect(dialog).toHaveTextContent('Accept the highlighted match (in the search results)')

      fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' })
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(trigger).toHaveFocus()

      fireEvent.click(trigger)
      expect(await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })).toBeInTheDocument()
      fireEvent.keyDown(document.body, { key: '?' })
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    })
  })
  describe('scan in the header', () => {
    afterEach(() => {
      vi.useRealTimers()
    })

    /** Settles pending promises and runs timers due within `ms`, under fake timers. */
    async function advance(ms = 0) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(ms)
      })
    }

    function scanStatus() {
      return screen.getByTestId('scan-status')
    }

    it('shows the last completed scan, or "No scans yet."', async () => {
      getReviewMock.mockResolvedValue(review)
      getLatestScanMock.mockResolvedValue(completedScan)
      const { unmount } = renderAt()

      await waitFor(() =>
        expect(scanStatus()).toHaveTextContent(`Last scan ${formatDateTime(completedScan.finishedAt)}: 3 files added`),
      )
      expect(scanStatus()).toHaveAttribute('role', 'status')
      unmount()

      getLatestScanMock.mockResolvedValue(null)
      renderAt()
      await waitFor(() => expect(scanStatus()).toHaveTextContent('No scans yet.'))
      expect(screen.getByRole('button', { name: 'Scan incoming' })).not.toHaveAttribute('aria-disabled')
    })

    it('starts a scan, shows progress, then announces completion and reloads the list keeping the selection', async () => {
      vi.useFakeTimers()
      getReviewMock.mockResolvedValue(review)
      getLatestScanMock
        .mockResolvedValueOnce(completedScan)
        .mockResolvedValueOnce({ ...runningScan, filesFound: 2 })
        .mockResolvedValueOnce({ ...completedScan, id: 2, filesAdded: 4 })
      startScanMock.mockResolvedValue({ started: true, run: { ...runningScan, filesFound: 0 } })

      renderAt('/?stage=NeedsMatch&book=2')
      await advance()
      expect(getReviewMock).toHaveBeenCalledTimes(1)

      const button = screen.getByRole('button', { name: 'Scan incoming' })
      button.focus()
      fireEvent.click(button)
      await advance()
      expect(startScanMock).toHaveBeenCalledTimes(1)
      expect(button).toHaveAttribute('aria-disabled', 'true')
      expect(button).not.toBeDisabled()
      expect(button).toHaveFocus()

      await advance(1000)
      expect(scanStatus()).toHaveTextContent('Scanning… 2 files found')

      fireEvent.click(button)
      await advance()
      expect(startScanMock).toHaveBeenCalledTimes(1)

      await advance(1000)
      expect(scanStatus()).toHaveTextContent('Scan completed')
      expect(scanStatus()).toHaveTextContent(': 4 files added')
      expect(button).not.toHaveAttribute('aria-disabled')
      expect(getReviewMock).toHaveBeenCalledTimes(2)
      expect(search().get('book')).toBe('2')
      expect(search().get('stage')).toBe('NeedsMatch')
      expect(screen.getByRole('region', { name: 'Dune' })).toBeInTheDocument()

      await advance(5000)
      expect(getLatestScanMock).toHaveBeenCalledTimes(3)
    })

    it('shows the message when a scan is already running (409)', async () => {
      getReviewMock.mockResolvedValue(review)
      startScanMock.mockResolvedValue({ started: false, message: 'A scan is already running.' })
      renderAt()
      await waitFor(() => expect(scanStatus()).toHaveTextContent('No scans yet.'))

      fireEvent.click(screen.getByRole('button', { name: 'Scan incoming' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('A scan is already running.')
    })

    it('shows an alert when the scan request fails', async () => {
      getReviewMock.mockResolvedValue(review)
      startScanMock.mockRejectedValue(new Error('boom'))
      renderAt()
      await waitFor(() => expect(scanStatus()).toHaveTextContent('No scans yet.'))

      fireEvent.click(screen.getByRole('button', { name: 'Scan incoming' }))

      expect(await screen.findByRole('alert')).toHaveTextContent("The scan couldn't be started.")
    })

    it('resumes polling a scan already running on load, and reloads the list when it completes', async () => {
      vi.useFakeTimers()
      getReviewMock.mockResolvedValue(review)
      getLatestScanMock.mockResolvedValueOnce(runningScan).mockResolvedValueOnce(completedScan)

      renderAt('/?book=1')
      await advance()
      expect(screen.getByRole('button', { name: 'Scan incoming' })).toHaveAttribute('aria-disabled', 'true')
      expect(scanStatus()).toHaveTextContent('Scanning… 5 files found')
      expect(getReviewMock).toHaveBeenCalledTimes(1)

      await advance(1000)
      expect(scanStatus()).toHaveTextContent('Scan completed')
      expect(getReviewMock).toHaveBeenCalledTimes(2)
      expect(search().get('book')).toBe('1')
    })

    it('announces a failed scan with its error', async () => {
      vi.useFakeTimers()
      getReviewMock.mockResolvedValue(review)
      startScanMock.mockResolvedValue({ started: true, run: runningScan })
      getLatestScanMock
        .mockResolvedValueOnce(null)
        .mockResolvedValueOnce({ ...runningScan, status: 'Failed', error: 'Disk gone.' })

      renderAt()
      await advance()
      fireEvent.click(screen.getByRole('button', { name: 'Scan incoming' }))
      await advance()
      await advance(1000)

      expect(scanStatus()).toHaveTextContent('Scan failed: Disk gone.')
      expect(screen.getByRole('button', { name: 'Scan incoming' })).not.toHaveAttribute('aria-disabled')
    })

    it('shows the last failed scan on load', async () => {
      getReviewMock.mockResolvedValue(review)
      getLatestScanMock.mockResolvedValue({ ...completedScan, status: 'Failed', error: 'Disk gone.' })
      renderAt()

      await waitFor(() => expect(scanStatus()).toHaveTextContent('Last scan failed: Disk gone.'))
    })

    it('links to Settings instead of the button when no incoming folder is set', async () => {
      getReviewMock.mockResolvedValue(review)
      getSettingsMock.mockResolvedValue({ ...settings, incomingPath: null })
      renderAt()

      const link = await screen.findByRole('link', { name: 'Set the incoming folder in Settings' })
      expect(link).toHaveAttribute('href', '/settings')
      expect(screen.queryByRole('button', { name: 'Scan incoming' })).not.toBeInTheDocument()
    })

    it('stops polling on unmount', async () => {
      vi.useFakeTimers()
      getReviewMock.mockResolvedValue(review)
      getLatestScanMock.mockResolvedValue(runningScan)

      const { unmount } = renderAt()
      await advance()
      unmount()
      await advance(5000)

      expect(getLatestScanMock).toHaveBeenCalledTimes(1)
    })
  })

  describe('selection bar', () => {
    function selectionBar() {
      return screen.getByRole('group', { name: 'Selected books' })
    }

    function checkbox(title: string) {
      return within(bookList()).getByRole('checkbox', { name: `Select ${title}` })
    }

    function acceptButton() {
      return within(selectionBar()).getByRole('button', { name: 'Accept suggested match' })
    }

    it('selects books by checkbox and counts them', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt()
      await screen.findByRole('list', { name: 'Books' })

      expect(selectionBar()).toHaveTextContent('0 selected')
      expect(acceptButton()).toHaveAttribute('aria-disabled', 'true')

      fireEvent.click(checkbox('The Hobbit'))
      fireEvent.click(checkbox('Dune'))

      expect(selectionBar()).toHaveTextContent('2 selected')
      expect(checkbox('The Hobbit')).toBeChecked()
      expect(checkbox('The Lost World')).not.toBeChecked()
      expect(acceptButton()).not.toHaveAttribute('aria-disabled')

      fireEvent.click(within(selectionBar()).getByRole('button', { name: 'Clear selection' }))
      expect(selectionBar()).toHaveTextContent('0 selected')
      expect(checkbox('Dune')).not.toBeChecked()
    })

    it('selects all shown books under a stage filter and shows the mixed state', async () => {
      getReviewMock.mockResolvedValue(review)
      renderAt('/?stage=NeedsMatch')
      await screen.findByRole('list', { name: 'Books' })

      const all = within(selectionBar()).getByRole('checkbox', { name: 'Select all shown' })
      expect(all).not.toBeChecked()
      fireEvent.click(all)

      expect(selectionBar()).toHaveTextContent('2 selected')
      expect(checkbox('Dune')).toBeChecked()
      expect(checkbox('The Lost World')).toBeChecked()
      expect(all).toBeChecked()

      fireEvent.click(checkbox('Dune'))
      expect(selectionBar()).toHaveTextContent('1 selected')
      expect(all).toBePartiallyChecked()

      // Showing every stage: the Hobbit was never selected.
      fireEvent.click(screen.getByRole('button', { name: 'All 3' }))
      expect(checkbox('The Hobbit')).not.toBeChecked()
      expect(checkbox('The Lost World')).toBeChecked()
    })

    it('accepts the suggested matches, reports skipped books and keeps only them selected', async () => {
      getReviewMock.mockResolvedValueOnce(review).mockResolvedValue(reviewAfterPick)
      acceptSuggestedMock.mockResolvedValue([
        { groupId: 2, accepted: true, error: null },
        { groupId: 3, accepted: false, error: 'No suggested match.' },
      ])
      renderAt('/?book=2')
      await screen.findByRole('list', { name: 'Books' })
      await waitFor(() => expect(getMatchMock).toHaveBeenCalledWith(2))
      const matchCalls = getMatchMock.mock.calls.length

      fireEvent.click(checkbox('Dune'))
      fireEvent.click(checkbox('The Lost World'))
      acceptButton().focus()
      fireEvent.click(acceptButton())

      const status = await within(selectionBar().parentElement!).findByRole('status')
      await waitFor(() => expect(status).toHaveTextContent('Accepted 1 suggested match.'))
      expect(status).toHaveTextContent('The Lost World: No suggested match.')
      expect(acceptSuggestedMock).toHaveBeenCalledWith([2, 3])
      expect(getReviewMock).toHaveBeenCalledTimes(2)

      await waitFor(() => expect(selectionBar()).toHaveTextContent('1 selected'))
      expect(checkbox('Dune')).not.toBeChecked()
      expect(checkbox('The Lost World')).toBeChecked()
      expect(acceptButton()).toHaveFocus()
      // The pane remounts, so the selected book loads its new match.
      await waitFor(() => expect(getMatchMock.mock.calls.length).toBeGreaterThan(matchCalls))
    })

    it('shows an alert and keeps the selection when accepting fails', async () => {
      getReviewMock.mockResolvedValue(review)
      acceptSuggestedMock.mockRejectedValue(new Error('status 500'))
      renderAt()
      await screen.findByRole('list', { name: 'Books' })

      fireEvent.click(checkbox('Dune'))
      fireEvent.click(acceptButton())

      const alert = await within(selectionBar().parentElement!).findByRole('alert')
      expect(alert).toHaveTextContent("The suggested matches couldn't be accepted.")
      expect(selectionBar()).toHaveTextContent('1 selected')
      expect(checkbox('Dune')).toBeChecked()
      expect(getReviewMock).toHaveBeenCalledTimes(1)
    })

    it('drops books no longer listed from the selection after a reload', async () => {
      getReviewMock
        .mockResolvedValueOnce(review)
        .mockResolvedValue({ ...review, books: [hobbit, lostWorld] })
      acceptSuggestedMock.mockResolvedValue([{ groupId: 3, accepted: false, error: 'No suggested match.' }])
      renderAt()
      await screen.findByRole('list', { name: 'Books' })

      fireEvent.click(checkbox('Dune'))
      fireEvent.click(checkbox('The Lost World'))
      expect(selectionBar()).toHaveTextContent('2 selected')
      // Accepting reloads the list; Dune has gone from it.
      fireEvent.click(acceptButton())

      await waitFor(() => expect(within(bookList()).queryByRole('checkbox', { name: 'Select Dune' })).toBeNull())
      expect(selectionBar()).toHaveTextContent('1 selected')
    })

    describe('approve selected', () => {
      // Hobbit and Dune Ready, the Lost World Needs match.
      const readyReview: ReviewList = {
        books: [hobbit, { ...dune, stage: 'Ready' }, lostWorld],
        counts: { needsMatch: 1, needsReview: 0, ready: 2, queued: 0, failed: 0 },
      }

      function approveSelected() {
        return within(selectionBar()).getByRole('button', { name: /^Approve/ })
      }

      function confirmation() {
        return screen.queryByRole('group', { name: 'Confirm approval' })
      }

      it('counts only the selected Ready books, and says why when none are', async () => {
        getReviewMock.mockResolvedValue(readyReview)
        renderAt()
        await screen.findByRole('list', { name: 'Books' })

        fireEvent.click(checkbox('The Lost World'))
        expect(approveSelected()).toHaveAccessibleName('Approve selected')
        expect(approveSelected()).toHaveAttribute('aria-disabled', 'true')
        expect(selectionBar()).toHaveTextContent('No Ready books selected.')
        fireEvent.click(approveSelected())
        expect(confirmation()).toBeNull()

        fireEvent.click(checkbox('The Hobbit'))
        fireEvent.click(checkbox('Dune'))
        expect(approveSelected()).toHaveAccessibleName('Approve 2 Ready books')
        expect(approveSelected()).not.toHaveAttribute('aria-disabled')
        expect(selectionBar()).not.toHaveTextContent('No Ready books selected.')
      })

      it('opens the confirmation with focus on Approve; Esc closes it and returns focus', async () => {
        getReviewMock.mockResolvedValue(readyReview)
        renderAt()
        await screen.findByRole('list', { name: 'Books' })

        fireEvent.click(checkbox('The Hobbit'))
        fireEvent.click(checkbox('Dune'))
        fireEvent.click(approveSelected())

        const group = confirmation()!
        expect(group).toHaveTextContent("Approve 2 books? They'll be copied, tagged and moved into the library.")
        expect(within(group).getByRole('button', { name: 'Approve' })).toHaveFocus()
        expect(within(group).getByRole('button', { name: 'Cancel' })).toBeInTheDocument()

        fireEvent.keyDown(within(group).getByRole('button', { name: 'Approve' }), { key: 'Escape' })
        expect(confirmation()).toBeNull()
        expect(approveSelected()).toHaveFocus()
        expect(approveMock).not.toHaveBeenCalled()

        fireEvent.click(approveSelected())
        fireEvent.click(within(confirmation()!).getByRole('button', { name: 'Cancel' }))
        expect(confirmation()).toBeNull()
        expect(approveSelected()).toHaveFocus()
        expect(approveMock).not.toHaveBeenCalled()
      })

      it('queues the Ready books only, reports the refused one and keeps it and the others selected', async () => {
        getReviewMock
          .mockResolvedValueOnce(readyReview)
          .mockResolvedValue({ ...readyReview, books: [{ ...hobbit, stage: 'Queued' }, readyReview.books[1], lostWorld] })
        approveMock.mockImplementation(async (groupId) =>
          groupId === 1
            ? { ok: true, job: { id: 9, groupId: 1, title: 'The Hobbit', status: 'Queued', createdAtUtc: '', startedAtUtc: null, finishedAtUtc: null, error: null, files: [] } }
            : { ok: false, message: 'This book has destination problems.' },
        )
        renderAt('/?book=2')
        await screen.findByRole('list', { name: 'Books' })
        await waitFor(() => expect(getMatchMock).toHaveBeenCalledWith(2))
        const matchCalls = getMatchMock.mock.calls.length

        fireEvent.click(checkbox('The Hobbit'))
        fireEvent.click(checkbox('Dune'))
        fireEvent.click(checkbox('The Lost World'))
        fireEvent.click(approveSelected())
        fireEvent.click(within(confirmation()!).getByRole('button', { name: 'Approve' }))

        const status = within(selectionBar().parentElement!).getByRole('status')
        await waitFor(() => expect(status).toHaveTextContent('Queued 1 book.'))
        expect(status).toHaveTextContent('Dune: This book has destination problems.')
        expect(approveMock.mock.calls).toEqual([[1], [2]])
        expect(confirmation()).toBeNull()
        await waitFor(() => expect(getReviewMock).toHaveBeenCalledTimes(2))

        await waitFor(() => expect(selectionBar()).toHaveTextContent('2 selected'))
        expect(checkbox('The Hobbit')).not.toBeChecked()
        expect(checkbox('Dune')).toBeChecked()
        expect(checkbox('The Lost World')).toBeChecked()
        expect(approveSelected()).toHaveFocus()
        await waitFor(() => expect(getMatchMock.mock.calls.length).toBeGreaterThan(matchCalls))
      })

      it('reports a book whose request failed and still sends the rest', async () => {
        getReviewMock.mockResolvedValue(readyReview)
        approveMock.mockRejectedValueOnce(new Error('status 500')).mockResolvedValueOnce({ ok: false, message: 'Already queued.' })
        renderAt()
        await screen.findByRole('list', { name: 'Books' })

        fireEvent.click(checkbox('The Hobbit'))
        fireEvent.click(checkbox('Dune'))
        fireEvent.click(approveSelected())
        fireEvent.click(within(confirmation()!).getByRole('button', { name: 'Approve' }))

        const status = within(selectionBar().parentElement!).getByRole('status')
        await waitFor(() => expect(status).toHaveTextContent('Queued 0 books.'))
        expect(status).toHaveTextContent("The Hobbit: The book couldn't be approved. Check the connection and try again.")
        expect(status).toHaveTextContent('Dune: Already queued.')
        expect(approveMock).toHaveBeenCalledTimes(2)
      })
    })

    it("shows a Needs match book's suggestion score", async () => {
      getReviewMock.mockResolvedValue({
        ...review,
        books: [{ ...hobbit, suggestedScore: 95 }, { ...dune, suggestedScore: 87 }, lostWorld],
      })
      renderAt()

      const items = await within(await screen.findByRole('list', { name: 'Books' })).findAllByRole('listitem')
      expect(items[1]).toHaveTextContent('Suggestion 87')
      expect(items[0]).not.toHaveTextContent('Suggestion')
      expect(items[2]).not.toHaveTextContent('Suggestion')
    })
  })
})
