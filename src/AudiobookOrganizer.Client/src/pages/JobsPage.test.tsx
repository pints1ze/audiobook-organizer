import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { approveBook, getJobs } from '../services/api'
import type { ProcessingJob, ProcessingJobFile } from '../types/jobs'
import { formatDateTime } from '../utils/formatDateTime'
import JobsPage from './JobsPage'

vi.mock('../services/api', () => ({
  getJobs: vi.fn(),
  approveBook: vi.fn(),
}))

const getJobsMock = vi.mocked(getJobs)
const approveBookMock = vi.mocked(approveBook)

function jobFile(overrides: Partial<ProcessingJobFile> & { fileId: number }): ProcessingJobFile {
  return {
    sourcePath: `/incoming/book/${overrides.fileId}.mp3`,
    destinationPath: `/lib/Author/Book/${overrides.fileId}.mp3`,
    status: 'Pending',
    error: null,
    ...overrides,
  }
}

function job(overrides: Partial<ProcessingJob> & { id: number }): ProcessingJob {
  return {
    groupId: 1,
    title: 'The Hobbit',
    status: 'Queued',
    createdAtUtc: '2026-10-19T10:00:00+00:00',
    startedAtUtc: null,
    finishedAtUtc: null,
    error: null,
    files: [],
    ...overrides,
  }
}

const succeededJob = job({
  id: 7,
  groupId: 3,
  title: 'Dune',
  status: 'Succeeded',
  startedAtUtc: '2026-10-19T11:00:01+00:00',
  finishedAtUtc: '2026-10-19T11:00:09+00:00',
  files: [
    jobFile({
      fileId: 30,
      destinationPath: '/lib/Herbert/Dune/01 - Dune.mp3',
      status: 'SourceDeleted',
    }),
  ],
})

const failedJob = job({
  id: 6,
  groupId: null,
  title: null,
  status: 'Failed',
  startedAtUtc: '2026-10-19T10:30:01+00:00',
  finishedAtUtc: '2026-10-19T10:30:05+00:00',
  error: 'A file is already at /lib/Tolkien/The Hobbit/02 - The Hobbit.mp3.',
  files: [
    jobFile({ fileId: 20, destinationPath: '/lib/Tolkien/The Hobbit/01 - The Hobbit.mp3' }),
    jobFile({
      fileId: 21,
      destinationPath: '/lib/Tolkien/The Hobbit/02 - The Hobbit.mp3',
      status: 'Failed',
      error: 'A file is already at /lib/Tolkien/The Hobbit/02 - The Hobbit.mp3.',
    }),
  ],
})

function renderPage() {
  return render(
    <MemoryRouter>
      <JobsPage />
    </MemoryRouter>,
  )
}

