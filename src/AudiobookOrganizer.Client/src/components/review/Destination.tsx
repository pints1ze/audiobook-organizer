import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert, CircleCheck } from 'lucide-react'
import { getBookPreview } from '../../services/api'
import type { BookPreview } from '../../types/preview'
import { fileProblems } from '../../utils/previewProblems'

type DestinationState =
  | { kind: 'loading'; key: string }
  | { kind: 'loaded'; key: string; preview: BookPreview }
  | { kind: 'failed'; key: string }

function fileName(path: string) {
  return path.split(/[\\/]/).pop() ?? path
}

/** The folder of a full destination path: everything before its last separator. */
function folderOf(path: string) {
  return path.slice(0, Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\')))
}

/**
 * Where the book's files would go in the library and every problem that blocks approval.
 * Loads again whenever `reloadKey` changes; a response for an older key or book is ignored.
 */
export default function Destination({ groupId, reloadKey }: { groupId: number; reloadKey: number }) {
  const key = `${groupId}:${reloadKey}`
  const [state, setState] = useState<DestinationState>({ kind: 'loading', key })

  useEffect(() => {
    let current = true
    getBookPreview(groupId)
      .then((preview) => {
        if (current) setState({ kind: 'loaded', key, preview })
      })
      .catch(() => {
        if (current) setState({ kind: 'failed', key })
      })
    return () => {
      current = false
    }
  }, [groupId, key])

  const headingId = `destination-heading-${groupId}`
  // A new book or key shows the skeleton until its own response arrives.
  const shown: DestinationState = state.key === key ? state : { kind: 'loading', key }

  return (
    <section aria-labelledby={headingId} className="mt-6">
      <h2 id={headingId} className="text-xl font-semibold">
        Destination
      </h2>
      {shown.kind === 'loading' && (
        <>
          <p role="status" className="sr-only">
            Loading the destination…
          </p>
          <span aria-hidden="true" className="mt-2 block h-4 w-2/3 animate-pulse rounded bg-border" />
        </>
      )}
      {shown.kind === 'failed' && (
        <p role="alert" className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The destination couldn't be loaded.
        </p>
      )}
      {shown.kind === 'loaded' && <DestinationContent preview={shown.preview} />}
    </section>
  )
}

function DestinationContent({ preview }: { preview: BookPreview }) {
  const { files } = preview
  const first = files.find((file) => file.destinationPath)
  const problems = files.flatMap((file) =>
    fileProblems(file).map((problem) => ({ key: `${file.fileId}:${problem}`, file: fileName(file.sourcePath), problem })),
  )

  return (
    <>
      <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
        <dt className="font-medium">Folder</dt>
        <dd>
          {!preview.libraryPath ? (
            <>
              The library path isn't set.{' '}
              <Link to="/settings" className="underline underline-offset-4">
                Set it in Settings
              </Link>
            </>
          ) : first?.destinationPath ? (
            <span className="break-all">{folderOf(first.destinationPath)}</span>
          ) : (
            <span className="italic">No destination</span>
          )}
        </dd>
        <dt className="font-medium">Files</dt>
        <dd>{files.length === 1 ? '1 file' : `${files.length} files`}</dd>
      </dl>
      {problems.length === 0 ? (
        <p className="mt-2 flex items-center gap-2">
          <CircleCheck aria-hidden="true" className="size-5 shrink-0" />
          Ready to approve.
        </p>
      ) : (
        <ul aria-label="Problems" className="mt-2 space-y-1">
          {problems.map(({ key, file, problem }) => (
            <li key={key} className="flex items-start gap-2 text-destructive">
              <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
              <span className="break-all">
                {file}: {problem}
              </span>
            </li>
          ))}
        </ul>
      )}
      <details className="mt-2">
        <summary className="cursor-pointer">Show all {files.length} files</summary>
        <ul className="mt-2 space-y-1 text-sm">
          {files.map((file) => (
            <li key={file.fileId} className="break-all">
              {file.relativePath ?? <span className="italic">No destination</span>}
            </li>
          ))}
        </ul>
      </details>
    </>
  )
}
