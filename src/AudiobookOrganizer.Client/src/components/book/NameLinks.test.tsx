import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createEntry, getBookGenres, getBookPeople, updateEntry } from '../../services/api'
import type { BookGenres, BookPeople, CanonicalEntry } from '../../types/names'
import NameLinks from './NameLinks'

vi.mock('../../services/api', () => ({
  createEntry: vi.fn(),
  getBookGenres: vi.fn(),
  getBookPeople: vi.fn(),
  updateEntry: vi.fn(),
}))

const getBookPeopleMock = vi.mocked(getBookPeople)
const getBookGenresMock = vi.mocked(getBookGenres)
const updateEntryMock = vi.mocked(updateEntry)
const createEntryMock = vi.mocked(createEntry)

const ann: CanonicalEntry = { id: 1, name: 'Ann Author', aliases: ['Author, Ann'] }
const narrator: CanonicalEntry = { id: 2, name: 'N. Narrator', aliases: ['Nia N.'] }

const unlinked: BookPeople = {
  groupId: 4,
  authors: [{ name: 'Author, Ann', entry: ann, suggestions: [] }],
  narrators: [
    {
      name: 'Nia Narrator',
      entry: null,
      suggestions: [{ entry: narrator, reason: 'Matching initials' }],
    },
  ],
}

const linked: BookPeople = {
  ...unlinked,
  narrators: [{ name: 'Nia Narrator', entry: { ...narrator, aliases: ['Nia N.', 'Nia Narrator'] }, suggestions: [] }],
}

function renderLinks() {
  return render(<NameLinks kind="people" groupId={4} reloadKey={0} />)
}

function row(name: string) {
  return screen.getByText(name).closest('li')!
}

