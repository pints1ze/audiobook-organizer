import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import CoverPicker from '../components/book/CoverPicker'
import MetadataEditor, { providerNames } from '../components/book/MetadataEditor'
import { getGroup, getMatch, pickMatch, searchMetadata } from '../services/api'
import type { GroupDetail } from '../types/groups'
import type { BookMatch, MetadataCandidate, MetadataSearchResult } from '../types/metadata'
import { formatDateTime } from '../utils/formatDateTime'
import { formatDuration } from '../utils/formatDuration'

type BookState = { kind: 'loading' } | { kind: 'loaded'; group: GroupDetail } | { kind: 'notFound' }

const inputClass =
  'mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:border-2'

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
      <MetadataEditor groupId={group.id} match={currentMatch} reloadKey={metadataReloadKey} />
      <CatalogSearch
        initialKeywords={[group.title, group.author].filter((part) => part?.trim()).join(' ')}
        groupId={group.id}
        currentMatch={currentMatch}
        onPicked={handlePicked}
      />
    </>
  )
}

type SearchOutcome =
  | { kind: 'idle' }
  | { kind: 'done'; result: MetadataSearchResult }
  | { kind: 'failed'; message: string }

/** Why a pick failed: a message to show, or the book is gone. */
type PickError = { kind: 'message'; text: string } | { kind: 'notFound' }

/** A result is the current match when provider, ASIN, ISBN and title all equal the chosen match's. */
function isSameCandidate(a: MetadataCandidate, b: MetadataCandidate | null) {
  return !!b && a.provider === b.provider && a.asin === b.asin && a.isbn === b.isbn && a.title === b.title
}

type CatalogSearchProps = {
  initialKeywords: string
  groupId: number
  currentMatch: MetadataCandidate | null
  onPicked: (match: BookMatch) => void
}

