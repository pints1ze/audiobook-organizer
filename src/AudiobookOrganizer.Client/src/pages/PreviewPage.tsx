import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { approveBook, getPreview } from '../services/api'
import type { BookPreview, FilePreview, LibraryPreview } from '../types/preview'
import { fileProblems, hasProblems } from '../utils/previewProblems'

type PreviewState =
  | { kind: 'loading' }
  | { kind: 'loaded'; preview: LibraryPreview }
  | { kind: 'error' }

export default function PreviewPage() {
  const [state, setState] = useState<PreviewState>({ kind: 'loading' })

  useEffect(() => {
    let mounted = true
    getPreview()
      .then((preview) => {
        if (mounted) setState({ kind: 'loaded', preview })
      })
      .catch(() => {
        if (mounted) setState({ kind: 'error' })
      })
    return () => {
      mounted = false
    }
  }, [])

  return (
    <>
      <h1 className="text-2xl font-semibold">Preview</h1>
      <p className="mt-2 text-muted">
        Nothing has been moved. The paths follow the templates on{' '}
        <Link to="/settings" className="underline underline-offset-4">
          Settings
        </Link>
        .
      </p>
      {state.kind === 'loading' && <p className="mt-4 text-muted">Loading preview…</p>}
      {state.kind === 'error' && (
        <p role="alert" className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The preview couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' && <PreviewContent preview={state.preview} />}
    </>
  )
}

function PreviewContent({ preview }: { preview: LibraryPreview }) {
  const [problemsOnly, setProblemsOnly] = useState(false)
  const { books, counts } = preview
  const shown = problemsOnly ? books.filter(hasProblems) : books

  return (
    <>
      <dl className="mt-6 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
        <dt className="font-medium">Library</dt>
        <dd>
          {preview.libraryPath ? (
            <span className="break-all">{preview.libraryPath}</span>
          ) : (
            <>
              Not set.{' '}
              <Link to="/settings" className="underline underline-offset-4">
                Set it in Settings
              </Link>
            </>
          )}
        </dd>
        <dt className="font-medium">Files</dt>
        <dd>{counts.files}</dd>
        <dt className="font-medium">Missing values</dt>
        <dd>{counts.missing}</dd>
        <dt className="font-medium">Errors</dt>
        <dd>{counts.errors}</dd>
        <dt className="font-medium">Duplicate targets</dt>
        <dd>{counts.duplicates}</dd>
        <dt className="font-medium">Already exist</dt>
        <dd>{counts.existing}</dd>
      </dl>

      {books.length === 0 ? (
        <p className="mt-6">
          No books to preview yet.{' '}
          <Link to="/dashboard" className="underline underline-offset-4">
            Scan incoming on the dashboard
          </Link>
        </p>
      ) : (
        <>
          <div className="mt-6 flex flex-wrap items-center gap-x-6 gap-y-2">
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={problemsOnly}
                onChange={(event) => setProblemsOnly(event.target.checked)}
                className="size-4"
              />
              Only show books with problems
            </label>
            <p role="status" className="text-sm text-muted">
              {problemsOnly && shown.length === 0
                ? 'No books have problems.'
                : `${shown.length} of ${books.length} books shown`}
            </p>
          </div>
          {shown.map((book) => (
            <BookSection key={book.groupId} book={book} />
          ))}
        </>
      )}
    </>
  )
}

function BookSection({ book }: { book: BookPreview }) {
  const headingId = `book-${book.groupId}-heading`
  return (
    <section aria-labelledby={headingId} className="mt-6">
      <h2 id={headingId} className="text-xl font-semibold">
        <Link to={`/books/${book.groupId}`} className="underline underline-offset-4">
          {book.title || 'Unknown'}
        </Link>
      </h2>
      {hasProblems(book) ? (
        <p className="mt-2 text-sm text-muted">Fix the problems above to approve this book.</p>
      ) : (
        <ApproveBook groupId={book.groupId} headingId={headingId} />
      )}
      <ul className="mt-2 space-y-3">
        {book.files.map((file) => (
          <FileItem key={file.fileId} file={file} />
        ))}
      </ul>
    </section>
  )
}

type ApproveState =
  | { kind: 'idle' }
  | { kind: 'approving' }
  | { kind: 'queued' }
  | { kind: 'refused'; message: string }

function ApproveBook({ groupId, headingId }: { groupId: number; headingId: string }) {
  const [state, setState] = useState<ApproveState>({ kind: 'idle' })
  const queuedRef = useRef<HTMLParagraphElement>(null)

  useEffect(() => {
    if (state.kind === 'queued') queuedRef.current?.focus()
  }, [state.kind])

  async function approve() {
    setState({ kind: 'approving' })
    try {
      const result = await approveBook(groupId)
      setState(result.ok ? { kind: 'queued' } : { kind: 'refused', message: result.message })
    } catch {
      setState({ kind: 'refused', message: "The book couldn't be approved." })
    }
  }

  if (state.kind === 'queued') {
    return (
      <p ref={queuedRef} role="status" tabIndex={-1} className="mt-2">
        Queued for processing.
      </p>
    )
  }

  return (
    <div className="mt-2">
      <button
        type="button"
        onClick={approve}
        disabled={state.kind === 'approving'}
        aria-describedby={headingId}
        className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
      >
        {state.kind === 'approving' ? 'Approving…' : 'Approve'}
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

function FileItem({ file }: { file: FilePreview }) {
  const problems = fileProblems(file)
  return (
    <li className="rounded border border-muted/30 p-3">
      <p>
        <span className="font-medium">From </span>
        <span className="break-all">{file.sourcePath.split(/[\\/]/).pop()}</span>
      </p>
      <p className="break-all text-sm text-muted">{file.sourcePath}</p>
      <p className="mt-1">
        <span className="font-medium">To </span>
        {file.relativePath ? (
          <span className="break-all">{file.relativePath}</span>
        ) : (
          <span className="italic">No destination</span>
        )}
      </p>
      {problems.map((problem) => (
        <p key={problem} className="mt-1 flex items-start gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span className="break-words">{problem}</span>
        </p>
      ))}
    </li>
  )
}