describe('NameLinks (people)', () => {
  beforeEach(() => {
    getBookPeopleMock.mockReset()
    getBookGenresMock.mockReset()
    updateEntryMock.mockReset()
    createEntryMock.mockReset()
  })

  it('shows a resolved author as normalized and an unresolved narrator with its suggestion and reason', async () => {
    getBookPeopleMock.mockResolvedValue(unlinked)
    renderLinks()

    const section = (await screen.findByRole('heading', { level: 2, name: 'People' })).closest('section')!
    expect(section).toHaveAttribute('aria-labelledby')
    expect(await screen.findByText('Normalized: Ann Author')).toBeInTheDocument()
    expect(within(screen.getByRole('heading', { level: 3, name: 'Authors' }).parentElement!).getByText('Author, Ann')).toBeInTheDocument()
    const narratorRow = row('Nia Narrator')
    expect(within(narratorRow).getByText('Not linked')).toBeInTheDocument()
    expect(within(narratorRow).getByText('N. Narrator (Matching initials)')).toBeInTheDocument()
    expect(within(narratorRow).getByRole('button', { name: 'Link Nia Narrator to N. Narrator' })).toBeInTheDocument()
    expect(within(narratorRow).getByRole('button', { name: 'Add Nia Narrator as a new person' })).toBeInTheDocument()
    expect(getBookPeopleMock).toHaveBeenCalledWith(4)
  })

  it('says when the book has no authors or narrators', async () => {
    getBookPeopleMock.mockResolvedValue({ groupId: 4, authors: [], narrators: [] })
    renderLinks()
    expect(await screen.findByText('No authors or narrators yet.')).toBeInTheDocument()
  })

  it('says when the people links fail to load', async () => {
    getBookPeopleMock.mockRejectedValue(new Error('boom'))
    renderLinks()
    expect(await screen.findByText("People links couldn't be loaded.")).toBeInTheDocument()
  })

  it('links a name to a suggested person as an alias, reloads, announces it and focuses the heading', async () => {
    getBookPeopleMock.mockResolvedValueOnce(unlinked).mockResolvedValueOnce(linked)
    updateEntryMock.mockResolvedValue({ ok: true, entry: linked.narrators[0].entry! })
    renderLinks()

    fireEvent.click(await screen.findByRole('button', { name: 'Link Nia Narrator to N. Narrator' }))

    expect(await screen.findByText('Normalized: N. Narrator')).toBeInTheDocument()
    expect(updateEntryMock).toHaveBeenCalledWith('people', 2, { name: 'N. Narrator', aliases: ['Nia N.', 'Nia Narrator'] })
    expect(getBookPeopleMock).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('status')).toHaveTextContent('Nia Narrator linked to N. Narrator.')
    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'People' })).toHaveFocus())
  })

  it('adds a name as a new person', async () => {
    const created: CanonicalEntry = { id: 9, name: 'Nia Narrator', aliases: [] }
    getBookPeopleMock
      .mockResolvedValueOnce(unlinked)
      .mockResolvedValueOnce({ ...unlinked, narrators: [{ name: 'Nia Narrator', entry: created, suggestions: [] }] })
    createEntryMock.mockResolvedValue({ ok: true, entry: created })
    renderLinks()

    fireEvent.click(await screen.findByRole('button', { name: 'Add Nia Narrator as a new person' }))

    expect(await screen.findByText('Normalized: Nia Narrator')).toBeInTheDocument()
    expect(createEntryMock).toHaveBeenCalledWith('people', { name: 'Nia Narrator', aliases: [] })
    expect(screen.getByRole('status')).toHaveTextContent('Nia Narrator linked to Nia Narrator.')
  })

  it('shows a 400 message as an alert on the row, keeps the row and does not reload', async () => {
    getBookPeopleMock.mockResolvedValue(unlinked)
    createEntryMock.mockResolvedValue({ ok: false, errors: { aliases: ['"Nia Narrator" already belongs to Other Person.'] } })
    renderLinks()

    const button = await screen.findByRole('button', { name: 'Add Nia Narrator as a new person' })
    button.focus()
    fireEvent.click(button)

    const alert = await within(row('Nia Narrator')).findByRole('alert')
    expect(alert).toHaveTextContent('"Nia Narrator" already belongs to Other Person.')
    expect(within(row('Nia Narrator')).getByText('Not linked')).toBeInTheDocument()
    expect(getBookPeopleMock).toHaveBeenCalledTimes(1)
    expect(button).toBeEnabled()
    expect(button).toHaveFocus()
  })

  it('says the person no longer exists on a 404', async () => {
    getBookPeopleMock.mockResolvedValue(unlinked)
    updateEntryMock.mockResolvedValue({ ok: false, notFound: true })
    renderLinks()

    fireEvent.click(await screen.findByRole('button', { name: 'Link Nia Narrator to N. Narrator' }))

    expect(await within(row('Nia Narrator')).findByRole('alert')).toHaveTextContent('That person no longer exists.')
  })

  it('shows a connection message when the request throws', async () => {
    getBookPeopleMock.mockResolvedValue(unlinked)
    updateEntryMock.mockRejectedValue(new Error('offline'))
    renderLinks()

    fireEvent.click(await screen.findByRole('button', { name: 'Link Nia Narrator to N. Narrator' }))

    expect(await within(row('Nia Narrator')).findByRole('alert')).toHaveTextContent(/check the connection/i)
  })

  it('disables every row button while an action runs', async () => {
    getBookPeopleMock.mockResolvedValue(unlinked)
    updateEntryMock.mockReturnValue(new Promise(() => {}))
    renderLinks()

    fireEvent.click(await screen.findByRole('button', { name: 'Link Nia Narrator to N. Narrator' }))

    expect(screen.getByRole('button', { name: 'Link Nia Narrator to N. Narrator' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Add Nia Narrator as a new person' })).toBeDisabled()
  })
})

const scienceFiction: CanonicalEntry = { id: 5, name: 'Science Fiction', aliases: ['SF'] }

const genresUnlinked: BookGenres = {
  groupId: 4,
  genres: [
    { name: 'Sciencefiction', entry: null, suggestions: [{ entry: scienceFiction, reason: 'Same letters' }] },
    { name: 'Horror', entry: null, suggestions: [] },
  ],
}

function renderGenres() {
  return render(<NameLinks kind="genres" groupId={4} reloadKey={0} />)
}

describe('NameLinks (genres)', () => {
  beforeEach(() => {
    getBookPeopleMock.mockReset()
    getBookGenresMock.mockReset()
    updateEntryMock.mockReset()
    createEntryMock.mockReset()
  })

  it('shows a resolved genre as normalized and an unresolved one with its suggestion and reason, in one list', async () => {
    getBookGenresMock.mockResolvedValue({
      groupId: 4,
      genres: [{ name: 'Sci Fi', entry: scienceFiction, suggestions: [] }, ...genresUnlinked.genres],
    })
    renderGenres()

    const section = (await screen.findByRole('heading', { level: 2, name: 'Genres' })).closest('section')!
    expect(section).toHaveAttribute('aria-labelledby', 'genres-heading')
    expect(await screen.findByText('Normalized: Science Fiction')).toBeInTheDocument()
    expect(within(section).queryByRole('heading', { level: 3 })).not.toBeInTheDocument()
    const sciRow = row('Sciencefiction')
    expect(within(sciRow).getByText('Not linked')).toBeInTheDocument()
    expect(within(sciRow).getByText('Science Fiction (Same letters)')).toBeInTheDocument()
    expect(within(sciRow).getByRole('button', { name: 'Link Sciencefiction to Science Fiction' })).toBeInTheDocument()
    expect(within(row('Horror')).getByRole('button', { name: 'Add Horror as a new genre' })).toHaveTextContent('Add as new genre')
    expect(getBookGenresMock).toHaveBeenCalledWith(4)
    expect(getBookPeopleMock).not.toHaveBeenCalled()
  })

  it('says when the book has no genres', async () => {
    getBookGenresMock.mockResolvedValue({ groupId: 4, genres: [] })
    renderGenres()
    expect(await screen.findByText('No genres yet.')).toBeInTheDocument()
  })

  it('links a genre to a suggested genre as an alias, reloads and announces it', async () => {
    const linkedEntry: CanonicalEntry = { ...scienceFiction, aliases: ['SF', 'Sciencefiction'] }
    getBookGenresMock.mockResolvedValueOnce(genresUnlinked).mockResolvedValueOnce({
      groupId: 4,
      genres: [{ name: 'Sciencefiction', entry: linkedEntry, suggestions: [] }, genresUnlinked.genres[1]],
    })
    updateEntryMock.mockResolvedValue({ ok: true, entry: linkedEntry })
    renderGenres()

    fireEvent.click(await screen.findByRole('button', { name: 'Link Sciencefiction to Science Fiction' }))

    expect(await screen.findByText('Normalized: Science Fiction')).toBeInTheDocument()
    expect(updateEntryMock).toHaveBeenCalledWith('genres', 5, { name: 'Science Fiction', aliases: ['SF', 'Sciencefiction'] })
    expect(getBookGenresMock).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('status')).toHaveTextContent('Sciencefiction linked to Science Fiction.')
    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'Genres' })).toHaveFocus())
  })

  it('adds a genre as a new genre', async () => {
    const created: CanonicalEntry = { id: 9, name: 'Horror', aliases: [] }
    getBookGenresMock.mockResolvedValueOnce(genresUnlinked).mockResolvedValueOnce({
      groupId: 4,
      genres: [genresUnlinked.genres[0], { name: 'Horror', entry: created, suggestions: [] }],
    })
    createEntryMock.mockResolvedValue({ ok: true, entry: created })
    renderGenres()

    fireEvent.click(await screen.findByRole('button', { name: 'Add Horror as a new genre' }))

    expect(await screen.findByText('Normalized: Horror')).toBeInTheDocument()
    expect(createEntryMock).toHaveBeenCalledWith('genres', { name: 'Horror', aliases: [] })
    expect(screen.getByRole('status')).toHaveTextContent('Horror linked to Horror.')
  })

  it('says the genre no longer exists on a 404', async () => {
    getBookGenresMock.mockResolvedValue(genresUnlinked)
    updateEntryMock.mockResolvedValue({ ok: false, notFound: true })
    renderGenres()

    fireEvent.click(await screen.findByRole('button', { name: 'Link Sciencefiction to Science Fiction' }))

    expect(await within(row('Sciencefiction')).findByRole('alert')).toHaveTextContent('That genre no longer exists.')
  })

  it('says when the genre links fail to load', async () => {
    getBookGenresMock.mockRejectedValue(new Error('boom'))
    renderGenres()
    expect(await screen.findByText("Genre links couldn't be loaded.")).toBeInTheDocument()
  })
})
