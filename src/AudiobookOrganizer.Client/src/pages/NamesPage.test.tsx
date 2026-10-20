import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createEntry, getEntries, getSuggestions, mergeEntry, updateEntry } from '../services/api'
import type { CanonicalEntry, MergeSuggestion } from '../types/names'
import NamesPage from './NamesPage'

vi.mock('../services/api', () => ({
  getEntries: vi.fn(),
  createEntry: vi.fn(),
  updateEntry: vi.fn(),
  getSuggestions: vi.fn(),
  mergeEntry: vi.fn(),
}))

const getEntriesMock = vi.mocked(getEntries)
const createEntryMock = vi.mocked(createEntry)
const updateEntryMock = vi.mocked(updateEntry)
const getSuggestionsMock = vi.mocked(getSuggestions)
const mergeEntryMock = vi.mocked(mergeEntry)

const king: CanonicalEntry = { id: 1, name: 'Stephen King', aliases: ['John Swithen', 'Richard Bachman'] }
const pratchett: CanonicalEntry = { id: 2, name: 'Terry Pratchett', aliases: [] }
const sKing: CanonicalEntry = { id: 4, name: 'S. King', aliases: [] }
const kingSuggestion: MergeSuggestion = { keep: king, merge: sKing, reason: 'Matching initials' }
const mergedKing: CanonicalEntry = {
  id: 1,
  name: 'Stephen King',
  aliases: ['John Swithen', 'Richard Bachman', 'S. King'],
}

function duplicates() {
  return screen.getByRole('region', { name: 'Possible duplicates' })
}

function row(name: string) {
  const items = within(screen.getByRole('list', { name: 'People' })).getAllByRole('listitem')
  const item = items.find((element) => within(element).queryByText(name, { selector: 'span' }))
  if (!item) throw new Error(`No row for ${name}`)
  return item
}

function addForm() {
  return screen.getByRole('region', { name: 'New person' })
}

