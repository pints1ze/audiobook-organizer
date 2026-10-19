import { Fragment, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { getDashboard, getHealth, getLatestScan, startScan } from '../services/api'
import type { Dashboard, ScanRun } from '../types/dashboard'
import type { HealthResponse } from '../types/health'
import { formatDateTime } from '../utils/formatDateTime'

type HealthState =
  | { kind: 'loading' }
  | { kind: 'loaded'; health: HealthResponse }
  | { kind: 'error' }

type DashboardState =
  | { kind: 'loading' }
  | { kind: 'loaded'; dashboard: Dashboard }
  | { kind: 'error' }

const POLL_INTERVAL_MS = 1000

export default function DashboardPage() {
  const [health, setHealth] = useState<HealthState>({ kind: 'loading' })
  const [state, setState] = useState<DashboardState>({ kind: 'loading' })
  // The running scan being polled, or null when none is.
  const [progress, setProgress] = useState<ScanRun | null>(null)
  const [starting, setStarting] = useState(false)
  const [announcement, setAnnouncement] = useState('')
  const [startMessage, setStartMessage] = useState<string | null>(null)
  const [startFailed, setStartFailed] = useState(false)
  const [pollFailed, setPollFailed] = useState(false)
  const mounted = useRef(false)
  const pollTimer = useRef<number | undefined>(undefined)

  const loadDashboard = useCallback(async () => {
    try {
      const dashboard = await getDashboard()
      if (!mounted.current) return null
      setState({ kind: 'loaded', dashboard })
      return dashboard
    } catch {
      if (mounted.current) setState({ kind: 'error' })
      return null
    }
  }, [])

  const poll = useCallback(() => {
    pollTimer.current = window.setTimeout(async () => {
      let run: ScanRun | null
      try {
        run = await getLatestScan()
      } catch {
        if (!mounted.current) return
        setProgress(null)
        setPollFailed(true)
        return
      }
      if (!mounted.current) return
      if (run?.status === 'Running') {
        setProgress(run)
        poll()
        return
      }
      setProgress(null)
      if (run?.status === 'Completed') setAnnouncement('Scan completed')
      else if (run?.status === 'Failed') setAnnouncement(`Scan failed: ${run.error ?? 'unknown error'}`)
      await loadDashboard()
    }, POLL_INTERVAL_MS)
  }, [loadDashboard])

  const beginPolling = useCallback(
    (run: ScanRun) => {
      window.clearTimeout(pollTimer.current)
      setPollFailed(false)
      setAnnouncement('')
      setProgress(run)
      poll()
    },
    [poll],
  )

  useEffect(() => {
    mounted.current = true
    getHealth()
      .then((result) => {
        if (mounted.current) setHealth({ kind: 'loaded', health: result })
      })
      .catch(() => {
        if (mounted.current) setHealth({ kind: 'error' })
      })
    loadDashboard().then((dashboard) => {
      if (dashboard?.lastScan?.status === 'Running') beginPolling(dashboard.lastScan)
    })
    return () => {
      mounted.current = false
      window.clearTimeout(pollTimer.current)
    }
  }, [loadDashboard, beginPolling])

  const scanning = progress !== null
  const busy = scanning || starting

  async function handleScan() {
    // aria-disabled (not disabled) while busy keeps focus on the button, so ignore the click here.
    if (busy) return
    setStarting(true)
    setStartMessage(null)
    setStartFailed(false)
    try {
      const result = await startScan()
      if (!mounted.current) return
      if (result.started) beginPolling(result.run)
      else setStartMessage(result.message)
    } catch {
      if (mounted.current) setStartFailed(true)
    } finally {
      if (mounted.current) setStarting(false)
    }
  }

  const dashboard = state.kind === 'loaded' ? state.dashboard : null
  const incomingSet = !!dashboard?.incomingPath

  return (
    <>
      <h1 className="text-2xl font-semibold">Dashboard</h1>

      <section aria-labelledby="health-heading" className="mt-4 text-sm">
        <h2 id="health-heading" className="sr-only">
          System health
        </h2>
        <div role="status" data-testid="health-status">
          {health.kind === 'loading' && <p className="text-muted">Checking API…</p>}
          {health.kind === 'loaded' && (
            <>
              <p>API: {health.health.status}</p>
              <p>Database: {health.health.database}</p>
            </>
          )}
          {health.kind === 'error' && (
            <p className="flex items-center gap-2 text-destructive">
              <CircleAlert aria-hidden="true" className="size-5" />
              API unreachable
            </p>
          )}
        </div>
      </section>

      {state.kind === 'loading' && <p className="mt-6 text-muted">Loading dashboard…</p>}
      {state.kind === 'error' && (
        <ErrorMessage className="mt-6">The dashboard couldn't be loaded.</ErrorMessage>
      )}

      {dashboard && (
        <>
          <section aria-labelledby="directories-heading" className="mt-6">
            <h2 id="directories-heading" className="text-xl font-semibold">
              Directories
            </h2>
            <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              <dt className="font-medium">Incoming</dt>
              <dd>
                <PathValue path={dashboard.incomingPath} />
              </dd>
              <dt className="font-medium">Library</dt>
              <dd>
                <PathValue path={dashboard.libraryPath} />
              </dd>
            </dl>
          </section>

          <div className="mt-6 flex flex-wrap items-center gap-4">
            <button
              type="button"
              onClick={handleScan}
              disabled={!incomingSet}
              aria-disabled={busy || undefined}
              aria-describedby={!incomingSet ? 'scan-disabled-reason' : undefined}
              className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60 aria-disabled:opacity-60"
            >
              Scan incoming
            </button>
            {!incomingSet && (
              <p id="scan-disabled-reason" className="text-sm text-muted">
                Set the incoming directory in Settings first.
              </p>
            )}
            <p role="status" data-testid="scan-status">
              {progress ? `Scanning… ${progress.filesFound} files found` : announcement}
            </p>
          </div>
          {startMessage && <ErrorMessage className="mt-2">{startMessage}</ErrorMessage>}
          {startFailed && (
            <ErrorMessage className="mt-2">The scan couldn't be started. Please try again.</ErrorMessage>
          )}
          {pollFailed && (
            <ErrorMessage className="mt-2">
              The scan's progress couldn't be loaded. Reload the page to check on it.
            </ErrorMessage>
          )}

          <section aria-labelledby="last-scan-heading" className="mt-6">
            <h2 id="last-scan-heading" className="text-xl font-semibold">
              Last scan
            </h2>
            {dashboard.lastScan ? <LastScan run={dashboard.lastScan} /> : <p className="mt-2">No scans yet.</p>}
          </section>

          <section aria-labelledby="inventory-heading" className="mt-6">
            <h2 id="inventory-heading" className="text-xl font-semibold">
              Inventory
            </h2>
            <p className="text-sm text-muted">From the last completed scan</p>
            <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              <dt className="font-medium">Audio files</dt>
              <dd>{dashboard.audioFileCount}</dd>
              <dt className="font-medium">Files with errors</dt>
              <dd>{dashboard.filesWithErrors}</dd>
            </dl>
          </section>

          <section aria-labelledby="books-heading" className="mt-6">
            <h2 id="books-heading" className="text-xl font-semibold">
              Books
            </h2>
            <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              <dt className="font-medium">Proposed books</dt>
              <dd>{dashboard.bookCount}</dd>
              <dt className="font-medium">Low confidence</dt>
              <dd>{dashboard.booksByConfidence.low}</dd>
              <dt className="font-medium">Medium confidence</dt>
              <dd>{dashboard.booksByConfidence.medium}</dd>
              <dt className="font-medium">High confidence</dt>
              <dd>{dashboard.booksByConfidence.high}</dd>
              <dt className="font-medium">Corrected by you</dt>
              <dd>{dashboard.correctedBookCount}</dd>
              <dt className="font-medium">Unresolved files</dt>
              <dd>{dashboard.unresolvedFileCount}</dd>
              <dt className="font-medium">Excluded files</dt>
              <dd>{dashboard.excludedFileCount}</dd>
            </dl>
            <p className="mt-2">
              <Link to="/incoming" className="underline underline-offset-4">
                Review on the Incoming page
              </Link>
            </p>
          </section>
        </>
      )}
    </>
  )
}

function PathValue({ path }: { path: string | null }) {
  if (path) return <span className="break-all">{path}</span>
  return (
    <>
      Not set.{' '}
      <Link to="/settings" className="underline underline-offset-4">
        Set it in Settings
      </Link>
    </>
  )
}

function LastScan({ run }: { run: ScanRun }) {
  const rows: [string, ReactNode][] = [
    ['Status', run.status],
    ['Finished', formatDateTime(run.finishedAt)],
    ['Found', run.filesFound],
    ['Added', run.filesAdded],
    ['Updated', run.filesUpdated],
    ['Unchanged', run.filesUnchanged],
    ['Missing', run.filesMissing],
    ['Failed', run.filesFailed],
  ]
  return (
    <>
      <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
        {rows.map(([term, value]) => (
          <Fragment key={term}>
            <dt className="font-medium">{term}</dt>
            <dd>{value}</dd>
          </Fragment>
        ))}
      </dl>
      {run.error && (
        <p className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          Error: {run.error}
        </p>
      )}
    </>
  )
}

function ErrorMessage({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <p role="alert" className={`flex items-center gap-2 text-destructive ${className ?? ''}`}>
      <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
      {children}
    </p>
  )
}
