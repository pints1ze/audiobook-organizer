import { useEffect, useRef } from 'react'

interface ApproveConfirmProps {
  count: number
  onConfirm: () => void
  onCancel: () => void
}

/** Inline confirmation before approving a batch of books; approval copies, tags and moves them. */
export default function ApproveConfirm({ count, onConfirm, onCancel }: ApproveConfirmProps) {
  const confirmRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    confirmRef.current?.focus()
  }, [])

  return (
    <div
      role="group"
      aria-label="Confirm approval"
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault()
          onCancel()
        }
      }}
      className="mt-2 rounded border border-border p-3 text-sm"
    >
      <p>
        Approve {count} {count === 1 ? 'book' : 'books'}? They'll be copied, tagged and moved into the library.
      </p>
      <div className="mt-2 flex flex-wrap gap-2">
        <button
          type="button"
          ref={confirmRef}
          onClick={onConfirm}
          className="rounded bg-foreground px-3 py-1 font-medium text-background"
        >
          Approve
        </button>
        <button type="button" onClick={onCancel} className="rounded border border-border px-3 py-1 hover:bg-accent">
          Cancel
        </button>
      </div>
    </div>
  )
}
