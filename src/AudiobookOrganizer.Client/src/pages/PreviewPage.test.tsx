import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { approveBook, getPreview } from '../services/api'
import type { ApproveBookResult, ProcessingJob } from '../types/jobs'
import type { BookPreview, FilePreview, LibraryPreview } from '../types/preview'
import PreviewPage from './PreviewPage'

vi.mock('../services/api', () => ({
  getPreview: vi.fn(),
  approveBook: vi.fn(),
}))

const getPreviewMock = vi.mocked(getPreview)
const approveBookMock = vi.mocked(approveBook)

const queuedJob: ProcessingJob = {
  id: 3,
  groupId: 1,
  title: 'The Hobbit',
  status: 'Queued',
  createdAtUtc: '2026-10-19T10:00:00+00:00',
  startedAtUtc: null,
  finishedAtUtc: null,
  error: null,
  files: [],
}

function file(overrides: Partial<FilePreview> & { fileId: number }): FilePreview {
  return {
    sourcePath: `C:\\incoming\\book\\${overrides.fileId}.mp3`,
    relativePath: `Author/Book/${overrides.fileId}.mp3`,
    destinationPath: `/lib/Author/Book/${overrides.fileId}.mp3`,
    missing: [],
    error: null,
    duplicate: false,
    exists: false,
    ...overrides,
  }
}

function book(overrides: Partial<BookPreview> & { groupId: number }): BookPreview {
  return { libraryPath: '/lib', title: `Book ${overrides.groupId}`, author: 'Author', files: [], ...overrides }
}

function preview(books: BookPreview[]): LibraryPreview {
  return {
    libraryPath: '/lib',
    books,
    counts: { files: 7, missing: 1, errors: 2, duplicates: 3, existing: 4 },
  }
}

const cleanBook = book({
  groupId: 1,
  title: 'The Hobbit',
  files: [
    file({
      fileId: 10,
      sourcePath: 'C:\\incoming\\hobbit\\01.mp3',
      relativePath: 'Tolkien/The Hobbit/001. The Hobbit.mp3',
      destinationPath: '/lib/Tolkien/The Hobbit/001. The Hobbit.mp3',
    }),
    file({
      fileId: 11,
      sourcePath: '/incoming/hobbit/02.mp3',
      relativePath: 'Tolkien/The Hobbit/002. The Hobbit.mp3',
      destinationPath: '/lib/Tolkien/The Hobbit/002. The Hobbit.mp3',
    }),
  ],
})

const problemBook = book({
  groupId: 2,
  title: null,
  files: [
    file({ fileId: 20, missing: ['series', 'year'] }),
    file({ fileId: 21, duplicate: true }),
    file({ fileId: 22, exists: true }),
    file({
      fileId: 23,
      relativePath: null,
      destinationPath: null,
      error: 'The template renders an empty path.',
    }),
  ],
})

function renderPage() {
  return render(
    <MemoryRouter>
      <PreviewPage />
    </MemoryRouter>,
  )
}

function bookSection(name: string) {
  const heading = screen.getByRole('heading', { level: 2, name })
  return heading.closest('section') as HTMLElement
}