describe('NamesPage', () => {
  beforeEach(() => {
    getEntriesMock.mockReset()
    createEntryMock.mockReset()
    updateEntryMock.mockReset()
    getSuggestionsMock.mockReset()
    getSuggestionsMock.mockResolvedValue([])
    mergeEntryMock.mockReset()
  })

  it('lists people in the API order with their aliases', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett])

    render(<NamesPage kind="people" />)

    expect(screen.getByRole('heading', { level: 1, name: 'People' })).toBeInTheDocument()
    const list = await screen.findByRole('list', { name: 'People' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Stephen King')
    expect(items[0]).toHaveTextContent('Also known as: John Swithen, Richard Bachman')
    expect(items[1]).toHaveTextContent('Terry Pratchett')
    expect(items[1]).not.toHaveTextContent('Also known as')
    expect(getEntriesMock).toHaveBeenCalledWith('people')
  })

  it('shows "No people yet." for an empty catalog', async () => {
    getEntriesMock.mockResolvedValue([])

    render(<NamesPage kind="people" />)

    expect(await screen.findByText('No people yet.')).toBeInTheDocument()
  })

  it('shows an alert when loading fails and "Try again" reloads', async () => {
    getEntriesMock.mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce([king])

    render(<NamesPage kind="people" />)

    expect(await screen.findByRole('alert')).toHaveTextContent("People couldn't be loaded.")
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Stephen King')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(getEntriesMock).toHaveBeenCalledTimes(2)
  })

  it('adds a person, dropping blank alias lines, clears the form and lists it', async () => {
    const created = { id: 1, name: 'Stephen King', aliases: ['Richard Bachman'] }
    getEntriesMock.mockResolvedValueOnce([pratchett]).mockResolvedValueOnce([created, pratchett])
    createEntryMock.mockResolvedValue({ ok: true, entry: created })
    render(<NamesPage kind="people" />)
    await screen.findByText('Terry Pratchett')
    const form = addForm()

    fireEvent.change(within(form).getByLabelText('Name'), { target: { value: ' Stephen King ' } })
    fireEvent.change(within(form).getByLabelText('Aliases'), { target: { value: '\n Richard Bachman \n\n' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add a person' }))

    expect(await screen.findByText('Saved Stephen King.')).toHaveAttribute('role', 'status')
    expect(createEntryMock).toHaveBeenCalledWith('people', { name: 'Stephen King', aliases: ['Richard Bachman'] })
    const items = within(screen.getByRole('list', { name: 'People' })).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Stephen King')
    expect(items[0]).toHaveTextContent('Also known as: Richard Bachman')
    expect(within(form).getByLabelText('Name')).toHaveValue('')
    expect(within(form).getByLabelText('Aliases')).toHaveValue('')
  })

  it('shows a 400 name error on the field and keeps what was typed', async () => {
    getEntriesMock.mockResolvedValue([king])
    createEntryMock.mockResolvedValue({ ok: false, errors: { name: ['Already used by Stephen King.'] } })
    render(<NamesPage kind="people" />)
    await screen.findByText('Stephen King')
    const form = addForm()
    const name = within(form).getByLabelText('Name')

    fireEvent.change(name, { target: { value: 'KING, STEPHEN' } })
    fireEvent.change(within(form).getByLabelText('Aliases'), { target: { value: 'Bachman' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add a person' }))

    expect(await within(form).findByText('Already used by Stephen King.')).toBeInTheDocument()
    expect(name).toHaveAttribute('aria-invalid', 'true')
    expect(name).toHaveAccessibleDescription(expect.stringContaining('Already used by Stephen King.'))
    expect(name).toHaveValue('KING, STEPHEN')
    expect(within(form).getByLabelText('Aliases')).toHaveValue('Bachman')
    expect(within(form).getByLabelText('Aliases')).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('status')).toHaveTextContent('')
  })

  it('shows an alert and keeps the input when saving throws', async () => {
    getEntriesMock.mockResolvedValue([])
    createEntryMock.mockRejectedValue(new Error('500'))
    render(<NamesPage kind="people" />)
    await screen.findByText('No people yet.')
    const form = addForm()

    fireEvent.change(within(form).getByLabelText('Name'), { target: { value: 'Stephen King' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add a person' }))

    expect(await within(form).findByRole('alert')).toHaveTextContent("couldn't be saved")
    expect(within(form).getByLabelText('Name')).toHaveValue('Stephen King')
  })

  it('edits a person: prefilled form, Save calls updateEntry and shows the new name', async () => {
    const renamed = { id: 1, name: 'Stephen Edwin King', aliases: ['Richard Bachman'] }
    getEntriesMock.mockResolvedValueOnce([king]).mockResolvedValueOnce([renamed])
    updateEntryMock.mockResolvedValue({ ok: true, entry: renamed })
    render(<NamesPage kind="people" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Edit Stephen King' }))
    const row = within(screen.getByRole('list', { name: 'People' })).getByRole('listitem')
    const name = within(row).getByLabelText('Name')
    expect(name).toHaveValue('Stephen King')
    expect(name).toHaveFocus()
    expect(within(row).getByLabelText('Aliases')).toHaveValue('John Swithen\nRichard Bachman')

    fireEvent.change(name, { target: { value: 'Stephen Edwin King' } })
    fireEvent.change(within(row).getByLabelText('Aliases'), { target: { value: 'Richard Bachman' } })
    fireEvent.click(within(row).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved Stephen Edwin King.')).toBeInTheDocument()
    expect(updateEntryMock).toHaveBeenCalledWith('people', 1, { name: 'Stephen Edwin King', aliases: ['Richard Bachman'] })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit Stephen Edwin King' })).toHaveFocus())
  })

  it('Cancel restores the row and focuses its Edit button', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett])
    render(<NamesPage kind="people" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Edit Stephen King' }))
    const row = within(screen.getByRole('list', { name: 'People' })).getAllByRole('listitem')[0]
    fireEvent.change(within(row).getByLabelText('Name'), { target: { value: 'Changed' } })
    fireEvent.click(within(row).getByRole('button', { name: 'Cancel' }))

    const edit = screen.getByRole('button', { name: 'Edit Stephen King' })
    expect(edit).toHaveFocus()
    expect(screen.getByText('Also known as: John Swithen, Richard Bachman')).toBeInTheDocument()
    expect(updateEntryMock).not.toHaveBeenCalled()
  })

  it('uses genre labels and loads genres for kind="genres"', async () => {
    getEntriesMock.mockResolvedValue([{ id: 3, name: 'Science Fiction', aliases: ['Sci-Fi'] }])

    render(<NamesPage kind="genres" />)

    expect(screen.getByRole('heading', { level: 1, name: 'Genres' })).toBeInTheDocument()
    expect(await screen.findByText('Also called: Sci-Fi')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add a genre' })).toBeInTheDocument()
    expect(getEntriesMock).toHaveBeenCalledWith('genres')
  })

  it('lists possible duplicates with their reason and a merge button', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett, sKing])
    getSuggestionsMock.mockResolvedValue([kingSuggestion])

    render(<NamesPage kind="people" />)

    await screen.findByText('Matching initials')
    const section = duplicates()
    expect(within(section).getByRole('heading', { level: 2, name: 'Possible duplicates' })).toBeInTheDocument()
    expect(within(section).getByRole('button', { name: 'Merge S. King into Stephen King' })).toBeInTheDocument()
    expect(getSuggestionsMock).toHaveBeenCalledWith('people')
  })

  it('says "No possible duplicates." when there are none', async () => {
    getEntriesMock.mockResolvedValue([king])

    render(<NamesPage kind="people" />)

    await screen.findByText('Stephen King')
    expect(within(duplicates()).getByText('No possible duplicates.')).toBeInTheDocument()
  })

  it('shows an alert in the duplicates section when suggestions fail, and still lists entries', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett])
    getSuggestionsMock.mockRejectedValue(new Error('boom'))

    render(<NamesPage kind="people" />)

    expect(await screen.findByRole('button', { name: 'Edit Terry Pratchett' })).toBeInTheDocument()
    expect(await within(duplicates()).findByRole('alert')).toHaveTextContent("Possible duplicates couldn't be loaded.")
    expect(screen.getAllByRole('alert')).toHaveLength(1)
  })

  it('confirms a suggested merge before sending, then reloads, announces and focuses the kept entry', async () => {
    getEntriesMock.mockResolvedValueOnce([king, pratchett, sKing]).mockResolvedValueOnce([mergedKing, pratchett])
    getSuggestionsMock.mockResolvedValueOnce([kingSuggestion]).mockResolvedValueOnce([])
    mergeEntryMock.mockResolvedValue({ ok: true, entry: mergedKing })
    render(<NamesPage kind="people" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Merge S. King into Stephen King' }))

    const section = duplicates()
    expect(section).toHaveTextContent(
      'Merge S. King into Stephen King? S. King and its aliases become aliases of Stephen King.',
    )
    expect(within(section).getByRole('button', { name: 'Confirm merge' })).toHaveFocus()
    expect(mergeEntryMock).not.toHaveBeenCalled()

    fireEvent.click(within(section).getByRole('button', { name: 'Confirm merge' }))

    expect(await screen.findByText('Merged S. King into Stephen King.')).toHaveAttribute('role', 'status')
    expect(mergeEntryMock).toHaveBeenCalledWith('people', 1, 4)
    expect(getEntriesMock).toHaveBeenCalledTimes(2)
    expect(getSuggestionsMock).toHaveBeenCalledTimes(2)
    expect(screen.getByText('Also known as: John Swithen, Richard Bachman, S. King')).toBeInTheDocument()
    expect(within(duplicates()).getByText('No possible duplicates.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit Stephen King' })).toHaveFocus())
  })

  it('offers only the other entries in the row merge control, and Cancel sends nothing and refocuses Merge', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett, sKing])
    render(<NamesPage kind="people" />)
    await screen.findByRole('button', { name: 'Edit S. King' })

    const sKingRow = row('S. King')
    const select = within(sKingRow).getByLabelText('Merge S. King into')
    const options = within(select)
      .getAllByRole('option')
      .map((option) => option.textContent)
    expect(options).toEqual(['Choose…', 'Stephen King', 'Terry Pratchett'])
    const merge = within(sKingRow).getByRole('button', { name: 'Merge' })
    expect(merge).toBeDisabled()

    fireEvent.change(select, { target: { value: '1' } })
    expect(merge).toBeEnabled()
    fireEvent.click(merge)

    expect(sKingRow).toHaveTextContent(
      'Merge S. King into Stephen King? S. King and its aliases become aliases of Stephen King.',
    )
    expect(within(sKingRow).getByRole('button', { name: 'Confirm merge' })).toHaveFocus()
    fireEvent.click(within(sKingRow).getByRole('button', { name: 'Cancel' }))

    expect(within(sKingRow).queryByRole('button', { name: 'Confirm merge' })).not.toBeInTheDocument()
    expect(within(sKingRow).getByRole('button', { name: 'Merge' })).toHaveFocus()
    expect(mergeEntryMock).not.toHaveBeenCalled()
  })

  it('merges through the row control', async () => {
    getEntriesMock.mockResolvedValueOnce([king, pratchett, sKing]).mockResolvedValueOnce([mergedKing, pratchett])
    mergeEntryMock.mockResolvedValue({ ok: true, entry: mergedKing })
    render(<NamesPage kind="people" />)
    await screen.findByRole('button', { name: 'Edit S. King' })

    const sKingRow = row('S. King')
    fireEvent.change(within(sKingRow).getByLabelText('Merge S. King into'), { target: { value: '1' } })
    fireEvent.click(within(sKingRow).getByRole('button', { name: 'Merge' }))
    fireEvent.click(within(sKingRow).getByRole('button', { name: 'Confirm merge' }))

    expect(await screen.findByText('Merged S. King into Stephen King.')).toBeInTheDocument()
    expect(mergeEntryMock).toHaveBeenCalledWith('people', 1, 4)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit Stephen King' })).toHaveFocus())
  })

  it('shows "That entry no longer exists." on a 404 and reloads', async () => {
    getEntriesMock.mockResolvedValueOnce([king, pratchett, sKing]).mockResolvedValueOnce([king, pratchett])
    getSuggestionsMock.mockResolvedValueOnce([kingSuggestion]).mockResolvedValueOnce([])
    mergeEntryMock.mockResolvedValue({ ok: false, notFound: true })
    render(<NamesPage kind="people" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Merge S. King into Stephen King' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm merge' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That entry no longer exists.')
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Edit S. King' })).not.toBeInTheDocument())
    expect(getEntriesMock).toHaveBeenCalledTimes(2)
    expect(getSuggestionsMock).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('status')).toHaveTextContent('')
  })

  it('shows an alert and keeps the list when the merge throws', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett, sKing])
    getSuggestionsMock.mockResolvedValue([kingSuggestion])
    mergeEntryMock.mockRejectedValue(new Error('500'))
    render(<NamesPage kind="people" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Merge S. King into Stephen King' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm merge' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("S. King couldn't be merged into Stephen King.")
    expect(getEntriesMock).toHaveBeenCalledTimes(1)
    expect(getSuggestionsMock).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Edit S. King' })).toBeInTheDocument()
  })

  it('shows an alert and keeps the list on a 400', async () => {
    getEntriesMock.mockResolvedValue([king, pratchett, sKing])
    mergeEntryMock.mockResolvedValue({ ok: false, errors: {} })
    render(<NamesPage kind="people" />)
    await screen.findByRole('button', { name: 'Edit S. King' })

    const sKingRow = row('S. King')
    fireEvent.change(within(sKingRow).getByLabelText('Merge S. King into'), { target: { value: '1' } })
    fireEvent.click(within(sKingRow).getByRole('button', { name: 'Merge' }))
    fireEvent.click(within(sKingRow).getByRole('button', { name: 'Confirm merge' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("S. King couldn't be merged into Stephen King.")
    expect(getEntriesMock).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Edit S. King' })).toBeInTheDocument()
  })
})
