import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createEntry, getEntries, updateEntry } from '../services/api'
import type { CanonicalEntry } from '../types/names'
import NamesPage from './NamesPage'

vi.mock('../services/api', () => ({ getEntries: vi.fn(), createEntry: vi.fn(), updateEntry: vi.fn() }))

const getEntriesMock = vi.mocked(getEntries)
const createEntryMock = vi.mocked(createEntry)
const updateEntryMock = vi.mocked(updateEntry)

const king: CanonicalEntry = { id: 1, name: 'Stephen King', aliases: ['John Swithen', 'Richard Bachman'] }
const pratchett: CanonicalEntry = { id: 2, name: 'Terry Pratchett', aliases: [] }

function addForm() {
  return screen.getByRole('region', { name: 'New person' })
}

describe('NamesPage', () => {
  beforeEach(() => {
    getEntriesMock.mockReset()
    createEntryMock.mockReset()
    updateEntryMock.mockReset()
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
    expect(screen.getByRole('button', { name: 'Edit Stephen Edwin King' })).toHaveFocus()
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
})
