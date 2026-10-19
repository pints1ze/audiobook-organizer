import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getDashboard, getHealth, getLatestScan, startScan } from '../services/api'
import type { Dashboard, ScanRun } from '../types/dashboard'
import DashboardPage from './DashboardPage'

vi.mock('../services/api', () => ({
  getHealth: vi.fn(),
  getDashboard: vi.fn(),
  getLatestScan: vi.fn(),
  startScan: vi.fn(),
}))

const getHealthMock = vi.mocked(getHealth)
const getDashboardMock = vi.mocked(getDashboard)
const getLatestScanMock = vi.mocked(getLatestScan)
const startScanMock = vi.mocked(startScan)

const completedRun: ScanRun = {
  id: 1,
  startedAt: '2026-10-18T10:00:00Z',
  finishedAt: '2026-10-18T10:01:00Z',
  status: 'Completed',
  filesFound: 12,
  filesAdded: 7,
  filesUpdated: 2,
  filesUnchanged: 3,
  filesMissing: 1,
  filesFailed: 4,
  error: null,
}

const runningRun: ScanRun = {
  ...completedRun,
  id: 2,
  finishedAt: null,
  status: 'Running',
  filesFound: 5,
  filesAdded: 0,
  filesUpdated: 0,
  filesUnchanged: 0,
  filesMissing: 0,
  filesFailed: 0,
}

const dashboard: Dashboard = {
  incomingPath: '/incoming',
  libraryPath: '/library',
  lastScan: completedRun,
  audioFileCount: 42,
  filesWithErrors: 4,
}

function renderPage() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  )
}

/** The <dd> paired with the <dt> labelled `term` inside `section`. */
function valueOf(section: HTMLElement, term: string) {
  const dt = within(section).getByText(term, { selector: 'dt' })
  return dt.nextElementSibling as HTMLElement
}

function section(name: string) {
  return screen.getByRole('region', { name })
}

