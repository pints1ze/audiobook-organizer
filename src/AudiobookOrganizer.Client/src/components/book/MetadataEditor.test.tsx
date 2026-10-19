import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getMetadata, saveMetadata } from '../../services/api'
import type { BookMetadata, MetadataCandidate, MetadataField, SaveMetadataResult } from '../../types/metadata'
import MetadataEditor from './MetadataEditor'

vi.mock('../../services/api', () => ({
  getMetadata: vi.fn(),
  saveMetadata: vi.fn(),
}))

const getMetadataMock = vi.mocked(getMetadata)
const saveMetadataMock = vi.mocked(saveMetadata)

function field<T>(value: T, origin: MetadataField<T>['origin'] = null, provider: string | null = null): MetadataField<T> {
  return { value, origin, provider }
}

const metadata: BookMetadata = {
  groupId: 4,
  fields: {
    title: field<string | null>('Project Hail Mary', 'provider', 'audible'),
    subtitle: field<string | null>(null),
    authors: field(['Andy Weir'], 'file'),
    narrators: field(['Jane Doe', 'John Roe'], 'user'),
    series: field<string | null>('Hail Mary', 'provider', 'openlibrary'),
    seriesPart: field<string | null>('1', 'provider', 'audible'),
    publisher: field<string | null>('Audible Studios', 'provider', 'audible'),
    releaseDate: field<string | null>('2021-05-04', 'provider', 'audible'),
    language: field<string | null>('english', 'provider', 'audible'),
    format: field<string | null>(null),
    asin: field<string | null>('B08G9PRS1K', 'provider', 'audible'),
    isbn: field<string | null>(null),
  },
}

const match: MetadataCandidate = {
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
  format: null,
  coverUrl: null,
  isbn: null,
}

function renderEditor(candidate: MetadataCandidate | null = null) {
  return render(
    <MemoryRouter>
      <MetadataEditor groupId={4} match={candidate} reloadKey={0} />
    </MemoryRouter>,
  )
}

/** The dd of the row labelled `label`. */
function row(label: string) {
  const dt = screen.getAllByRole('term').find((term) => term.textContent === label)!
  return dt.nextElementSibling as HTMLElement
}

async function loaded(candidate: MetadataCandidate | null = null) {
  getMetadataMock.mockResolvedValue(metadata)
  renderEditor(candidate)
  await screen.findByText('Andy Weir')
}

