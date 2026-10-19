import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getGroups } from '../services/api'
import type { GroupsResponse } from '../types/groups'
import IncomingPage from './IncomingPage'

vi.mock('../services/api', () => ({ getGroups: vi.fn() }))

const getGroupsMock = vi.mocked(getGroups)

const response: GroupsResponse = {
  groups: [
    {
      id: 2,
      title: null,
      author: null,
      confidence: 'Low',
      evidence: ['Album tags conflict', 'Same folder'],
      fileCount: 3,
      totalDurationMs: 61_000,
    },
    {
      id: 1,
      title: 'The Hobbit',
      author: 'J.R.R. Tolkien',
      confidence: 'High',
      evidence: ['Shared album tag'],
      fileCount: 12,
      totalDurationMs: 3_723_000,
    },
  ],
  unresolved: [
    { fileId: 7, relativePath: 'stray/track.mp3', durationMs: 125_000 },
    { fileId: 8, relativePath: 'loose.m4b', durationMs: null },
  ],
}

function renderPage() {
  return render(
    <MemoryRouter>
      <IncomingPage />
    </MemoryRouter>,
  )
}

describe('IncomingPage', () => {
  beforeEach(() => {
    getGroupsMock.mockReset()
  })

  it('shows loading, then the groups in API order with their details and evidence', async () => {
    getGroupsMock.mockResolvedValue(response)
    renderPage()

    expect(screen.getByRole('heading', { level: 1, name: 'Incoming' })).toBeInTheDocument()
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: 'Proposed books (2)' })).toBeInTheDocument()

    const table = screen.getByRole('table')
    expect(within(table).getByText(/proposed books/i, { selector: 'caption' })).toBeInTheDocument()
    const headers = within(table).getAllByRole('columnheader')
    expect(headers.map((h) => h.textContent)).toEqual(['Title', 'Author', 'Files', 'Duration', 'Confidence', 'Evidence'])
    headers.forEach((h) => expect(h).toHaveAttribute('scope', 'col'))

    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)

    const first = within(rows[0]).getAllByRole('cell')
    expect(first[0]).toHaveTextContent('Unknown')
    expect(first[1]).toHaveTextContent('Unknown')
    expect(first[2]).toHaveTextContent('3')
    expect(first[3]).toHaveTextContent('1:01')
    expect(first[4]).toHaveTextContent('Low')
    const firstDetails = first[5].querySelector('details')
    expect(firstDetails).not.toBeNull()
    expect(within(firstDetails as HTMLElement).getByText('Evidence', { selector: 'summary' })).toBeInTheDocument()
    expect(within(firstDetails as HTMLElement).getByText('Album tags conflict')).toBeInTheDocument()
    expect(within(firstDetails as HTMLElement).getByText('Same folder')).toBeInTheDocument()

    const second = within(rows[1]).getAllByRole('cell')
    expect(second[0]).toHaveTextContent('The Hobbit')
    expect(second[1]).toHaveTextContent('J.R.R. Tolkien')
    expect(second[2]).toHaveTextContent('12')
    expect(second[3]).toHaveTextContent('1:02:03')
    expect(second[4]).toHaveTextContent('High')
    expect(within(second[5].querySelector('details') as HTMLElement).getByText('Shared album tag')).toBeInTheDocument()
  })

  it('lists the unresolved files with paths and durations', async () => {
    getGroupsMock.mockResolvedValue(response)
    renderPage()

    const heading = await screen.findByRole('heading', { level: 2, name: 'Unresolved files (2)' })
    const section = heading.closest('section') as HTMLElement
    expect(within(section).getByText(/too little evidence/i)).toBeInTheDocument()
    const items = within(section).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('stray/track.mp3')
    expect(items[0]).toHaveTextContent('2:05')
    expect(items[1]).toHaveTextContent('loose.m4b')
    expect(items[1]).toHaveTextContent('—')
  })

  it('shows the empty state with a Dashboard link when there is nothing', async () => {
    getGroupsMock.mockResolvedValue({ groups: [], unresolved: [] })
    renderPage()

    expect(await screen.findByText(/No books found yet\./)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /dashboard/i })).toHaveAttribute('href', '/')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows an alert when the load fails', async () => {
    getGroupsMock.mockRejectedValue(new Error('boom'))
    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be loaded/i)
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })
})
