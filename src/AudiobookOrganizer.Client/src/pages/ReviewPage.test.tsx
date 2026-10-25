import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { chosenCoverUrl, fileCoverUrl, getReview } from '../services/api'
import type { ReviewBook, ReviewList } from '../types/review'
import { formatDuration } from '../utils/formatDuration'
import ReviewPage from './ReviewPage'

vi.mock('../services/api', () => ({
  getReview: vi.fn(),
  chosenCoverUrl: (groupId: number) => `/api/groups/${groupId}/cover/image`,
  fileCoverUrl: (groupId: number, fileId: number) => `/api/groups/${groupId}/files/${fileId}/cover`,
}))

const getReviewMock = vi.mocked(getReview)

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
  })

  it('lists the books with title, author, narrator, runtime, stage and cover', async () => {
    getReviewMock.mockResolvedValue(review)
    renderAt()

    expect(screen.getByRole('heading', { level: 1, name: 'Review' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Loading books…')

    const items = await within(await screen.findByRole('list', { name: 'Books' })).findAllByRole('listitem')
    expect(items).toHaveLength(3)

    expect(items[0]).toHaveTextContent('The Hobbit')
    expect(items[0]).toHaveTextContent('J.R.R. Tolkien')
    expect(items[0]).toHaveTextContent('Narrated by Andy Serkis')
    expect(items[0]).toHaveTextContent(formatDuration(hobbit.durationMs))
    expect(items[0]).toHaveTextContent('Ready')
    const hobbitCover = items[0].querySelector('img')!
    expect(hobbitCover).toHaveAttribute('src', chosenCoverUrl(1))
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
})
