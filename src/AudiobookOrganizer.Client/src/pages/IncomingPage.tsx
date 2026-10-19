import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { getGroups } from '../services/api'
import type { GroupConfidence, GroupsResponse } from '../types/groups'
import { formatDuration } from '../utils/formatDuration'

type IncomingState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: GroupsResponse }
  | { kind: 'error' }

// A subtle extra cue per level; the level is always shown as text.
const confidenceClass: Record<GroupConfidence, string> = {
  Low: 'font-semibold',
  Medium: 'font-medium',
  High: '',
}

export default function IncomingPage() {
  const [state, setState] = useState<IncomingState>({ kind: 'loading' })

  useEffect(() => {
    let mounted = true
    getGroups()
      .then((data) => {
        if (mounted) setState({ kind: 'loaded', data })
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
      <h1 className="text-2xl font-semibold">Incoming</h1>
      {state.kind === 'loading' && <p className="mt-4 text-muted">Loading…</p>}
      {state.kind === 'error' && (
        <p role="alert" className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The incoming groups couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' && <IncomingContent data={state.data} />}
    </>
  )
}

function IncomingContent({ data }: { data: GroupsResponse }) {
  const { groups, unresolved } = data

  if (groups.length === 0 && unresolved.length === 0) {
    return (
      <p className="mt-4">
        No books found yet.{' '}
        <Link to="/" className="underline underline-offset-4">
          Run a scan from the Dashboard
        </Link>
      </p>
    )
  }

  return (
    <>
      <section aria-labelledby="groups-heading" className="mt-6">
        <h2 id="groups-heading" className="text-xl font-semibold">
          Proposed books ({groups.length})
        </h2>
        {groups.length === 0 ? (
          <p className="mt-2">No proposed books.</p>
        ) : (
          <div className="mt-2 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <caption className="sr-only">Proposed books, least confident first</caption>
              <thead>
                <tr className="border-b border-muted/30">
                  <th scope="col" className="p-2 font-medium">Title</th>
                  <th scope="col" className="p-2 font-medium">Author</th>
                  <th scope="col" className="p-2 font-medium">Files</th>
                  <th scope="col" className="p-2 font-medium">Duration</th>
                  <th scope="col" className="p-2 font-medium">Confidence</th>
                  <th scope="col" className="p-2 font-medium">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {groups.map((group) => (
                  <tr key={group.id} className="border-b border-muted/30 align-top">
                    <td className="p-2">{group.title ?? 'Unknown'}</td>
                    <td className="p-2">{group.author ?? 'Unknown'}</td>
                    <td className="p-2">{group.fileCount}</td>
                    <td className="p-2 whitespace-nowrap">{formatDuration(group.totalDurationMs)}</td>
                    <td className={`p-2 ${confidenceClass[group.confidence]}`}>{group.confidence}</td>
                    <td className="p-2">
                      <details>
                        <summary className="cursor-pointer rounded">Evidence</summary>
                        <ul className="mt-1 list-disc pl-5">
                          {group.evidence.map((item, index) => (
                            <li key={index}>{item}</li>
                          ))}
                        </ul>
                      </details>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section aria-labelledby="unresolved-heading" className="mt-6">
        <h2 id="unresolved-heading" className="text-xl font-semibold">
          Unresolved files ({unresolved.length})
        </h2>
        <p className="text-sm text-muted">These files had too little evidence to group.</p>
        {unresolved.length === 0 ? (
          <p className="mt-2">None.</p>
        ) : (
          <ul className="mt-2 space-y-1">
            {unresolved.map((file) => (
              <li key={file.fileId} className="flex flex-wrap gap-x-4">
                <span className="break-all">{file.relativePath}</span>
                <span className="text-muted">{formatDuration(file.durationMs)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  )
}
