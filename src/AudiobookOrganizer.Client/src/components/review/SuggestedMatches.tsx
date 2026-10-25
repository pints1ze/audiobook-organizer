import { useEffect, useRef, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import { CandidateCover, CandidateDetails, isSameCandidate } from '../book/CatalogSearch'
import { findCandidates, getCandidates, pickMatch } from '../../services/api'
import type { BookCandidates, BookMatch, MetadataCandidate, ScoredCandidate } from '../../types/metadata'
import { formatDateTime } from '../../utils/formatDateTime'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; result: BookCandidates | null } | { kind: 'failed' }

/**
 * A book's stored candidates side by side with their scores, a button to (re)search, and Accept match on each card.
 * `onPicked` gets the saved match, exactly as Catalog search's pick does.
 */
export default function SuggestedMatches({
  groupId,
  currentMatch,
  onPicked,
}: {
  groupId: number
  currentMatch: MetadataCandidate | null
  onPicked: (match: BookMatch) => void
}) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState(false)
  const [statusText, setStatusText] = useState('')
  const [picking, setPicking] = useState(false)
  const [pickError, setPickError] = useState<{ index: number; text: string } | null>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const mounted = useRef(false)
  const headingId = `suggested-matches-${groupId}`

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    let current = true
    getCandidates(groupId)
      .then((result) => {
        if (current) setState({ kind: 'loaded', result })
      })
      .catch(() => {
        if (current) setState({ kind: 'failed' })
      })
    return () => {
      current = false
    }
  }, [groupId])

  async function handleSearch() {
    const wasNeverSearched = state.kind === 'loaded' && state.result === null
    setSearching(true)
    setSearchError(false)
    setPickError(null)
    setStatusText('Searching…')
    let result: BookCandidates | null = null
    try {
      result = await findCandidates(groupId)
    } catch {
      // shown as an alert below
    }
    if (!mounted.current) return
    setSearching(false)
    if (!result) {
      setSearchError(true)
      setStatusText('')
      return
    }
    const count = result.candidates.length
    setState({ kind: 'loaded', result })
    // An empty result already reads "No suggestions found." below.
    setStatusText(count === 0 ? 'Search finished.' : `${count} ${count === 1 ? 'suggestion' : 'suggestions'} found.`)
    // "Find suggestions" is replaced by "Search again", so focus would be lost; keep it in the area.
    if (wasNeverSearched) heading.current?.focus()
  }

  async function handlePick(candidate: MetadataCandidate, index: number) {
    setPicking(true)
    setPickError(null)
    let text: string | null = null
    let saved: BookMatch | null = null
    try {
      const response = await pickMatch(groupId, candidate)
      if (response.ok) saved = response.match
      else if ('notFound' in response) text = 'This book no longer exists.'
      else text = Object.values(response.errors).flat()[0] ?? "This match couldn't be saved."
    } catch {
      text = "The match couldn't be saved. Check the connection and try again."
    }
    if (!mounted.current) return
    setPicking(false)
    if (text) setPickError({ index, text })
    if (saved) onPicked(saved)
  }

  const result = state.kind === 'loaded' ? state.result : null
  const candidates = result?.candidates ?? []
  const acceptable = candidates.map((scored) => !isSameCandidate(scored.candidate, currentMatch))
  // Candidates come best first, so the first acceptable one is the primary action.
  const primary = acceptable.indexOf(true)

  return (
    <section aria-labelledby={headingId} className="mt-6">
      <h3 id={headingId} ref={heading} tabIndex={-1} className="rounded text-lg font-semibold">
        Suggested matches
      </h3>
      {state.kind === 'loading' && (
        <>
          <p role="status" className="sr-only">
            Loading the suggested matches…
          </p>
          <span aria-hidden="true" className="mt-2 block h-4 w-2/3 animate-pulse rounded bg-border" />
        </>
      )}
      {state.kind === 'failed' && (
        <p role="alert" className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The suggested matches couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' && (
        <>
          <div className="mt-2 flex flex-wrap items-center gap-4">
            <p className="text-sm text-muted">
              {result ? `Searched ${formatDateTime(result.searchedAtUtc)}` : 'Not searched yet.'}
            </p>
            <button
              type="button"
              onClick={handleSearch}
              disabled={searching}
              className="rounded border border-foreground px-3 py-1 text-sm font-medium disabled:opacity-60"
            >
              {result ? 'Search again' : 'Find suggestions'}
            </button>
          </div>
          <p role="status" className="mt-2 text-sm">
            {statusText}
          </p>
          {searchError && (
            <p role="alert" className="mt-2 flex items-center gap-2 text-destructive">
              <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
              The search couldn't be run. Check the connection and try again.
            </p>
          )}
          {result && result.errors.length > 0 && (
            <div className="mt-2 text-sm">
              {result.errors.map((error, index) => (
                <p key={index}>{error}</p>
              ))}
            </div>
          )}
          {result && candidates.length === 0 && <p className="mt-2">No suggestions found.</p>}
          {candidates.length > 0 && (
            <ul className="mt-2 grid grid-cols-[repeat(auto-fill,minmax(14rem,1fr))] gap-4">
              {candidates.map((scored, index) => (
                <SuggestionCard
                  key={`${scored.candidate.provider}-${scored.candidate.asin ?? scored.candidate.isbn ?? ''}-${index}`}
                  scored={scored}
                  isCurrent={!acceptable[index]}
                  isPrimary={index === primary}
                  picking={picking}
                  error={pickError?.index === index ? pickError.text : null}
                  onPick={() => handlePick(scored.candidate, index)}
                />
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  )
}

function SuggestionCard({
  scored,
  isCurrent,
  isPrimary,
  picking,
  error,
  onPick,
}: {
  scored: ScoredCandidate
  isCurrent: boolean
  isPrimary: boolean
  picking: boolean
  error: string | null
  onPick: () => void
}) {
  const { candidate, score } = scored
  const title = candidate.title ?? 'Untitled'
  return (
    <li className="flex flex-col gap-2 rounded border border-muted/30 p-3">
      <div className="flex gap-3">
        <CandidateCover candidate={candidate} />
        <div className="min-w-0">
          <p className="font-semibold">{title}</p>
          {candidate.subtitle && <p className="text-sm text-muted">{candidate.subtitle}</p>}
          <p className="text-sm font-medium tabular-nums">Score {score} of 100</p>
        </div>
      </div>
      <CandidateDetails candidate={candidate} />
      {isCurrent ? (
        <p className="text-sm font-medium">Current match</p>
      ) : (
        <button
          type="button"
          onClick={onPick}
          disabled={picking}
          data-primary={isPrimary || undefined}
          aria-label={`Accept match: ${title}, score ${score} of 100`}
          className={`self-start rounded border border-foreground px-3 py-1 text-sm font-medium disabled:opacity-60 ${
            isPrimary ? 'bg-foreground text-background' : ''
          }`}
        >
          Accept match
        </button>
      )}
      {error && (
        <p role="alert" className="flex items-center gap-2 text-sm text-destructive">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
          {error}
        </p>
      )}
    </li>
  )
}
