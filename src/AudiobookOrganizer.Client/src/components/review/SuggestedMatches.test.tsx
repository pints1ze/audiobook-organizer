import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { findCandidates, getCandidates, pickMatch } from '../../services/api'
import type { BookCandidates, BookMatch, MetadataCandidate } from '../../types/metadata'
import { formatDateTime } from '../../utils/formatDateTime'
import SuggestedMatches from './SuggestedMatches'

vi.mock('../../services/api', () => ({
  getCandidates: vi.fn(),
  findCandidates: vi.fn(),
  pickMatch: vi.fn(),
}))

const getCandidatesMock = vi.mocked(getCandidates)
const findCandidatesMock = vi.mocked(findCandidates)
const pickMock = vi.mocked(pickMatch)

function candidate(overrides: Partial<MetadataCandidate> = {}): MetadataCandidate {
  return {
    provider: 'audible',
    asin: 'B08G9PRS1K',
    title: 'Project Hail Mary',
    subtitle: null,
    authors: ['Andy Weir'],
    narrators: ['Ray Porter'],
    series: null,
    publisher: null,
    releaseDate: null,
    language: null,
    runtimeMinutes: 970,
    format: null,
    coverUrl: null,
    isbn: null,
    ...overrides,
  }
}

const best = candidate()
const second = candidate({ asin: 'B000000002', title: 'Project Hail Mary (Dramatized)', runtimeMinutes: 600 })

const stored: BookCandidates = {
  groupId: 4,
  searchedAtUtc: '2026-10-25T10:00:00Z',
  candidates: [
    { candidate: best, score: 87 },
    { candidate: second, score: 50 },
  ],
  errors: [],
}

function renderMatches(currentMatch: MetadataCandidate | null = null, onPicked = vi.fn()) {
  render(
    <MemoryRouter>
      <SuggestedMatches groupId={4} currentMatch={currentMatch} onPicked={onPicked} />
    </MemoryRouter>,
  )
  return onPicked
}

function area() {
  return screen.getByRole('region', { name: 'Suggested matches' })
}

describe('SuggestedMatches', () => {
  beforeEach(() => {
    getCandidatesMock.mockReset()
    findCandidatesMock.mockReset()
    pickMock.mockReset()
  })

  it('shows stored candidates as cards best first with their scores and the search time', async () => {
    getCandidatesMock.mockResolvedValue(stored)
    renderMatches()

    const cards = await within(area()).findAllByRole('listitem')
    expect(getCandidatesMock).toHaveBeenCalledWith(4)
    expect(cards).toHaveLength(2)
    expect(cards[0]).toHaveTextContent('Project Hail Mary')
    expect(cards[0]).toHaveTextContent('Score 87 of 100')
    expect(cards[0]).toHaveTextContent('Ray Porter')
    expect(cards[1]).toHaveTextContent('Score 50 of 100')
    expect(area()).toHaveTextContent(`Searched ${formatDateTime(stored.searchedAtUtc)}`)
    const buttons = within(area()).getAllByRole('button', { name: /^Accept match/ })
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual([
      'Accept match: Project Hail Mary, score 87 of 100',
      'Accept match: Project Hail Mary (Dramatized), score 50 of 100',
    ])
    expect(buttons[0]).toHaveAttribute('data-primary', 'true')
    expect(buttons[1]).not.toHaveAttribute('data-primary')
    expect(within(area()).getByRole('button', { name: 'Search again' })).toBeInTheDocument()
  })

  it('shows a stored provider error and an empty result as text', async () => {
    getCandidatesMock.mockResolvedValue({ ...stored, candidates: [], errors: ['Audible: the catalog timed out.'] })
    renderMatches()

    expect(await within(area()).findByText('Audible: the catalog timed out.')).toBeInTheDocument()
    expect(area()).toHaveTextContent('No suggestions found.')
    expect(within(area()).queryByRole('listitem')).not.toBeInTheDocument()
  })

  it('shows an empty search with no errors as text', async () => {
    getCandidatesMock.mockResolvedValue({ ...stored, candidates: [] })
    renderMatches()

    expect(await within(area()).findByText('No suggestions found.')).toBeInTheDocument()
  })

  it('shows an alert when the suggestions fail to load', async () => {
    getCandidatesMock.mockRejectedValue(new Error('boom'))
    renderMatches()

    expect(await within(area()).findByRole('alert')).toHaveTextContent("The suggested matches couldn't be loaded.")
  })

  it('offers Find suggestions for a book never searched and moves focus to the heading after it', async () => {
    getCandidatesMock.mockResolvedValue(null)
    let resolve: (value: BookCandidates) => void = () => {}
    findCandidatesMock.mockReturnValue(new Promise((r) => (resolve = r)))
    renderMatches()

    expect(await within(area()).findByText('Not searched yet.')).toBeInTheDocument()
    const find = within(area()).getByRole('button', { name: 'Find suggestions' })
    fireEvent.click(find)

    expect(findCandidatesMock).toHaveBeenCalledWith(4)
    expect(find).toBeDisabled()
    expect(within(area()).getByRole('status')).toHaveTextContent('Searching…')

    resolve(stored)
    expect(await within(area()).findAllByRole('listitem')).toHaveLength(2)
    expect(within(area()).getByRole('status')).toHaveTextContent('2 suggestions found.')
    expect(within(area()).queryByRole('button', { name: 'Find suggestions' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Suggested matches' })).toHaveFocus()
  })

  it('shows an alert when the search fails', async () => {
    getCandidatesMock.mockResolvedValue(null)
    findCandidatesMock.mockRejectedValue(new Error('boom'))
    renderMatches()

    fireEvent.click(await within(area()).findByRole('button', { name: 'Find suggestions' }))
    expect(await within(area()).findByRole('alert')).toHaveTextContent("The search couldn't be run.")
    expect(within(area()).getByRole('button', { name: 'Find suggestions' })).toBeEnabled()
  })

  it('accepts a card through pickMatch and then calls onPicked', async () => {
    getCandidatesMock.mockResolvedValue(stored)
    const saved: BookMatch = { groupId: 4, match: second, pickedAtUtc: '2026-10-25T11:00:00Z' }
    pickMock.mockResolvedValue({ ok: true, match: saved })
    const onPicked = renderMatches()

    fireEvent.click(await within(area()).findByRole('button', { name: /score 50 of 100/ }))

    await waitFor(() => expect(onPicked).toHaveBeenCalledWith(saved))
    expect(pickMock).toHaveBeenCalledWith(4, second)
  })

  it('shows "Current match" with no button on the card equal to the current match', async () => {
    getCandidatesMock.mockResolvedValue(stored)
    renderMatches(best)

    const cards = await within(area()).findAllByRole('listitem')
    expect(cards[0]).toHaveTextContent('Current match')
    expect(within(cards[0]).queryByRole('button')).not.toBeInTheDocument()
    expect(within(cards[1]).getByRole('button', { name: /^Accept match/ })).toHaveAttribute('data-primary', 'true')
  })

  it('shows an alert beside the card when the pick fails', async () => {
    getCandidatesMock.mockResolvedValue(stored)
    pickMock.mockResolvedValue({ ok: false, errors: { title: ['A title is required.'] } })
    const onPicked = renderMatches()

    const cards = await within(area()).findAllByRole('listitem')
    fireEvent.click(within(cards[1]).getByRole('button', { name: /^Accept match/ }))

    expect(await within(cards[1]).findByRole('alert')).toHaveTextContent('A title is required.')
    expect(onPicked).not.toHaveBeenCalled()
  })
})