describe('MetadataEditor', () => {
  beforeEach(() => {
    getMetadataMock.mockReset()
    saveMetadataMock.mockReset()
  })

  it('shows each field with its origin label and "Not set" for empty fields', async () => {
    await loaded()

    expect(getMetadataMock).toHaveBeenCalledWith(4)
    expect(screen.getByRole('heading', { level: 2, name: 'Metadata' })).toBeInTheDocument()
    expect(screen.getAllByRole('term')).toHaveLength(12)
    expect(row('Title')).toHaveTextContent('Project Hail Mary')
    expect(row('Title')).toHaveTextContent('From Audible')
    expect(row('Authors')).toHaveTextContent('Andy Weir')
    expect(row('Authors')).toHaveTextContent('From files')
    expect(row('Narrators')).toHaveTextContent('Jane Doe, John Roe')
    expect(row('Narrators')).toHaveTextContent('Edited by you')
    expect(row('Series')).toHaveTextContent('Hail Mary')
    expect(row('Series')).toHaveTextContent('From Open Library')
    expect(row('Series part')).toHaveTextContent('1')
    expect(row('Subtitle')).toHaveTextContent('Not set')
    expect(row('Format')).toHaveTextContent(/^Not set$/)
    expect(screen.queryByText(/Match:/)).not.toBeInTheDocument()
  })

  it('shows "Match: Ray Porter" for a user narrator that differs from the match', async () => {
    await loaded(match)

    expect(row('Narrators')).toHaveTextContent('Match: Ray Porter')
    // The match has a subtitle the book doesn't, and no format: only differing values the match has show.
    expect(row('Subtitle')).toHaveTextContent('Match: A Novel')
    expect(within(row('Title')).queryByText(/Match:/)).not.toBeInTheDocument()
    expect(within(row('Format')).queryByText(/Match:/)).not.toBeInTheDocument()
    expect(within(row('Series part')).queryByText(/Match:/)).not.toBeInTheDocument()
  })

  it('says the metadata could not be loaded when the request fails', async () => {
    getMetadataMock.mockRejectedValue(new Error('down'))
    renderEditor()

    expect(await screen.findByText("The metadata couldn't be loaded.")).toBeInTheDocument()
  })

  it('reloads when the reload key changes', async () => {
    getMetadataMock.mockResolvedValue(metadata)
    const view = renderEditor()
    await screen.findByText('Andy Weir')

    getMetadataMock.mockResolvedValue({
      ...metadata,
      fields: { ...metadata.fields, narrators: field(['Ray Porter'], 'provider', 'audible') },
    })
    view.rerender(
      <MemoryRouter>
        <MetadataEditor groupId={4} match={null} reloadKey={1} />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Ray Porter')).toBeInTheDocument()
    expect(getMetadataMock).toHaveBeenCalledTimes(2)
  })
  it('sends only the changed fields, then shows the saved values, a status and focuses the heading', async () => {
    let resolveSave!: (value: SaveMetadataResult) => void
    saveMetadataMock.mockReturnValue(new Promise((resolve) => (resolveSave = resolve)))
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    const narrators = screen.getByLabelText('Narrators')
    expect(narrators.tagName).toBe('TEXTAREA')
    expect(narrators).toHaveValue('Jane Doe\nJohn Roe')
    expect(narrators).toHaveAccessibleDescription('One name per line')
    expect(screen.getByLabelText('Title')).toHaveValue('Project Hail Mary')

    fireEvent.change(narrators, { target: { value: 'A\n\n B \n' } })
    fireEvent.change(screen.getByLabelText('Publisher'), { target: { value: '   ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(saveMetadataMock).toHaveBeenCalledWith(4, { narrators: ['A', 'B'], publisher: '' })
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()

    resolveSave({
      ok: true,
      metadata: {
        ...metadata,
        fields: { ...metadata.fields, narrators: field(['A', 'B'], 'user'), publisher: field<string | null>(null, 'user') },
      },
    })
    expect(await screen.findByText('Metadata saved.')).toHaveAttribute('role', 'status')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(row('Narrators')).toHaveTextContent('A, B')
    expect(row('Publisher')).toHaveTextContent('Not set')
    expect(screen.getByRole('heading', { level: 2, name: 'Metadata' })).toHaveFocus()
  })

  it('closes the form without a request when nothing changed', async () => {
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: ' Project Hail Mary ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(saveMetadataMock).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit metadata' })).toBeInTheDocument()
  })

  it('discards the edits on Cancel and returns focus to "Edit metadata"', async () => {
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Something else' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(saveMetadataMock).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit metadata' })).toHaveFocus())
    expect(row('Title')).toHaveTextContent('Project Hail Mary')
    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    expect(screen.getByLabelText('Title')).toHaveValue('Project Hail Mary')
  })

  it('shows a 400 under the Title input, marks it invalid, focuses it and keeps the form open', async () => {
    saveMetadataMock.mockResolvedValue({ ok: false, errors: { title: ['The title is required.'] } })
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    const title = screen.getByLabelText('Title')
    fireEvent.change(title, { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(saveMetadataMock).toHaveBeenCalledWith(4, { title: '' })
    await waitFor(() => expect(title).toHaveAttribute('aria-invalid', 'true'))
    expect(title).toHaveAccessibleDescription('The title is required.')
    expect(title).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('shows "This book no longer exists." with a link to Incoming on a 404', async () => {
    saveMetadataMock.mockResolvedValue({ ok: false, notFound: true })
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'New' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('This book no longer exists.')
    expect(within(alert).getByRole('link', { name: 'Back to Incoming' })).toHaveAttribute('href', '/incoming')
  })

  it('shows a retryable error when the save request fails', async () => {
    saveMetadataMock.mockRejectedValueOnce(new Error('offline'))
    saveMetadataMock.mockResolvedValueOnce({ ok: true, metadata })
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata' }))
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'New' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("The metadata couldn't be saved. Check the connection and try again.")
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Metadata saved.')).toBeInTheDocument()
    expect(saveMetadataMock).toHaveBeenCalledTimes(2)
  })
})