describe('PreviewPage', () => {
  beforeEach(() => {
    getPreviewMock.mockReset()
    approveBookMock.mockReset()
  })

  it('shows loading, then the heading, library path and counts', async () => {
    getPreviewMock.mockResolvedValue(preview([cleanBook]))
    renderPage()

    expect(screen.getByText('Loading preview…')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Preview' })).toBeInTheDocument()
    expect(screen.getByText(/nothing has been moved/i)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings')

    const terms = (await screen.findAllByRole('term')).map((t) => t.textContent)
    expect(terms).toEqual(['Library', 'Files', 'Missing values', 'Errors', 'Duplicate targets', 'Already exist'])
    const values = screen.getAllByRole('definition').map((d) => d.textContent)
    expect(values).toEqual(['/lib', '7', '1', '2', '3', '4'])
  })

  it('lists the books in order with their files from and to, linked to the book page', async () => {
    getPreviewMock.mockResolvedValue(preview([cleanBook, problemBook]))
    renderPage()

    const headings = await screen.findAllByRole('heading', { level: 2 })
    expect(headings.map((h) => h.textContent)).toEqual(['The Hobbit', 'Unknown'])
    expect(screen.getByRole('link', { name: 'The Hobbit' })).toHaveAttribute('href', '/books/1')
    expect(screen.getByRole('link', { name: 'Unknown' })).toHaveAttribute('href', '/books/2')

    const items = within(bookSection('The Hobbit')).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('From')
    expect(items[0]).toHaveTextContent('01.mp3')
    expect(items[0]).toHaveTextContent('C:\\incoming\\hobbit\\01.mp3')
    expect(items[0]).toHaveTextContent('To')
    expect(items[0]).toHaveTextContent('Tolkien/The Hobbit/001. The Hobbit.mp3')
    expect(items[1]).toHaveTextContent('02.mp3')
    expect(items[1]).toHaveTextContent('/incoming/hobbit/02.mp3')
    expect(items[1]).toHaveTextContent('Tolkien/The Hobbit/002. The Hobbit.mp3')
  })

  it('shows each problem as text beside its file', async () => {
    getPreviewMock.mockResolvedValue(preview([problemBook]))
    renderPage()

    await screen.findByRole('heading', { level: 2, name: 'Unknown' })
    const items = within(bookSection('Unknown')).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Missing: series, year')
    expect(items[1]).toHaveTextContent('Duplicate target')
    expect(items[2]).toHaveTextContent('Already exists')
    expect(items[3]).toHaveTextContent('The template renders an empty path.')
    expect(items[3]).toHaveTextContent('No destination')
    expect(items[0]).not.toHaveTextContent('Duplicate target')
  })

  it('hides books without problems when the filter is ticked', async () => {
    getPreviewMock.mockResolvedValue(preview([cleanBook, problemBook]))
    renderPage()

    await screen.findByRole('heading', { level: 2, name: 'The Hobbit' })
    expect(screen.getByRole('status')).toHaveTextContent('2 of 2 books shown')

    fireEvent.click(screen.getByRole('checkbox', { name: 'Only show books with problems' }))

    expect(screen.queryByRole('heading', { level: 2, name: 'The Hobbit' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Unknown' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('1 of 2 books shown')
  })

  it('says when no book has problems under the filter', async () => {
    getPreviewMock.mockResolvedValue(preview([cleanBook]))
    renderPage()

    await screen.findByRole('heading', { level: 2, name: 'The Hobbit' })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Only show books with problems' }))

    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('No books have problems.')
  })

  it('shows the empty state with a link to the dashboard', async () => {
    getPreviewMock.mockResolvedValue(preview([]))
    renderPage()

    expect(await screen.findByText('No books to preview yet.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /dashboard/i })).toHaveAttribute('href', '/dashboard')
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('shows an alert when the preview fails to load', async () => {
    getPreviewMock.mockRejectedValue(new Error('boom'))
    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent("The preview couldn't be loaded.")
  })

  it('shows Approve on a clean book and the fix note instead on a book with problems', async () => {
    getPreviewMock.mockResolvedValue(preview([cleanBook, problemBook]))
    renderPage()

    await screen.findByRole('heading', { level: 2, name: 'The Hobbit' })
    const clean = within(bookSection('The Hobbit'))
    const approve = clean.getByRole('button', { name: 'Approve' })
    expect(approve).toHaveAccessibleDescription('The Hobbit')
    expect(clean.queryByText('Fix the problems above to approve this book.')).not.toBeInTheDocument()

    const problem = within(bookSection('Unknown'))
    expect(problem.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(problem.getByText('Fix the problems above to approve this book.')).toBeInTheDocument()
  })

  it('approves the book, showing Approving… while pending, then focuses the queued message', async () => {
    let resolve: (result: ApproveBookResult) => void = () => {}
    approveBookMock.mockReturnValue(new Promise((r) => (resolve = r)))
    getPreviewMock.mockResolvedValue(preview([cleanBook]))
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(approveBookMock).toHaveBeenCalledWith(1)
    expect(screen.getByRole('button', { name: 'Approving…' })).toBeDisabled()

    await act(async () => resolve({ ok: true, job: queuedJob }))

    const queued = screen.getByText('Queued for processing.')
    expect(queued).toHaveAttribute('role', 'status')
    expect(queued).toHaveFocus()
    expect(screen.queryByRole('button', { name: /approv/i })).not.toBeInTheDocument()
  })

  it('shows a refusal in an alert and keeps the button enabled', async () => {
    approveBookMock.mockResolvedValue({ ok: false, message: 'A file is already at X.' })
    getPreviewMock.mockResolvedValue(preview([cleanBook]))
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('A file is already at X.')
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled()
  })

  it('shows a generic alert when approving throws', async () => {
    approveBookMock.mockRejectedValue(new Error('boom'))
    getPreviewMock.mockResolvedValue(preview([cleanBook]))
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("The book couldn't be approved.")
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled()
  })
})
