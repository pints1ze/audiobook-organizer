import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { approveBook, getJobs } from '../services/api'
import type { ProcessingJob, ProcessingJobFile } from '../types/jobs'
import { formatDateTime } from '../utils/formatDateTime'

type JobsState =
  | { kind: 'loading' }
  | { kind: 'loaded'; jobs: ProcessingJob[] }
  | { kind: 'error' }

const POLL_INTERVAL_MS = 2000

function isActive(job: ProcessingJob) {
  return job.status === 'Queued' || job.status === 'Running'
}

/** The ids of jobs that can be retried: a book's newest job (the list is newest first) when it failed. */
function retryableIds(jobs: ProcessingJob[]): Set<number> {
  const seen = new Set<number>()
  const retryable = new Set<number>()
  for (const job of jobs) {
    if (job.groupId === null || seen.has(job.groupId)) continue
    seen.add(job.groupId)
    if (job.status === 'Failed') retryable.add(job.id)
  }
  return retryable
}

export default function JobsPage() {
  const [state, setState] = useState<JobsState>({ kind: 'loading' })
  // A new object each time, so a repeated message still re-runs the focus effect.
  const [announcement, setAnnouncement] = useState<{ text: string } | null>(null)
  const mounted = useRef(false)
  const pollTimer = useRef<number | undefined>(undefined)
  const statusRef = useRef<HTMLParagraphElement>(null)

  const load = useCallback(async function reload() {
    window.clearTimeout(pollTimer.current)
    let jobs: ProcessingJob[]
    try {
      jobs = await getJobs()
    } catch {
      if (mounted.current) setState({ kind: 'error' })
      return
    }
    if (!mounted.current) return
    setState({ kind: 'loaded', jobs })
    window.clearTimeout(pollTimer.current)
    if (jobs.some(isActive)) pollTimer.current = window.setTimeout(reload, POLL_INTERVAL_MS)
  }, [])

  useEffect(() => {
    mounted.current = true
    void load()
    return () => {
      mounted.current = false
      window.clearTimeout(pollTimer.current)
    }
  }, [load])

  useEffect(() => {
    if (announcement) statusRef.current?.focus()
  }, [announcement])

  function handleQueued(title: string) {
    setAnnouncement({ text: `${title} is queued again.` })
    void load()
  }

  const retryable = state.kind === 'loaded' ? retryableIds(state.jobs) : new Set<number>()

  return (
    <>
      <h1 className="text-2xl font-semibold">Jobs</h1>
      {announcement && (
        <p ref={statusRef} role="status" tabIndex={-1} className="mt-2">
          {announcement.text}
        </p>
      )}
      {state.kind === 'loading' && <p className="mt-4 text-muted">Loading jobs…</p>}
      {state.kind === 'error' && (
        <p role="alert" className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The jobs couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' &&
        (state.jobs.length === 0 ? (
          <p className="mt-4">
            No jobs yet.{' '}
            <Link to="/preview" className="underline underline-offset-4">
              Approve books on the Preview page
            </Link>
          </p>
        ) : (
          state.jobs.map((job) => (
            <JobSection key={job.id} job={job} retryable={retryable.has(job.id)} onQueued={handleQueued} />
          ))
        ))}
    </>
  )
}

function JobSection({
  job,
  retryable,
  onQueued,
}: {
  job: ProcessingJob
  retryable: boolean
  onQueued: (title: string) => void
}) {
  const headingId = `job-${job.id}-heading`
  const title = job.title || 'Unknown'
  return (
    <section aria-labelledby={headingId} className="mt-6">
      <h2 id={headingId} className="text-xl font-semibold">
        {job.groupId !== null ? (
          <Link to={`/books/${job.groupId}`} className="underline underline-offset-4">
            {title}
          </Link>
        ) : (
          title
        )}
      </h2>
      <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
        <dt className="font-medium">Status</dt>
        <dd>{job.status}</dd>
        <dt className="font-medium">Created</dt>
        <dd>{formatDateTime(job.createdAtUtc)}</dd>
        <dt className="font-medium">Finished</dt>
        <dd>{formatDateTime(job.finishedAtUtc)}</dd>
      </dl>
      {job.error && <ErrorLine message={job.error} />}
      {retryable && job.groupId !== null && (
        <RetryJob groupId={job.groupId} headingId={headingId} onQueued={() => onQueued(title)} />
      )}
      <ul className="mt-2 space-y-3">
        {job.files.map((file) => (
          <FileItem key={file.fileId} file={file} />
        ))}
      </ul>
    </section>
  )
}

type RetryState = { kind: 'idle' } | { kind: 'retrying' } | { kind: 'refused'; message: string }

function RetryJob({ groupId, headingId, onQueued }: { groupId: number; headingId: string; onQueued: () => void }) {
  const [state, setState] = useState<RetryState>({ kind: 'idle' })

  async function retry() {
    setState({ kind: 'retrying' })
    try {
      const result = await approveBook(groupId)
      // On success the button stays disabled until the reloaded list replaces this job's Retry.
      if (result.ok) onQueued()
      else setState({ kind: 'refused', message: result.message })
    } catch {
      setState({ kind: 'refused', message: "The book couldn't be queued again." })
    }
  }

  return (
    <div className="mt-2">
      <button
        type="button"
        onClick={retry}
        disabled={state.kind === 'retrying'}
        aria-describedby={headingId}
        className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
      >
        Retry
      </button>
      {state.kind === 'refused' && (
        <p role="alert" className="mt-2 flex items-start gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span className="break-words">{state.message}</span>
        </p>
      )}
    </div>
  )
}

function FileItem({ file }: { file: ProcessingJobFile }) {
  return (
    <li className="rounded border border-muted/30 p-3">
      <p className="break-all font-medium">{file.destinationPath.split(/[\\/]/).pop()}</p>
      <p className="break-all text-sm text-muted">{file.destinationPath}</p>
      <p className="mt-1">Status: {file.status}</p>
      {file.error && <ErrorLine message={file.error} />}
    </li>
  )
}

function ErrorLine({ message }: { message: string }) {
  return (
    <p className="mt-1 flex items-start gap-2 text-destructive">
      <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <span className="break-words">{message}</span>
    </p>
  )
}
