import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import CatalogSearch, { CandidateCover, CandidateDetails } from '../components/book/CatalogSearch'
import CoverPicker from '../components/book/CoverPicker'
import MetadataEditor from '../components/book/MetadataEditor'
import NameLinks from '../components/book/NameLinks'
import { getGroup, getMatch } from '../services/api'
import type { GroupDetail } from '../types/groups'
import type { BookMatch } from '../types/metadata'
import { formatDateTime } from '../utils/formatDateTime'
import { formatDuration } from '../utils/formatDuration'

type BookState = { kind: 'loading' } | { kind: 'loaded'; group: GroupDetail } | { kind: 'notFound' }

export default function BookPage() {
  const { groupId } = useParams()
  const id = groupId && /^\d+$/.test(groupId) ? Number(groupId) : null
  const [state, setState] = useState<BookState>({ kind: 'loading' })

  useEffect(() => {
    if (id === null) {
      setState({ kind: 'notFound' })
      return
    }
    let current = true
    setState({ kind: 'loading' })
    getGroup(id)
      .then((group) => {
        if (current) setState({ kind: 'loaded', group })
      })
      // shortcut: getGroup throws on any non-200, so every load failure reads as not found — revisit when getGroup reports 404 separately.
      .catch(() => {
        if (current) setState({ kind: 'notFound' })
      })
    return () => {
      current = false
    }
  }, [id])

  if (state.kind === 'loading') return <p className="text-muted">Loading…</p>
  if (state.kind === 'notFound') {
    return (
      <>
        <h1 className="text-2xl font-semibold">Book not found</h1>
        <p className="mt-4">
          <Link to="/incoming" className="rounded underline underline-offset-4">
            Back to Incoming
          </Link>
        </p>
      </>
    )
  }
  return <BookContent key={state.group.id} group={state.group} />
}

type MatchState = { kind: 'loading' } | { kind: 'loaded'; match: BookMatch | null } | { kind: 'failed' }

function BookContent({ group }: { group: GroupDetail }) {
  const [matchState, setMatchState] = useState<MatchState>({ kind: 'loading' })
  const [savedMessage, setSavedMessage] = useState('')
  const [metadataReloadKey, setMetadataReloadKey] = useState(0)
  const [namesReloadKey, setNamesReloadKey] = useState(0)
  const chosenHeading = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    let current = true
    getMatch(group.id)
      .then((match) => {
        if (current) setMatchState({ kind: 'loaded', match })
      })
      .catch(() => {
        if (current) setMatchState({ kind: 'failed' })
      })
    return () => {
      current = false
    }
  }, [group.id])

  function handlePicked(match: BookMatch) {
    setMatchState({ kind: 'loaded', match })
    setSavedMessage(`Match saved: ${match.match.title ?? 'Untitled'}`)
    setMetadataReloadKey((key) => key + 1)
    setNamesReloadKey((key) => key + 1)
    chosenHeading.current?.focus()
  }

  const currentMatch = matchState.kind === 'loaded' ? (matchState.match?.match ?? null) : null

  return (
    <>
      <h1 className="text-2xl font-semibold">{group.title ?? 'Unknown'}</h1>
      <dl className="mt-4 grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1">
        <dt className="font-medium">Author</dt>
        <dd>{group.author ?? 'Unknown'}</dd>
        <dt className="font-medium">Files</dt>
        <dd>{group.fileCount}</dd>
        <dt className="font-medium">Duration</dt>
        <dd>{formatDuration(group.totalDurationMs)}</dd>
      </dl>
      <section aria-labelledby="chosen-match-heading" className="mt-6">
        <h2 id="chosen-match-heading" ref={chosenHeading} tabIndex={-1} className="rounded text-xl font-semibold">
          Chosen match
        </h2>
        <p role="status" className="mt-2">
          {savedMessage}
        </p>
        {matchState.kind === 'loading' && <p className="mt-2 text-muted">Loading the match…</p>}
        {matchState.kind === 'failed' && (
          <p className="mt-2 flex items-center gap-2 text-destructive">
            <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
            The chosen match couldn't be loaded.
          </p>
        )}
        {matchState.kind === 'loaded' && !matchState.match && <p className="mt-2">No match chosen yet.</p>}
        {matchState.kind === 'loaded' && matchState.match && (
          <div className="mt-2 flex gap-4">
            <CandidateCover candidate={matchState.match.match} />
            <div className="min-w-0">
              <h3 className="font-semibold">{matchState.match.match.title ?? 'Untitled'}</h3>
              {matchState.match.match.subtitle && <p className="text-sm text-muted">{matchState.match.match.subtitle}</p>}
              <CandidateDetails candidate={matchState.match.match} />
              <p className="mt-1 text-sm">Picked {formatDateTime(matchState.match.pickedAtUtc)}</p>
            </div>
          </div>
        )}
      </section>
      <CoverPicker groupId={group.id} reloadKey={metadataReloadKey} />
      <MetadataEditor
        groupId={group.id}
        match={currentMatch}
        reloadKey={metadataReloadKey}
        onSaved={() => setNamesReloadKey((key) => key + 1)}
      />
      <NameLinks kind="people" groupId={group.id} reloadKey={namesReloadKey} />
      <NameLinks kind="genres" groupId={group.id} reloadKey={namesReloadKey} />
      <CatalogSearch
        initialKeywords={[group.title, group.author].filter((part) => part?.trim()).join(' ')}
        groupId={group.id}
        currentMatch={currentMatch}
        onPicked={handlePicked}
      />
    </>
  )
}