function CatalogSearch({ initialKeywords, groupId, currentMatch, onPicked }: CatalogSearchProps) {
  const [keywords, setKeywords] = useState(initialKeywords)
  const [searching, setSearching] = useState(false)
  const [outcome, setOutcome] = useState<SearchOutcome>({ kind: 'idle' })
  const [keywordErrors, setKeywordErrors] = useState<string[]>([])
  const [picking, setPicking] = useState(false)
  const [pickError, setPickError] = useState<{ index: number; error: PickError } | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSearching(true)
    let next: SearchOutcome = { kind: 'idle' }
    let errors: string[] = []
    try {
      const response = await searchMetadata(keywords)
      if (response.ok) next = { kind: 'done', result: response.result }
      else if ('providerFailure' in response) next = { kind: 'failed', message: response.providerFailure }
      else errors = response.errors.keywords?.length ? response.errors.keywords : ['Check the keywords and try again.']
    } catch {
      next = { kind: 'failed', message: "The search couldn't be run. Check the connection and try again." }
    }
    if (!mounted.current) return
    setOutcome(next)
    setKeywordErrors(errors)
    setPickError(null)
    setSearching(false)
    if (errors.length) inputRef.current?.focus()
  }

  async function handlePick(candidate: MetadataCandidate, index: number) {
    setPicking(true)
    setPickError(null)
    let error: PickError | null = null
    let saved: BookMatch | null = null
    try {
      const response = await pickMatch(groupId, candidate)
      if (response.ok) saved = response.match
      else if ('notFound' in response) error = { kind: 'notFound' }
      else error = { kind: 'message', text: Object.values(response.errors).flat()[0] ?? "This match couldn't be saved." }
    } catch {
      error = { kind: 'message', text: "The match couldn't be saved. Check the connection and try again." }
    }
    if (!mounted.current) return
    setPicking(false)
    if (error) setPickError({ index, error })
    if (saved) onPicked(saved)
  }

  const hasErrors = keywordErrors.length > 0
  const candidates = outcome.kind === 'done' ? outcome.result.candidates : []
  const providerErrors = outcome.kind === 'done' ? outcome.result.providerErrors : []
  let statusText = ''
  if (searching) statusText = 'Searching…'
  else if (outcome.kind === 'done')
    statusText = candidates.length === 0 ? 'No matches found.' : `${candidates.length} ${candidates.length === 1 ? 'match' : 'matches'} found.`

  return (
    <section aria-labelledby="catalog-search-heading" className="mt-6">
      <h2 id="catalog-search-heading" className="text-xl font-semibold">
        Catalog search
      </h2>
      <form onSubmit={handleSubmit} noValidate className="mt-2 flex flex-wrap items-start gap-4">
        <div className="min-w-0 grow basis-64">
          <label htmlFor="book-keywords" className="font-medium">
            Keywords
          </label>
          <input
            id="book-keywords"
            ref={inputRef}
            type="search"
            value={keywords}
            onChange={(event) => setKeywords(event.target.value)}
            aria-invalid={hasErrors || undefined}
            aria-describedby={hasErrors ? 'book-keywords-error' : undefined}
            autoComplete="off"
            className={inputClass}
          />
          {hasErrors && (
            <ul id="book-keywords-error" className="mt-1 text-sm text-destructive">
              {keywordErrors.map((error) => (
                <li key={error} className="flex items-center gap-1">
                  <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
                  {error}
                </li>
              ))}
            </ul>
          )}
        </div>
        <button
          type="submit"
          disabled={searching}
          className="mt-7 rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
        >
          Search
        </button>
      </form>

      {outcome.kind === 'failed' && (
        <p role="alert" className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          {outcome.message}
        </p>
      )}
      {providerErrors.length > 0 && (
        <div role="status" className="mt-4 rounded border border-muted p-3">
          <p className="font-medium">Some catalogs didn't answer, so these results come from another one:</p>
          <ul className="mt-1 list-disc pl-5">
            {providerErrors.map((error, index) => (
              <li key={index}>{error}</li>
            ))}
          </ul>
        </div>
      )}
      <p role="status" className="mt-4">
        {statusText}
      </p>
      {candidates.length > 0 && (
        <ul className="mt-2 space-y-4">
          {candidates.map((candidate, index) => (
            <CandidateItem
              key={`${candidate.provider}-${candidate.asin ?? candidate.isbn ?? ''}-${index}`}
              candidate={candidate}
              position={index + 1}
              count={candidates.length}
              isCurrent={isSameCandidate(candidate, currentMatch)}
              picking={picking}
              error={pickError?.index === index ? pickError.error : null}
              onPick={() => handlePick(candidate, index)}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

type CandidateItemProps = {
  candidate: MetadataCandidate
  position: number
  count: number
  isCurrent: boolean
  picking: boolean
  error: PickError | null
  onPick: () => void
}

function CandidateItem({ candidate, position, count, isCurrent, picking, error, onPick }: CandidateItemProps) {
  const title = candidate.title ?? 'Untitled'
  return (
    <li className="flex gap-4 border-b border-muted/30 pb-4">
      <CandidateCover candidate={candidate} />
      <div className="min-w-0">
        <h3 className="font-semibold">{title}</h3>
        {candidate.subtitle && <p className="text-sm text-muted">{candidate.subtitle}</p>}
        <CandidateDetails candidate={candidate} />
        {isCurrent ? (
          <p className="mt-2 text-sm font-medium">Current match</p>
        ) : (
          <button
            type="button"
            onClick={onPick}
            disabled={picking}
            aria-label={`Use this match: ${title}, result ${position} of ${count}`}
            className="mt-2 rounded border border-foreground px-3 py-1 text-sm font-medium disabled:opacity-60"
          >
            Use this match
          </button>
        )}
        {error && (
          <p role="alert" className="mt-2 flex items-center gap-2 text-sm text-destructive">
            <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
            {error.kind === 'notFound' ? (
              <span>
                This book no longer exists.{' '}
                <Link to="/incoming" className="rounded underline underline-offset-4">
                  Back to Incoming
                </Link>
              </span>
            ) : (
              error.text
            )}
          </p>
        )}
      </div>
    </li>
  )
}

function CandidateCover({ candidate }: { candidate: MetadataCandidate }) {
  if (!candidate.coverUrl) return null
  return <img src={candidate.coverUrl} alt="" width={80} height={80} loading="lazy" className="size-20 shrink-0 rounded object-cover" />
}

/** The detail rows shared by a search result and the chosen match, so the two can't drift. */
function CandidateDetails({ candidate }: { candidate: MetadataCandidate }) {
  const series = candidate.series
    ? candidate.series.part
      ? `${candidate.series.name}, book ${candidate.series.part}`
      : candidate.series.name
    : null
  const rows: [string, string | null][] = [
    ['Authors', candidate.authors.join(', ') || null],
    ['Narrators', candidate.narrators.join(', ') || null],
    ['Series', series],
    ['Released', candidate.releaseDate],
    ['Runtime', candidate.runtimeMinutes === null ? null : formatDuration(candidate.runtimeMinutes * 60_000)],
    ['Format', candidate.format],
    ['Provider', providerNames[candidate.provider] ?? candidate.provider],
    ['ASIN', candidate.asin],
    ['ISBN', candidate.isbn],
  ]

  return (
    <dl className="mt-1 grid grid-cols-[max-content_1fr] gap-x-3 text-sm">
      {rows
        .filter((row): row is [string, string] => !!row[1])
        .map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="font-medium">{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
    </dl>
  )
}
