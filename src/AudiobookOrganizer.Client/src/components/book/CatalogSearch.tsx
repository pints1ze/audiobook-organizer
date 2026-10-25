import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { providerNames } from './MetadataEditor'
import { pickMatch, searchMetadata } from '../../services/api'
import type { BookMatch, MetadataCandidate, MetadataSearchResult } from '../../types/metadata'
import { formatDuration } from '../../utils/formatDuration'

const inputClass =
  'mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:border-2'

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

export default function CatalogSearch({ initialKeywords, groupId, currentMatch, onPicked }: CatalogSearchProps) {
  const [keywords, setKeywords] = useState(initialKeywords)
  const [searching, setSearching] = useState(false)
  const [outcome, setOutcome] = useState<SearchOutcome>({ kind: 'idle' })
  const [keywordErrors, setKeywordErrors] = useState<string[]>([])
  const [picking, setPicking] = useState(false)
  const [pickError, setPickError] = useState<{ index: number; error: PickError } | null>(null)
  const [highlighted, setHighlighted] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const buttonRefs = useRef<(HTMLButtonElement | null)[]>([])
  const focusResult = useRef(false)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  // After a search with results, move focus to the highlighted result's Accept match button.
  useEffect(() => {
    if (!focusResult.current) return
    focusResult.current = false
    buttonRefs.current[highlighted]?.focus()
  }, [outcome, highlighted])

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
    const first = next.kind === 'done' ? next.result.candidates.findIndex((c) => !isSameCandidate(c, currentMatch)) : -1
    setHighlighted(Math.max(first, 0))
    focusResult.current = first >= 0
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
  const acceptable = candidates.map((candidate) => !isSameCandidate(candidate, currentMatch))
  // The stored highlight can land on the current match after a pick; fall back to the first acceptable result.
  const shownHighlight = acceptable[highlighted] ? highlighted : acceptable.indexOf(true)

  /** ArrowDown/ArrowUp on a result button move focus (and so the highlight) to the next/previous acceptable result. */
  function handleResultsKeyDown(event: KeyboardEvent<HTMLUListElement>) {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
    const from = buttonRefs.current.indexOf(event.target as HTMLButtonElement)
    if (from < 0) return
    event.preventDefault()
    const step = event.key === 'ArrowDown' ? 1 : -1
    for (let i = from + step; i >= 0 && i < candidates.length; i += step) {
      if (acceptable[i]) {
        buttonRefs.current[i]?.focus()
        return
      }
    }
  }

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
          className="mt-7 rounded border border-foreground px-4 py-2 font-medium disabled:opacity-60"
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
        <ul className="mt-2 space-y-4" onKeyDown={handleResultsKeyDown}>
          {candidates.map((candidate, index) => (
            <CandidateItem
              key={`${candidate.provider}-${candidate.asin ?? candidate.isbn ?? ''}-${index}`}
              candidate={candidate}
              position={index + 1}
              count={candidates.length}
              isCurrent={!acceptable[index]}
              highlighted={index === shownHighlight}
              buttonRef={(button) => {
                buttonRefs.current[index] = button
              }}
              onFocus={() => setHighlighted(index)}
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
  highlighted: boolean
  buttonRef: (button: HTMLButtonElement | null) => void
  onFocus: () => void
  picking: boolean
  error: PickError | null
  onPick: () => void
}

function CandidateItem({
  candidate,
  position,
  count,
  isCurrent,
  highlighted,
  buttonRef,
  onFocus,
  picking,
  error,
  onPick,
}: CandidateItemProps) {
  const title = candidate.title ?? 'Untitled'
  return (
    <li
      data-highlighted={highlighted || undefined}
      className={`flex gap-4 rounded border-2 p-3 ${highlighted ? 'border-foreground bg-accent' : 'border-muted/30'}`}
    >
      <CandidateCover candidate={candidate} />
      <div className="min-w-0">
        <h3 className="font-semibold">{title}</h3>
        {candidate.subtitle && <p className="text-sm text-muted">{candidate.subtitle}</p>}
        <CandidateDetails candidate={candidate} />
        {isCurrent ? (
          <p className="mt-2 text-sm font-medium">Current match</p>
        ) : (
          <button
            ref={buttonRef}
            type="button"
            onClick={onPick}
            onFocus={onFocus}
            disabled={picking}
            aria-label={`Accept match: ${title}, result ${position} of ${count}`}
            className={`mt-2 rounded border border-foreground px-3 py-1 text-sm font-medium disabled:opacity-60 ${
              highlighted ? 'bg-foreground text-background' : ''
            }`}
          >
            Accept match
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

export function CandidateCover({ candidate }: { candidate: MetadataCandidate }) {
  if (!candidate.coverUrl) return null
  return <img src={candidate.coverUrl} alt="" width={80} height={80} loading="lazy" className="size-20 shrink-0 rounded object-cover" />
}

/** The detail rows shared by a search result and the chosen match, so the two can't drift. */
export function CandidateDetails({ candidate }: { candidate: MetadataCandidate }) {
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
