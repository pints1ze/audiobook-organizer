import { useState } from 'react'
import { Link, type To } from 'react-router-dom'
import { chosenCoverUrl, fileCoverUrl } from '../../services/api'
import type { ReviewBook, ReviewCounts, ReviewStage } from '../../types/review'
import { cn } from '../../utils/cn'
import { formatDuration } from '../../utils/formatDuration'

export const stages: { stage: ReviewStage; label: string; count: keyof ReviewCounts; chip: string }[] = [
  { stage: 'NeedsMatch', label: 'Needs match', count: 'needsMatch', chip: 'bg-amber-50 text-amber-900' },
  { stage: 'NeedsReview', label: 'Needs review', count: 'needsReview', chip: 'bg-sky-50 text-sky-900' },
  { stage: 'Ready', label: 'Ready', count: 'ready', chip: 'bg-emerald-50 text-emerald-900' },
  { stage: 'Queued', label: 'Queued', count: 'queued', chip: 'bg-violet-50 text-violet-900' },
  { stage: 'Failed', label: 'Failed', count: 'failed', chip: 'bg-red-50 text-red-900' },
]

export function bookTitle(book: ReviewBook) {
  return book.title || 'Untitled'
}

function initials(title: string) {
  return title
    .split(/\s+/)
    .map((word) => word.match(/[\p{L}\p{N}]/u)?.[0] ?? '')
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

/** The book's chosen or embedded cover, or its initials when it has none. Decorative: the title sits beside it. */
export function BookCover({ book, size }: { book: ReviewBook; size: 48 | 160 }) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const box = size === 48 ? 'size-12 text-sm' : 'size-40 text-4xl'
  const src =
    book.coverSource === 'chosen'
      ? // The URL doesn't change with the cover, so its chosen time versions it past the browser cache.
        `${chosenCoverUrl(book.groupId)}?v=${encodeURIComponent(book.coverChosenAtUtc ?? '')}`
      : book.coverSource === 'embedded' && book.coverFileId !== null
        ? fileCoverUrl(book.groupId, book.coverFileId)
        : null
  // A cover URL can 404 when the scanned file has moved since the scan; fall back to the initials.
  if (src && src !== failedSrc)
    return <img src={src} alt="" onError={() => setFailedSrc(src)} className={cn(box, 'shrink-0 rounded object-cover')} />
  return (
    <span
      aria-hidden="true"
      className={cn(box, 'flex shrink-0 items-center justify-center rounded bg-secondary font-semibold text-muted')}
    >
      {initials(bookTitle(book))}
    </span>
  )
}

export function StageChip({ stage }: { stage: ReviewStage }) {
  const info = stages.find((s) => s.stage === stage)
  return (
    <span className={cn('rounded px-1.5 py-0.5 text-xs font-medium', info?.chip ?? 'bg-secondary')}>
      {info?.label ?? stage}
    </span>
  )
}

export default function BookList({
  books,
  counts,
  stage,
  selectedId,
  onStageChange,
  bookHref,
}: {
  books: ReviewBook[]
  counts: ReviewCounts
  stage: ReviewStage | null
  selectedId: number | null
  onStageChange: (stage: ReviewStage | null) => void
  bookHref: (groupId: number) => To
}) {
  const total = stages.reduce((sum, s) => sum + counts[s.count], 0)
  const shown = stage ? books.filter((b) => b.stage === stage) : books
  const filters = [
    { stage: null, label: 'All', count: total },
    ...stages.map((s) => ({ stage: s.stage, label: s.label, count: counts[s.count] })),
  ]

  return (
    <>
      <div className="flex flex-wrap gap-1" role="group" aria-label="Filter by stage">
        {filters.map((f) => (
          <button
            key={f.label}
            type="button"
            aria-pressed={stage === f.stage}
            onClick={() => onStageChange(f.stage)}
            className={cn(
              'rounded border px-2 py-1 text-sm',
              stage === f.stage ? 'border-foreground bg-primary text-primary-foreground' : 'border-border hover:bg-accent',
            )}
          >
            {f.label} <span className="tabular-nums">{f.count}</span>
          </button>
        ))}
      </div>
      {books.length === 0 ? (
        <p className="mt-4">
          No books to review yet.{' '}
          <Link to="/dashboard" className="underline underline-offset-4">
            Scan incoming on the dashboard
          </Link>
        </p>
      ) : (
        shown.length === 0 && <p className="mt-4">No books in this stage.</p>
      )}
      <ul aria-label="Books" className="mt-3 space-y-1">
        {shown.map((book) => {
          const selected = book.groupId === selectedId
          return (
            <li key={book.groupId}>
              <Link
                to={bookHref(book.groupId)}
                aria-current={selected ? 'true' : undefined}
                className={cn(
                  'flex gap-3 rounded border-l-4 p-2',
                  selected ? 'border-foreground bg-accent' : 'border-transparent hover:bg-accent',
                )}
              >
                <BookCover book={book} size={48} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{bookTitle(book)}</span>
                  {book.author && <span className="block truncate text-sm">{book.author}</span>}
                  {book.narrator && (
                    <span className="block truncate text-sm text-muted">Narrated by {book.narrator}</span>
                  )}
                  <span className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted">
                    <StageChip stage={book.stage} />
                    <span className="tabular-nums">{formatDuration(book.durationMs)}</span>
                  </span>
                </span>
              </Link>
            </li>
          )
        })}
      </ul>
    </>
  )
}
