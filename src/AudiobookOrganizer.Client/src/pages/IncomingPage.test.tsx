import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getGroup, getGroups, moveFilesToGroup, reorderGroup, updateGroup } from '../services/api'
import type { GroupDetail, GroupSummary, GroupsResponse } from '../types/groups'
import IncomingPage from './IncomingPage'

vi.mock('../services/api', () => ({
  createGroup: vi.fn(),
  getGroup: vi.fn(),
  getGroups: vi.fn(),
  moveFilesToGroup: vi.fn(),
  reorderGroup: vi.fn(),
  updateGroup: vi.fn(),
}))

const getGroupsMock = vi.mocked(getGroups)
const updateGroupMock = vi.mocked(updateGroup)
const getGroupMock = vi.mocked(getGroup)
const reorderGroupMock = vi.mocked(reorderGroup)
const moveFilesMock = vi.mocked(moveFilesToGroup)

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
      userCorrected: false,
    },
    {
      id: 1,
      title: 'The Hobbit',
      author: 'J.R.R. Tolkien',
      confidence: 'High',
      evidence: ['Shared album tag'],
      fileCount: 12,
      totalDurationMs: 3_723_000,
      userCorrected: false,
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
    updateGroupMock.mockReset()
    getGroupMock.mockReset()
    reorderGroupMock.mockReset()
    moveFilesMock.mockReset()
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

  describe('editing a title and author', () => {
    async function openHobbitEditor() {
      getGroupsMock.mockResolvedValue(response)
      renderPage()
      fireEvent.click(await screen.findByRole('button', { name: 'Edit The Hobbit' }))
      return {
        title: screen.getByLabelText('Title') as HTMLInputElement,
        author: screen.getByLabelText('Author') as HTMLInputElement,
      }
    }

    it('names each Edit button after its book, with the id for an unknown title', async () => {
      getGroupsMock.mockResolvedValue(response)
      renderPage()

      expect(await screen.findByRole('button', { name: 'Edit The Hobbit' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Edit Unknown (group 2)' })).toBeInTheDocument()
      expect(screen.queryByText('Edited')).not.toBeInTheDocument()
    })

    it('opens a prefilled form with focus on Title, saves, then shows the new values with Edited', async () => {
      const { title, author } = await openHobbitEditor()

      expect(title).toHaveValue('The Hobbit')
      expect(author).toHaveValue('J.R.R. Tolkien')
      expect(title).toHaveFocus()
      expect(title).toHaveAttribute('maxLength', '500')

      const saved: GroupSummary = {
        ...response.groups[1],
        title: 'The Hobbit, or There and Back Again',
        author: 'Tolkien',
        userCorrected: true,
      }
      updateGroupMock.mockResolvedValue({ ok: true, group: saved })
      fireEvent.change(title, { target: { value: 'The Hobbit, or There and Back Again' } })
      fireEvent.change(author, { target: { value: 'Tolkien' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save' }))

      const editButton = await screen.findByRole('button', { name: 'Edit The Hobbit, or There and Back Again' })
      expect(updateGroupMock).toHaveBeenCalledWith(1, { title: 'The Hobbit, or There and Back Again', author: 'Tolkien' })
      expect(screen.queryByLabelText('Title')).not.toBeInTheDocument()
      const row = editButton.closest('tr') as HTMLElement
      expect(within(row).getByText('Edited')).toBeInTheDocument()
      expect(within(row).getAllByRole('cell')[1]).toHaveTextContent('Tolkien')
      await waitFor(() => expect(editButton).toHaveFocus())
    })

    it('edits only one row at a time', async () => {
      await openHobbitEditor()

      fireEvent.click(screen.getByRole('button', { name: 'Edit Unknown (group 2)' }))

      expect(screen.getAllByLabelText('Title')).toHaveLength(1)
      expect(screen.getByLabelText('Title')).toHaveValue('')
      expect(screen.getByRole('button', { name: 'Edit The Hobbit' })).toBeInTheDocument()
    })

    it('shows field errors linked to the input and keeps the form open on a 400', async () => {
      const { title } = await openHobbitEditor()
      updateGroupMock.mockResolvedValue({ ok: false, errors: { title: ['The title can be at most 500 characters.'] } })

      fireEvent.click(screen.getByRole('button', { name: 'Save' }))

      const message = await screen.findByText('The title can be at most 500 characters.')
      expect(title).toHaveAttribute('aria-invalid', 'true')
      const errorList = message.closest('[id]') as HTMLElement
      expect((title.getAttribute('aria-describedby') ?? '').split(' ')).toContain(errorList.id)
      expect(screen.getByLabelText('Author')).not.toHaveAttribute('aria-invalid')
      expect(screen.getByLabelText('Title')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    })

    it('Cancel restores the original values and focuses the Edit button', async () => {
      const { title } = await openHobbitEditor()
      fireEvent.change(title, { target: { value: 'Changed' } })

      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

      expect(screen.queryByLabelText('Title')).not.toBeInTheDocument()
      const editButton = screen.getByRole('button', { name: 'Edit The Hobbit' })
      await waitFor(() => expect(editButton).toHaveFocus())
      expect(screen.queryByText('Changed')).not.toBeInTheDocument()
      expect(updateGroupMock).not.toHaveBeenCalled()
    })

    it('Escape cancels and focuses the Edit button', async () => {
      const { title } = await openHobbitEditor()
      fireEvent.change(title, { target: { value: 'Changed' } })

      fireEvent.keyDown(title, { key: 'Escape' })

      expect(screen.queryByLabelText('Title')).not.toBeInTheDocument()
      const editButton = screen.getByRole('button', { name: 'Edit The Hobbit' })
      await waitFor(() => expect(editButton).toHaveFocus())
    })

    it('shows an alert and reloads the list when the group is not found', async () => {
      await openHobbitEditor()
      updateGroupMock.mockResolvedValue({ ok: false, notFound: true })

      fireEvent.click(screen.getByRole('button', { name: 'Save' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(
        "That book couldn't be saved. The list has been reloaded.",
      )
      expect(screen.queryByLabelText('Title')).not.toBeInTheDocument()
      await waitFor(() => expect(getGroupsMock).toHaveBeenCalledTimes(2))
    })

    it('shows the alert and reloads the list when saving throws', async () => {
      await openHobbitEditor()
      updateGroupMock.mockRejectedValue(new Error('offline'))

      fireEvent.click(screen.getByRole('button', { name: 'Save' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be saved/)
      await waitFor(() => expect(getGroupsMock).toHaveBeenCalledTimes(2))
    })

    it('treats a 400 with no title or author message as a failed save', async () => {
      await openHobbitEditor()
      updateGroupMock.mockResolvedValue({ ok: false, errors: {} })

      fireEvent.click(screen.getByRole('button', { name: 'Save' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be saved/)
      await waitFor(() => expect(getGroupsMock).toHaveBeenCalledTimes(2))
    })

    it('shows Edited for a group already corrected on load', async () => {
      getGroupsMock.mockResolvedValue({
        ...response,
        groups: [response.groups[0], { ...response.groups[1], userCorrected: true }],
      })
      renderPage()

      const editButton = await screen.findByRole('button', { name: 'Edit The Hobbit' })
      expect(within(editButton.closest('tr') as HTMLElement).getByText('Edited')).toBeInTheDocument()
      expect(screen.getAllByText('Edited')).toHaveLength(1)
    })
  })
  describe('reordering files', () => {
    const files = [
      { fileId: 21, relativePath: 'mystery/a.mp3', position: 1, disc: null, track: 1, durationMs: 20_000 },
      { fileId: 22, relativePath: 'mystery/b.mp3', position: 2, disc: null, track: 2, durationMs: 20_000 },
      { fileId: 23, relativePath: 'mystery/c.mp3', position: 3, disc: null, track: 3, durationMs: 21_000 },
    ]
    const detail: GroupDetail = { ...response.groups[0], files }

    function openFiles(details: HTMLDetailsElement) {
      details.open = true
      fireEvent(details, new Event('toggle'))
    }

    it('shows a Files disclosure per row that loads only when opened', async () => {
      getGroupsMock.mockResolvedValue(response)
      getGroupMock.mockResolvedValue(detail)
      renderPage()

      const summary = await screen.findByText('Files (3)', { selector: 'summary' })
      expect(screen.getByText('Files (12)', { selector: 'summary' })).toBeInTheDocument()
      expect(getGroupMock).not.toHaveBeenCalled()
      expect(screen.getAllByRole('columnheader')).toHaveLength(6)

      openFiles(summary.closest('details') as HTMLDetailsElement)

      expect(await screen.findByRole('button', { name: 'Move c.mp3 up' })).toBeInTheDocument()
      expect(getGroupMock).toHaveBeenCalledWith(2)
      expect(getGroupMock).toHaveBeenCalledTimes(1)
    })

    it('marks the row Edited after a successful reorder', async () => {
      getGroupsMock.mockResolvedValue(response)
      getGroupMock.mockResolvedValue(detail)
      reorderGroupMock.mockResolvedValue({
        ok: true,
        group: { ...detail, userCorrected: true, files: [files[0], { ...files[2], position: 2 }, { ...files[1], position: 3 }] },
      })
      renderPage()

      const summary = await screen.findByText('Files (3)', { selector: 'summary' })
      openFiles(summary.closest('details') as HTMLDetailsElement)
      fireEvent.click(await screen.findByRole('button', { name: 'Move c.mp3 up' }))

      await waitFor(() => expect(reorderGroupMock).toHaveBeenCalledWith(2, [21, 23, 22]))
      const row = summary.closest('tr') as HTMLElement
      expect(await within(row).findByText('Edited')).toBeInTheDocument()
      expect(screen.getAllByText('Edited')).toHaveLength(1)
    })
  })
  describe('moving files between books', () => {
    const files = [
      { fileId: 21, relativePath: 'mystery/a.mp3', position: 1, disc: null, track: 1, durationMs: 20_000 },
      { fileId: 22, relativePath: 'mystery/b.mp3', position: 2, disc: null, track: 2, durationMs: 20_000 },
    ]
    const detail: GroupDetail = { ...response.groups[0], fileCount: 2, files }

    function openFiles(details: HTMLDetailsElement) {
      details.open = true
      fireEvent(details, new Event('toggle'))
    }

    async function optionsFor(summaryText: string) {
      const summary = await screen.findByText(summaryText, { selector: 'summary' })
      const details = summary.closest('details') as HTMLDetailsElement
      openFiles(details)
      const select = (await within(details).findByLabelText('Move selected to')) as HTMLSelectElement
      return Array.from(select.options).map((o) => o.textContent)
    }

    it("lists the other books as targets, not the row's own", async () => {
      getGroupsMock.mockResolvedValue(response)
      getGroupMock.mockResolvedValue(detail)
      renderPage()

      expect(await optionsFor('Files (3)')).toEqual(['The Hobbit', 'New book'])
      expect(await optionsFor('Files (12)')).toEqual(['Unknown (group 2)', 'New book'])
    })

    it('reloads the list and focuses the status message after a move', async () => {
      const after: GroupsResponse = {
        ...response,
        groups: [{ ...response.groups[0], fileCount: 2 }, { ...response.groups[1], fileCount: 13, userCorrected: true }],
      }
      getGroupsMock.mockResolvedValueOnce(response).mockResolvedValue(after)
      getGroupMock.mockResolvedValue(detail)
      moveFilesMock.mockResolvedValue({ ok: true, group: { ...response.groups[1], files: [] } })
      renderPage()

      const summary = await screen.findByText('Files (3)', { selector: 'summary' })
      openFiles(summary.closest('details') as HTMLDetailsElement)
      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select b.mp3' }))
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      const status = await screen.findByRole('status')
      expect(status).toHaveTextContent('Moved 1 file to The Hobbit')
      expect(status).toHaveAttribute('tabindex', '-1')
      await waitFor(() => expect(status).toHaveFocus())
      expect(moveFilesMock).toHaveBeenCalledWith(1, [22])
      expect(getGroupsMock).toHaveBeenCalledTimes(2)
      expect(screen.getByText('Files (13)', { selector: 'summary' })).toBeInTheDocument()

      fireEvent.click(screen.getByRole('button', { name: 'Edit The Hobbit' }))
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })
  })
})
