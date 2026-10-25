import { useEffect, useRef, useState } from 'react'
import { Link, type To } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import CatalogSearch, { CandidateCover, CandidateDetails } from '../book/CatalogSearch'
import { BookCover, StageChip, bookTitle } from './BookList'
import { getMatch } from '../../services/api'
import type { BookMatch } from '../../types/metadata'
import type { ReviewBook } from '../../types/review'
import { formatDuration } from '../../utils/formatDuration'

type MatchState = { kind: 'loading' } | { kind: 'loaded'; match: BookMatch | null } | { kind: 'failed' }

/** The chosen book's detail pane. Render it keyed by the book so a new book starts fresh. */
export default function BookPane({
  book,
  backHref,
  onMatchSaved,
}: {
  book: ReviewBook
  backHref: To
  onMatchSaved: () => void
}) {
  const [matchState, setMatchState] = useState<MatchState>({ kind: 'loading' })
  const [savedMessage, setSavedMessage] = useState('')
  const matchHeading = useRef<HTMLHeadingElement>(null)
  const headingId = `review-book-${book.groupId}`
  const matchHeadingId = `review-match-${book.groupId}`

  useEffect(() => {
    let current = true
    getMatch(book.groupId)
      .then((match) => {
        if (current) setMatchState({ kind: 'loaded', match })
      })
      .catch(() => {
        if (current) setMatchState({ kind: 'failed' })
      })
    return () => {
      current = false
    }
  }, [book.groupId])

  function handlePicked(match: BookMatch) {
    setMatchState({ kind: 'loaded', match })
    setSavedMessage(`Match saved: ${match.match.title ?? 'Untitled'}`)
    matchHeading.current?.focus()
    onMatchSaved()
  }

  const currentMatch = matchState.kind === 'loaded' ? (matchState.match?.match ?? null) : null

  return (
    <section aria-labelledby={headingId}>
      <p className="mb-4 lg:hidden">
        <Link to={backHref} className="underline underline-offset-4">
          Back to list
        </Link>
      </p>
      <div className="flex flex-wrap gap-6">
        <BookCover book={book} size={160} />
        <div className="min-w-0">
          <h2 id={headingId} className="text-xl font-semibold">
            {bookTitle(book)}
          </h2>
          {book.author && <p>{book.author}</p>}
          {book.narrator && <p className="text-muted">Narrated by {book.narrator}</p>}
          <p className="mt-2 flex flex-wrap items-center gap-2 text-sm text-muted">
            <StageChip stage={book.stage} />
            <span className="tabular-nums">{formatDuration(book.durationMs)}</span>
          </p>
          <p className="mt-4">
            <Link to={`/books/${book.groupId}`} className="underline underline-offset-4">
              Open book page
            </Link>
          </p>
        </div>
      </div>
      <section aria-labelledby={matchHeadingId} className="mt-6">
        <h3 id={matchHeadingId} ref={matchHeading} tabIndex={-1} className="rounded text-lg font-semibold">
          Match
        </h3>
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
              <p className="font-semibold">{matchState.match.match.title ?? 'Untitled'}</p>
              {matchState.match.match.subtitle && <p className="text-sm text-muted">{matchState.match.match.subtitle}</p>}
              <CandidateDetails candidate={matchState.match.match} />
            </div>
          </div>
        )}
        <CatalogSearch
          initialKeywords={[book.title, book.author].filter((part) => part?.trim()).join(' ')}
          groupId={book.groupId}
          currentMatch={currentMatch}
          onPicked={handlePicked}
        />
      </section>
    </section>
  )
}
