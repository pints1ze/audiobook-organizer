import { useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { approveBook } from '../../services/api'
import type { ReviewBook } from '../../types/review'

function JobsLink() {
  return (
    <Link to="/jobs" className="underline underline-offset-4">
      Jobs
    </Link>
  )
}

/**
 * The pane's action line: Approve & next for a Ready book, otherwise what the stage needs.
 * `onApproved` runs once the book is queued; the page moves on, so this component doesn't update after it.
 */
export default function ApproveAction({
  book,
  headingId,
  onApproved,
}: {
  book: ReviewBook
  headingId: string
  onApproved: () => void
}) {
  const [approving, setApproving] = useState(false)
  const [error, setError] = useState('')

  async function approve() {
    setApproving(true)
    setError('')
    try {
      const result = await approveBook(book.groupId)
      if (result.ok) {
        onApproved()
        return
      }
      setError(result.message)
    } catch {
      setError("The book couldn't be approved.")
    }
    setApproving(false)
  }

  switch (book.stage) {
    case 'NeedsReview':
      return <p className="mt-4">Fix the problems under Destination to approve.</p>
    case 'NeedsMatch':
      return <p className="mt-4">Pick a match or enter the metadata to approve.</p>
    case 'Queued':
      return (
        <p className="mt-4">
          Queued for processing. <JobsLink />
        </p>
      )
    case 'Failed':
      return (
        <p className="mt-4">
          Processing failed. <JobsLink />
        </p>
      )
  }

  return (
    <div className="mt-4 flex flex-wrap items-center gap-3">
      <button
        id="approve-next"
        type="button"
        onClick={approve}
        disabled={approving}
        aria-describedby={headingId}
        className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
      >
        {approving ? 'Approving…' : 'Approve & next'}
      </button>
      {error && (
        <p role="alert" className="flex items-start gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span className="break-words">{error}</span>
        </p>
      )}
    </div>
  )
}