/** Settles pending promises and runs timers due within `ms`, under fake timers. */
async function advance(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

function jobSection(heading: HTMLElement) {
  return heading.closest('section') as HTMLElement
}

function valueOf(section: HTMLElement, term: string) {
  return within(section).getByText(term, { selector: 'dt' }).nextElementSibling as HTMLElement
}

describe('JobsPage', () => {
  beforeEach(() => {
    getJobsMock.mockReset()
    approveBookMock.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows each job with its title, status, times, error and files', async () => {
    getJobsMock.mockResolvedValue([succeededJob, failedJob])
    renderPage()

    expect(screen.getByRole('heading', { level: 1, name: 'Jobs' })).toBeInTheDocument()
    expect(screen.getByText('Loading jobs…')).toBeInTheDocument()

    const dune = await screen.findByRole('heading', { level: 2, name: 'Dune' })
    expect(within(dune).getByRole('link', { name: 'Dune' })).toHaveAttribute('href', '/books/3')
    const duneSection = jobSection(dune)
    expect(screen.getByRole('region', { name: 'Dune' })).toBe(duneSection)
    expect(valueOf(duneSection, 'Status')).toHaveTextContent('Succeeded')
    expect(valueOf(duneSection, 'Created')).toHaveTextContent(formatDateTime(succeededJob.createdAtUtc))
    expect(valueOf(duneSection, 'Finished')).toHaveTextContent(formatDateTime(succeededJob.finishedAtUtc))
    const duneFile = within(duneSection).getByRole('listitem')
    expect(duneFile).toHaveTextContent('01 - Dune.mp3')
    expect(duneFile).toHaveTextContent('/lib/Herbert/Dune/01 - Dune.mp3')
    expect(duneFile).toHaveTextContent('Status: SourceDeleted')

    const unknown = screen.getByRole('heading', { level: 2, name: 'Unknown' })
    expect(within(unknown).queryByRole('link')).not.toBeInTheDocument()
    const failedSection = jobSection(unknown)
    expect(valueOf(failedSection, 'Status')).toHaveTextContent('Failed')
    expect(within(failedSection).getAllByText(failedJob.error!)).toHaveLength(2)
    const files = within(failedSection).getAllByRole('listitem')
    expect(files).toHaveLength(2)
    expect(files[0]).toHaveTextContent('01 - The Hobbit.mp3')
    expect(files[0]).toHaveTextContent('Status: Pending')
    expect(files[1]).toHaveTextContent('/lib/Tolkien/The Hobbit/02 - The Hobbit.mp3')
    expect(files[1]).toHaveTextContent('Status: Failed')
    expect(within(files[1]).getByText(failedJob.files[1].error!)).toBeInTheDocument()
    // The group is gone, so there's nothing to retry.
    expect(within(failedSection).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })

  it('says there are no jobs yet and links to the preview', async () => {
    getJobsMock.mockResolvedValue([])
    renderPage()

    expect(await screen.findByText(/No jobs yet\./)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Approve books on the Preview page' })).toHaveAttribute('href', '/preview')
  })

  it('shows an alert when the jobs fail to load', async () => {
    getJobsMock.mockRejectedValue(new Error('boom'))
    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent("The jobs couldn't be loaded.")
  })

  it('reloads every 2 seconds while a job is running and stops once all are finished', async () => {
    vi.useFakeTimers()
    const running = job({ id: 8, status: 'Running', startedAtUtc: '2026-10-19T12:00:00+00:00' })
    getJobsMock
      .mockResolvedValueOnce([running, succeededJob])
      .mockResolvedValueOnce([running, succeededJob])
      .mockResolvedValue([{ ...running, status: 'Succeeded', finishedAtUtc: '2026-10-19T12:00:05+00:00' }, succeededJob])

    renderPage()
    await advance()
    expect(getJobsMock).toHaveBeenCalledTimes(1)
    const heading = screen.getByRole('heading', { level: 2, name: 'The Hobbit' })
    expect(valueOf(jobSection(heading), 'Status')).toHaveTextContent('Running')
    const link = within(heading).getByRole('link')
    link.focus()

    await advance(1999)
    expect(getJobsMock).toHaveBeenCalledTimes(1)
    await advance(1)
    expect(getJobsMock).toHaveBeenCalledTimes(2)
    await advance(2000)
    expect(getJobsMock).toHaveBeenCalledTimes(3)
    expect(valueOf(jobSection(screen.getByRole('heading', { level: 2, name: 'The Hobbit' })), 'Status')).toHaveTextContent(
      'Succeeded',
    )
    expect(link).toHaveFocus()

    await advance(10000)
    expect(getJobsMock).toHaveBeenCalledTimes(3)
  })

  it('does not poll when every job is finished, and stops polling on unmount', async () => {
    vi.useFakeTimers()
    getJobsMock.mockResolvedValue([succeededJob])
    const { unmount } = renderPage()
    await advance(10000)
    expect(getJobsMock).toHaveBeenCalledTimes(1)
    unmount()

    getJobsMock.mockReset()
    getJobsMock.mockResolvedValue([job({ id: 9, status: 'Queued' })])
    const second = renderPage()
    await advance()
    expect(getJobsMock).toHaveBeenCalledTimes(1)
    second.unmount()
    await advance(10000)
    expect(getJobsMock).toHaveBeenCalledTimes(1)
  })

  it("offers Retry only on a book's newest failed job and queues it again", async () => {
    const newestFailed = { ...failedJob, id: 12, groupId: 1, title: 'The Hobbit' }
    const olderFailed = { ...failedJob, id: 5, groupId: 1, title: 'The Hobbit' }
    const queuedAgain = job({ id: 13, groupId: 1, status: 'Queued' })
    getJobsMock.mockResolvedValueOnce([newestFailed, succeededJob, olderFailed]).mockResolvedValue([
      { ...queuedAgain, status: 'Succeeded', finishedAtUtc: '2026-10-19T13:00:00+00:00' },
      newestFailed,
      succeededJob,
      olderFailed,
    ])
    approveBookMock.mockResolvedValue({ ok: true, job: queuedAgain })
    renderPage()

    const headings = await screen.findAllByRole('heading', { level: 2, name: 'The Hobbit' })
    expect(headings).toHaveLength(2)
    const retry = screen.getByRole('button', { name: 'Retry' })
    expect(jobSection(headings[0])).toContainElement(retry)
    expect(retry).toHaveAttribute('aria-describedby', headings[0].id)
    expect(screen.getAllByRole('button')).toHaveLength(1)

    fireEvent.click(retry)
    expect(retry).toBeDisabled()

    const status = await screen.findByRole('status')
    expect(status).toHaveTextContent('The Hobbit is queued again.')
    expect(status).toHaveFocus()
    expect(approveBookMock).toHaveBeenCalledWith(1)
    expect(getJobsMock).toHaveBeenCalledTimes(2)
    expect(await screen.findAllByRole('heading', { level: 2, name: 'The Hobbit' })).toHaveLength(3)
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })

  it('focuses the status line again when a second book with the same title is queued', async () => {
    const firstFailed = { ...failedJob, id: 12, groupId: 1, title: 'The Hobbit' }
    const secondFailed = { ...failedJob, id: 11, groupId: 2, title: 'The Hobbit' }
    getJobsMock.mockResolvedValue([firstFailed, secondFailed])
    approveBookMock.mockResolvedValue({ ok: true, job: job({ id: 13, groupId: 1 }) })
    renderPage()

    const [firstRetry, secondRetry] = await screen.findAllByRole('button', { name: 'Retry' })
    fireEvent.click(firstRetry)
    const status = await screen.findByRole('status')
    expect(status).toHaveTextContent('The Hobbit is queued again.')
    expect(status).toHaveFocus()

    secondRetry.focus()
    fireEvent.click(secondRetry)

    await vi.waitFor(() => expect(approveBookMock).toHaveBeenCalledWith(2))
    await vi.waitFor(() => expect(screen.getByRole('status')).toHaveFocus())
    expect(screen.getByRole('status')).toHaveTextContent('The Hobbit is queued again.')
  })

  it('shows a refused retry in an alert beside the button', async () => {
    getJobsMock.mockResolvedValue([{ ...failedJob, groupId: 1, title: 'The Hobbit' }])
    approveBookMock.mockResolvedValue({ ok: false, message: 'A file is already at Tolkien/The Hobbit/02.mp3.' })
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('A file is already at Tolkien/The Hobbit/02.mp3.')
    expect(jobSection(screen.getByRole('heading', { level: 2, name: 'The Hobbit' }))).toContainElement(alert)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeEnabled()
    expect(getJobsMock).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows an alert when the retry request fails', async () => {
    getJobsMock.mockResolvedValue([{ ...failedJob, groupId: 1, title: 'The Hobbit' }])
    approveBookMock.mockRejectedValue(new Error('boom'))
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("The book couldn't be queued again.")
  })
})
