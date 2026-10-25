import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams, type To } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import BookList, { BookCover, StageChip, bookTitle, stages } from '../components/review/BookList'
import { getReview } from '../services/api'
import type { ReviewBook, ReviewList, ReviewStage } from '../types/review'
import { formatDuration } from '../utils/formatDuration'

type ReviewState = { kind: 'loading' } | { kind: 'loaded'; review: ReviewList } | { kind: 'error' }

/** The query string for a stage filter and a chosen book, always in the same order. */
function searchFor(stage: ReviewStage | null, book: number | null) {
  const params = new URLSearchParams()
  if (stage) params.set('stage', stage)
  if (book !== null) params.set('book', String(book))
  const query = params.toString()
  return query ? `?${query}` : ''
}

export default function ReviewPage() {
  const [state, setState] = useState<ReviewState>({ kind: 'loading' })
  const [searchParams, setSearchParams] = useSearchParams()
  const mounted = useRef(false)

  const stageParam = searchParams.get('stage')
  const stage = stages.some((s) => s.stage === stageParam) ? (stageParam as ReviewStage) : null
  const bookParam = Number(searchParams.get('book'))
  const selectedId = Number.isInteger(bookParam) && bookParam > 0 ? bookParam : null

  const load = useCallback(async () => {
    try {
      const review = await getReview()
      if (mounted.current) setState({ kind: 'loaded', review })
    } catch {
      if (mounted.current) setState({ kind: 'error' })
    }
  }, [])

  useEffect(() => {
    mounted.current = true
    void load()
    return () => {
      mounted.current = false
    }
  }, [load])

  const selected =
    state.kind === 'loaded' ? (state.review.books.find((b) => b.groupId === selectedId) ?? null) : null

  return (
    <>
      <h1 className="text-2xl font-semibold">Review</h1>
      <div className="mt-4 gap-6 lg:grid lg:grid-cols-[360px_1fr]">
        <div className="lg:max-h-[calc(100vh-10rem)] lg:overflow-y-auto lg:pr-2">
          {state.kind === 'loading' && <ListSkeleton />}
          {state.kind === 'error' && (
            <div className="flex flex-wrap items-center gap-3">
              <p role="alert" className="flex items-center gap-2 text-destructive">
                <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
                The books couldn't be loaded.
              </p>
              <button type="button" onClick={() => {
                  setState({ kind: 'loading' })
                  void load()
                }} className="rounded border border-border px-3 py-1">
                Retry
              </button>
            </div>
          )}
          {state.kind === 'loaded' && (
            <BookList
              books={state.review.books}
              counts={state.review.counts}
              stage={stage}
              selectedId={selectedId}
              onStageChange={(next) => setSearchParams(searchFor(next, selectedId))}
              bookHref={(groupId) => ({ search: searchFor(stage, groupId) })}
            />
          )}
        </div>
        <div className="mt-6 lg:mt-0">
          {selected ? (
            <BookPane book={selected} backHref={{ search: searchFor(stage, null) }} />
          ) : (
            state.kind === 'loaded' && <p className="text-muted">Choose a book from the list.</p>
          )}
        </div>
      </div>
    </>
  )
}

/** Placeholder detail pane: match, cover, metadata and approval arrive in later slices. */
function BookPane({ book, backHref }: { book: ReviewBook; backHref: To }) {
  const headingId = `review-book-${book.groupId}`
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
    </section>
  )
}

function ListSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Loading books…
      </p>
      <ul aria-hidden="true" className="space-y-1">
        {Array.from({ length: 6 }, (_, i) => (
          <li key={i} className="flex gap-3 p-2">
            <span className="size-12 shrink-0 animate-pulse rounded bg-border" />
            <span className="flex-1 space-y-2">
              <span className="block h-4 w-3/4 animate-pulse rounded bg-border" />
              <span className="block h-3 w-1/2 animate-pulse rounded bg-border" />
            </span>
          </li>
        ))}
      </ul>
    </>
  )
}
