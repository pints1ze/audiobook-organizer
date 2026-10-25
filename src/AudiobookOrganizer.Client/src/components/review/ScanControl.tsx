import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { getLatestScan, getSettings, startScan } from '../../services/api'
import type { ScanRun } from '../../types/dashboard'
import { formatDateTime } from '../../utils/formatDateTime'

const POLL_INTERVAL_MS = 1000

/** The steady status line for the latest scan. */
function lastScanLine(run: ScanRun | null) {
  if (!run) return 'No scans yet.'
  if (run.status === 'Failed') return `Last scan failed: ${run.error ?? 'unknown error'}`
  return `Last scan ${formatDateTime(run.finishedAt)}: ${run.filesAdded} files added`
}

// shortcut: polling mirrors DashboardPage — revisit if a third caller appears
/**
 * The Review header's "Scan incoming" button and last-scan status. Polls a running scan and calls
 * `onCompleted` when one completes.
 */
export default function ScanControl({ onCompleted }: { onCompleted: () => void }) {
  // undefined until loaded; a failed settings load leaves the button (the API refuses a scan without a path).
  const [incomingPath, setIncomingPath] = useState<string | null | undefined>(undefined)
  const [lastRun, setLastRun] = useState<ScanRun | null | undefined>(undefined)
  // The running scan being polled, or null when none is.
  const [progress, setProgress] = useState<ScanRun | null>(null)
  const [starting, setStarting] = useState(false)
  const [announcement, setAnnouncement] = useState('')
  const [startMessage, setStartMessage] = useState<string | null>(null)
  const [startFailed, setStartFailed] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)
  const [pollFailed, setPollFailed] = useState(false)
  const mounted = useRef(false)
  const pollTimer = useRef<number | undefined>(undefined)
  const onCompletedRef = useRef(onCompleted)
  useEffect(() => {
    onCompletedRef.current = onCompleted
  }, [onCompleted])

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
      setLastRun(run ?? null)
      if (run?.status === 'Completed') {
        setAnnouncement('Scan completed')
        onCompletedRef.current()
      } else if (run?.status === 'Failed') {
        setAnnouncement(`Scan failed: ${run.error ?? 'unknown error'}`)
      }
    }, POLL_INTERVAL_MS)
  }, [])

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
    getSettings()
      .then((settings) => {
        if (mounted.current) setIncomingPath(settings.incomingPath)
      })
      .catch(() => {
        // Keep the button; a scan without an incoming path is refused with a message.
      })
    getLatestScan()
      .then((run) => {
        if (!mounted.current) return
        setLastRun(run ?? null)
        if (run?.status === 'Running') beginPolling(run)
      })
      .catch(() => {
        if (mounted.current) setLoadFailed(true)
      })
    return () => {
      mounted.current = false
      window.clearTimeout(pollTimer.current)
    }
  }, [beginPolling])

  const busy = progress !== null || starting

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

  let status = ''
  if (progress) status = `Scanning… ${progress.filesFound} files found`
  else if (announcement && lastRun?.status !== 'Completed') status = announcement
  else if (lastRun !== undefined) status = [announcement, lastScanLine(lastRun)].filter(Boolean).join('. ')

  return (
    <div className="flex flex-wrap items-center gap-3">
      {incomingPath === null ? (
        <Link to="/settings" className="text-sm underline underline-offset-4">
          Set the incoming folder in Settings
        </Link>
      ) : (
        <button
          type="button"
          onClick={handleScan}
          aria-disabled={busy || undefined}
          className="rounded border border-border px-3 py-1 text-sm hover:bg-accent aria-disabled:opacity-60"
        >
          Scan incoming
        </button>
      )}
      <p role="status" data-testid="scan-status" className="text-sm text-muted">
        {status}
      </p>
      {startMessage && <ErrorMessage>{startMessage}</ErrorMessage>}
      {startFailed && <ErrorMessage>The scan couldn't be started. Please try again.</ErrorMessage>}
      {loadFailed && <ErrorMessage>The last scan couldn't be loaded.</ErrorMessage>}
      {pollFailed && (
        <ErrorMessage>The scan's progress couldn't be loaded. Reload the page to check on it.</ErrorMessage>
      )}
    </div>
  )
}

function ErrorMessage({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="flex items-center gap-2 text-sm text-destructive">
      <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
      {children}
    </p>
  )
}
