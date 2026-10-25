import { useEffect, useRef, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import { acceptSuggested } from '../../services/api'
import type { AcceptSuggestedResult } from '../../types/review'

type Outcome = { kind: 'accepted'; message: string; skipped: string[] } | { kind: 'failed' }

/**
 * The Review list's batch actions: the selection count, Select all shown, Accept suggested match and Clear selection,
 * with the outcome beside them. `selected` is in list order; `onAccepted` gets one result per selected book.
 */
export default function SelectionBar({
  selected,
  shownIds,
  titleFor,
  onSelectionChange,
  onAccepted,
}: {
  selected: number[]
  shownIds: number[]
  titleFor: (groupId: number) => string
  onSelectionChange: (next: Set<number>) => void
  onAccepted: (results: AcceptSuggestedResult[]) => void
}) {
  const [running, setRunning] = useState(false)
  const [outcome, setOutcome] = useState<Outcome | null>(null)
  const selectAll = useRef<HTMLInputElement>(null)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const selectedSet = new Set(selected)
  const shownSelected = shownIds.filter((id) => selectedSet.has(id)).length
  const allShown = shownIds.length > 0 && shownSelected === shownIds.length
  const someShown = shownSelected > 0 && !allShown

  useEffect(() => {
    if (selectAll.current) selectAll.current.indeterminate = someShown
  }, [someShown])

  function toggleShown() {
    const next = new Set(selected)
    for (const id of shownIds) {
      if (allShown) next.delete(id)
      else next.add(id)
    }
    onSelectionChange(next)
  }

  // aria-disabled rather than disabled, so focus stays on the button while it runs and after.
  const acceptDisabled = running || selected.length === 0

  async function accept() {
    if (acceptDisabled) return
    const ids = [...selected]
    setRunning(true)
    setOutcome(null)
    try {
      const results = await acceptSuggested(ids)
      if (!mounted.current) return
      const accepted = results.filter((r) => r.accepted).length
      setOutcome({
        kind: 'accepted',
        message: `Accepted ${accepted} suggested ${accepted === 1 ? 'match' : 'matches'}.`,
        skipped: results.filter((r) => !r.accepted).map((r) => `${titleFor(r.groupId)}: ${r.error ?? 'Not accepted.'}`),
      })
      onAccepted(results)
    } catch {
      if (mounted.current) setOutcome({ kind: 'failed' })
    } finally {
      if (mounted.current) setRunning(false)
    }
  }

  return (
    <div className="mb-3">
      <div role="group" aria-label="Selected books" className="flex flex-wrap items-center gap-2 text-sm">
        <label className="flex items-center gap-2">
          <input
            ref={selectAll}
            type="checkbox"
            checked={allShown}
            onChange={toggleShown}
            disabled={shownIds.length === 0}
            className="size-4"
          />
          Select all shown
        </label>
        <span className="tabular-nums">{selected.length} selected</span>
        <button
          type="button"
          onClick={() => void accept()}
          aria-disabled={acceptDisabled ? 'true' : undefined}
          className="rounded border border-border px-3 py-1 hover:bg-accent aria-disabled:opacity-60"
        >
          Accept suggested match
        </button>
        <button
          type="button"
          onClick={() => onSelectionChange(new Set())}
          className="rounded border border-transparent px-3 py-1 underline underline-offset-4 hover:bg-accent"
        >
          Clear selection
        </button>
      </div>
      <div role="status" className="mt-1 text-sm">
        {outcome?.kind === 'accepted' && (
          <>
            <p>{outcome.message}</p>
            {outcome.skipped.length > 0 && (
              <ul className="mt-1 list-disc pl-5">
                {outcome.skipped.map((line, index) => (
                  <li key={index}>{line}</li>
                ))}
              </ul>
            )}
          </>
        )}
      </div>
      {outcome?.kind === 'failed' && (
        <p role="alert" className="mt-1 flex items-center gap-2 text-sm text-destructive">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
          The suggested matches couldn't be accepted.
        </p>
      )}
    </div>
  )
}