/** Settles pending promises and runs timers due within `ms`, under fake timers. */
async function advance(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

function scanStatus() {
  return screen.getByTestId('scan-status')
}

describe('DashboardPage', () => {
  beforeEach(() => {
    getHealthMock.mockReset()
    getDashboardMock.mockReset()
    getLatestScanMock.mockReset()
    startScanMock.mockReset()
    getHealthMock.mockResolvedValue({ status: 'ok', database: 'ok' })
    getDashboardMock.mockResolvedValue(dashboard)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the API and database status when the health check succeeds', async () => {
    renderPage()

    expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument()
    expect(await screen.findByText('API: ok')).toBeInTheDocument()
    expect(screen.getByText('Database: ok')).toBeInTheDocument()
  })

  it('shows "API unreachable" when the health check fails', async () => {
    getHealthMock.mockRejectedValue(new TypeError('Failed to fetch'))

    renderPage()

    expect(await screen.findByText('API unreachable')).toBeInTheDocument()
    expect(screen.getByTestId('health-status')).toHaveTextContent('API unreachable')
  })

  it('shows the directories, last scan counts and inventory counts', async () => {
    renderPage()

    expect(await screen.findByRole('heading', { level: 2, name: 'Directories' })).toBeInTheDocument()
    const directories = section('Directories')
    expect(valueOf(directories, 'Incoming')).toHaveTextContent('/incoming')
    expect(valueOf(directories, 'Library')).toHaveTextContent('/library')

    const lastScan = section('Last scan')
    expect(valueOf(lastScan, 'Status')).toHaveTextContent('Completed')
    expect(valueOf(lastScan, 'Finished')).not.toHaveTextContent('—')
    expect(valueOf(lastScan, 'Found')).toHaveTextContent('12')
    expect(valueOf(lastScan, 'Added')).toHaveTextContent('7')
    expect(valueOf(lastScan, 'Updated')).toHaveTextContent('2')
    expect(valueOf(lastScan, 'Unchanged')).toHaveTextContent('3')
    expect(valueOf(lastScan, 'Missing')).toHaveTextContent('1')
    expect(valueOf(lastScan, 'Failed')).toHaveTextContent('4')

    const inventory = section('Inventory')
    expect(within(inventory).getByText('From the last completed scan')).toBeInTheDocument()
    expect(valueOf(inventory, 'Audio files')).toHaveTextContent('42')
    expect(valueOf(inventory, 'Files with errors')).toHaveTextContent('4')

    expect(screen.getByRole('button', { name: 'Scan incoming' })).toBeEnabled()
  })

  it('shows the error text of a failed last scan', async () => {
    getDashboardMock.mockResolvedValue({
      ...dashboard,
      lastScan: { ...completedRun, status: 'Failed', error: 'Incoming directory not found.' },
    })

    renderPage()

    expect(await screen.findByText(/Incoming directory not found\./)).toBeInTheDocument()
    expect(valueOf(section('Last scan'), 'Status')).toHaveTextContent('Failed')
  })

  it('shows "No scans yet." when there is no last scan', async () => {
    getDashboardMock.mockResolvedValue({ ...dashboard, lastScan: null, audioFileCount: 0, filesWithErrors: 0 })

    renderPage()

    expect(await screen.findByText('No scans yet.')).toBeInTheDocument()
  })

  it('shows "Not set" with a Settings link and disables the scan button when incoming is not set', async () => {
    getDashboardMock.mockResolvedValue({ ...dashboard, incomingPath: null, libraryPath: null, lastScan: null })

    renderPage()

    await screen.findByRole('heading', { level: 2, name: 'Directories' })
    const directories = section('Directories')
    const incoming = valueOf(directories, 'Incoming')
    expect(incoming).toHaveTextContent('Not set')
    expect(within(incoming).getByRole('link', { name: 'Set it in Settings' })).toHaveAttribute('href', '/settings')
    expect(valueOf(directories, 'Library')).toHaveTextContent('Not set')

    const button = screen.getByRole('button', { name: 'Scan incoming' })
    expect(button).toBeDisabled()
    expect(button).toHaveAccessibleDescription('Set the incoming directory in Settings first.')
  })

  it('shows an alert when the dashboard fails to load', async () => {
    getDashboardMock.mockRejectedValue(new Error('boom'))

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent("The dashboard couldn't be loaded.")
  })

  it('starts a scan, shows progress while polling and announces completion', async () => {
    vi.useFakeTimers()
    startScanMock.mockResolvedValue({ started: true, run: { ...runningRun, filesFound: 0 } })
    getLatestScanMock.mockResolvedValueOnce(runningRun).mockResolvedValueOnce({ ...completedRun, id: 2 })

    renderPage()
    await advance()
    expect(getDashboardMock).toHaveBeenCalledTimes(1)

    const button = screen.getByRole('button', { name: 'Scan incoming' })
    button.focus()
    fireEvent.click(button)
    await advance()

    expect(startScanMock).toHaveBeenCalledTimes(1)
    expect(button).toHaveAttribute('aria-disabled', 'true')
    expect(button).toHaveFocus()

    await advance(1000)
    expect(getLatestScanMock).toHaveBeenCalledTimes(1)
    expect(scanStatus()).toHaveAttribute('role', 'status')
    expect(scanStatus()).toHaveTextContent('Scanning… 5 files found')

    getDashboardMock.mockResolvedValue({ ...dashboard, audioFileCount: 50 })
    await advance(1000)
    expect(getLatestScanMock).toHaveBeenCalledTimes(2)
    expect(getDashboardMock).toHaveBeenCalledTimes(2)
    expect(scanStatus()).toHaveTextContent('Scan completed')
    expect(valueOf(section('Inventory'), 'Audio files')).toHaveTextContent('50')
    expect(button).not.toHaveAttribute('aria-disabled')
    expect(button).toHaveFocus()

    await advance(5000)
    expect(getLatestScanMock).toHaveBeenCalledTimes(2)
  })

  it('announces a failed scan with its error', async () => {
    vi.useFakeTimers()
    startScanMock.mockResolvedValue({ started: true, run: runningRun })
    getLatestScanMock.mockResolvedValue({ ...runningRun, status: 'Failed', error: 'Disk gone.' })

    renderPage()
    await advance()
    fireEvent.click(screen.getByRole('button', { name: 'Scan incoming' }))
    await advance()
    await advance(1000)

    expect(scanStatus()).toHaveTextContent('Scan failed: Disk gone.')
  })

  it('shows the API message when a scan is already running (409)', async () => {
    startScanMock.mockResolvedValue({
      started: false,
      message: 'Wait for the current scan to finish, then start another.',
    })

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Scan incoming' }))

    expect(await screen.findByText('Wait for the current scan to finish, then start another.')).toBeInTheDocument()
    expect(getLatestScanMock).not.toHaveBeenCalled()
  })

  it('starts polling without a click when the last scan is running on load', async () => {
    vi.useFakeTimers()
    getDashboardMock.mockResolvedValue({ ...dashboard, lastScan: runningRun })
    getLatestScanMock.mockResolvedValue(runningRun)

    renderPage()
    await advance()

    expect(screen.getByRole('button', { name: 'Scan incoming' })).toHaveAttribute('aria-disabled', 'true')
    expect(scanStatus()).toHaveTextContent('Scanning… 5 files found')

    await advance(1000)
    expect(getLatestScanMock).toHaveBeenCalledTimes(1)
    expect(startScanMock).not.toHaveBeenCalled()
  })

  it('shows an alert and stops polling when a poll fails', async () => {
    vi.useFakeTimers()
    getDashboardMock.mockResolvedValue({ ...dashboard, lastScan: runningRun })
    getLatestScanMock.mockRejectedValue(new Error('boom'))

    renderPage()
    await advance()
    await advance(1000)

    expect(screen.getByRole('alert')).toHaveTextContent("The scan's progress couldn't be loaded.")
    await advance(5000)
    expect(getLatestScanMock).toHaveBeenCalledTimes(1)
  })

  it('stops polling on unmount', async () => {
    vi.useFakeTimers()
    getDashboardMock.mockResolvedValue({ ...dashboard, lastScan: runningRun })
    getLatestScanMock.mockResolvedValue(runningRun)

    const { unmount } = renderPage()
    await advance()
    unmount()
    await advance(5000)

    expect(getLatestScanMock).not.toHaveBeenCalled()
  })
})
