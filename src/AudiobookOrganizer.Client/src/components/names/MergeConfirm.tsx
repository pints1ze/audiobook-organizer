import { useEffect, useRef } from 'react'
import type { CanonicalEntry } from '../../types/names'

interface MergeConfirmProps {
  from: CanonicalEntry
  into: CanonicalEntry
  onConfirm: () => void
  onCancel: () => void
}

/** Inline confirmation shown before any merge; merges have no undo. */
export default function MergeConfirm({ from, into, onConfirm, onCancel }: MergeConfirmProps) {
  const confirmRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    confirmRef.current?.focus()
  }, [])

  return (
    <div className="mt-2 rounded border border-muted/30 p-3">
      <p>
        Merge {from.name} into {into.name}? {from.name} and its aliases become aliases of {into.name}.
      </p>
      <div className="mt-2 flex flex-wrap gap-2">
        <button
          type="button"
          ref={confirmRef}
          onClick={onConfirm}
          className="rounded border border-destructive px-3 py-1 font-medium text-destructive"
        >
          Confirm merge
        </button>
        <button type="button" onClick={onCancel} className="rounded border border-muted px-3 py-1">
          Cancel
        </button>
      </div>
    </div>
  )
}
