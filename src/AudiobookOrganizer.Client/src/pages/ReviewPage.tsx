import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import BookList, { bookTitle, stages } from '../components/review/BookList'
import BookPane from '../components/review/BookPane'
import ScanControl from '../components/review/ScanControl'
import SelectionBar from '../components/review/SelectionBar'
import ShortcutsHelp from '../components/review/ShortcutsHelp'
import { getReview } from '../services/api'
import type { AcceptSuggestedResult, ReviewBook, ReviewList, ReviewStage } from '../types/review'
import { isTypingTarget } from '../utils/isTypingTarget'

type ReviewState = { kind: 'loading' } | { kind: 'loaded'; review: ReviewList } | { kind: 'error' }

/**
 * The page's status line after Approve & next, shown while `bookId` (the book moved to, or none) stays selected.
 * `fromId` is the approved book, still selected until the URL change lands; it's dropped once it has.
 */
type Approved = { message: string; fromId: number | null; bookId: number | null }

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
  const statusLine = useRef<HTMLParagraphElement>(null)
  const [approved, setApproved] = useState<Approved | null>(null)
  const [helpOpen, setHelpOpen] = useState(false)
  // The books ticked for a batch action, and a counter that remounts the pane after one changes the books.
  const [checked, setChecked] = useState<Set<number>>(() => new Set())
  const [paneVersion, setPaneVersion] = useState(0)

  const stageParam = searchParams.get('stage')
  const stage = stages.some((s) => s.stage === stageParam) ? (stageParam as ReviewStage) : null
  const bookParam = Number(searchParams.get('book'))
  const selectedId = Number.isInteger(bookParam) && bookParam > 0 ? bookParam : null

  /** Shows a loaded list and keeps only the ticked books still in it. */
  const show = useCallback((review: ReviewList) => {
    setState({ kind: 'loaded', review })
    const listed = new Set(review.books.map((b) => b.groupId))
    setChecked((current) => {
      const kept = [...current].filter((id) => listed.has(id))
      return kept.length === current.size ? current : new Set(kept)
    })
  }, [])

  const load = useCallback(async () => {
    try {
      const review = await getReview()
      if (mounted.current) show(review)
    } catch {
      if (mounted.current) setState({ kind: 'error' })
    }
  }, [show])

  /** Re-fetches the list without the skeleton; a failure keeps the list on screen. */
  const reload = useCallback(async () => {
    try {
      const review = await getReview()
      if (mounted.current) show(review)
    } catch {
      // Keep the current list; the next reload or page visit refreshes it.
    }
  }, [show])

  useEffect(() => {
    mounted.current = true
    void load()
    return () => {
      mounted.current = false
    }
  }, [load])

  // Choosing another book clears the status line.
  if (approved && selectedId !== approved.bookId && selectedId !== approved.fromId) setApproved(null)
  else if (approved && approved.fromId !== null && selectedId === approved.bookId) setApproved({ ...approved, fromId: null })

  useEffect(() => {
    if (approved && approved.bookId === null && selectedId === null) statusLine.current?.focus()
  }, [approved, selectedId])

  const selected =
    state.kind === 'loaded' ? (state.review.books.find((b) => b.groupId === selectedId) ?? null) : null

  /** Moves to the book after the approved one in the list as shown (else the one before), then reloads the list. */
  function handleApproved(book: ReviewBook) {
    if (state.kind !== 'loaded') return
    const shown = stage ? state.review.books.filter((b) => b.stage === stage) : state.review.books
    const index = shown.findIndex((b) => b.groupId === book.groupId)
    const next = (index >= 0 ? (shown[index + 1] ?? shown[index - 1]) : undefined) ?? null
    const nextId = next?.groupId ?? null
    setSearchParams(searchFor(stage, nextId))
    setApproved({ message: `Queued ${bookTitle(book)}.`, fromId: book.groupId, bookId: nextId })
    void reload()
  }

  function toggleChecked(groupId: number) {
    const next = new Set(checked)
    if (!next.delete(groupId)) next.add(groupId)
    setChecked(next)
  }

  /** Unticks the accepted books, reloads the list, then remounts the pane so the shown book loads its new match. */
  async function handleAccepted(results: AcceptSuggestedResult[]) {
    const accepted = new Set(results.filter((r) => r.accepted).map((r) => r.groupId))
    setChecked((current) => new Set([...current].filter((id) => !accepted.has(id))))
    await reload()
    if (mounted.current) setPaneVersion((version) => version + 1)
  }

  // The selection j/k step from: a key repeat can fire again before the URL change re-renders the page.
  const currentId = useRef(selectedId)
  useEffect(() => {
    currentId.current = selectedId
  }, [selectedId])

  // Single-key shortcuts (UIUX "Actions and keyboard"); each matches a visible control, and none fires while typing.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.ctrlKey || event.altKey || event.metaKey || isTypingTarget(event.target)) return
      switch (event.key) {
        case 'j':
        case 'k': {
          if (state.kind !== 'loaded') return
          const shown = stage ? state.review.books.filter((b) => b.stage === stage) : state.review.books
          if (shown.length === 0) return
          const index = shown.findIndex((b) => b.groupId === currentId.current)
          const target =
            index < 0
              ? event.key === 'j' ? shown[0] : shown[shown.length - 1]
              : shown[Math.min(Math.max(index + (event.key === 'j' ? 1 : -1), 0), shown.length - 1)]
          if (target.groupId !== currentId.current) {
            currentId.current = target.groupId
            setSearchParams(searchFor(stage, target.groupId))
          }
          // Focus scrolls the link into view inside the list.
          document.querySelector<HTMLElement>(`[data-group-id="${target.groupId}"]`)?.focus()
          return
        }
        case 'a':
          document.querySelector<HTMLButtonElement>('#approve-next:not(:disabled)')?.click()
          return
        case '/': {
          const keywords = document.querySelector<HTMLInputElement>('#book-keywords')
          if (!keywords) return
          event.preventDefault()
          keywords.focus()
          return
        }
        case '?':
          event.preventDefault()
          setHelpOpen((open) => !open)
          return
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [state, stage, setSearchParams])

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Review</h1>
        <div className="flex flex-wrap items-center gap-3">
          <ScanControl onCompleted={() => void reload()} />
          <ShortcutsHelp open={helpOpen} onOpenChange={setHelpOpen} />
        </div>
      </div>
      {state.kind === 'loaded' && (
        <p ref={statusLine} role="status" tabIndex={-1} className="mt-2 rounded">
          {approved?.message}
        </p>
      )}
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
            <SelectionBar
              selected={state.review.books.filter((b) => checked.has(b.groupId)).map((b) => b.groupId)}
              shownIds={state.review.books.filter((b) => !stage || b.stage === stage).map((b) => b.groupId)}
              titleFor={(groupId) => {
                const book = state.review.books.find((b) => b.groupId === groupId)
                return book ? bookTitle(book) : 'A book no longer listed'
              }}
              onSelectionChange={setChecked}
              onAccepted={(results) => void handleAccepted(results)}
            />
          )}
          {state.kind === 'loaded' && (
            <BookList
              books={state.review.books}
              counts={state.review.counts}
              stage={stage}
              selectedId={selectedId}
              selectedIds={checked}
              onToggle={toggleChecked}
              onStageChange={(next) => setSearchParams(searchFor(next, selectedId))}
              bookHref={(groupId) => ({ search: searchFor(stage, groupId) })}
            />
          )}
        </div>
        <div className="mt-6 lg:mt-0">
          {selected ? (
            <BookPane
              key={`${selected.groupId}-${paneVersion}`}
              book={selected}
              backHref={{ search: searchFor(stage, null) }}
              onChanged={() => void reload()}
              onApproved={() => handleApproved(selected)}
              focusTitle={approved?.bookId === selected.groupId}
            />
          ) : (
            state.kind === 'loaded' && <p className="text-muted">Choose a book from the list.</p>
          )}
        </div>
      </div>
    </>
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
