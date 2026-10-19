import { useEffect, useRef, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import MergeConfirm from './MergeConfirm'
import type { CanonicalEntry, MergeSuggestion } from '../../types/names'

interface MergeSuggestionsProps {
  suggestions: MergeSuggestion[]
  failed: boolean
  /** Resolves true when the merge went through; the page handles status, alerts and focus then. */
  onMerge: (from: CanonicalEntry, into: CanonicalEntry) => Promise<boolean>
}

const keyOf = (suggestion: MergeSuggestion) => `${suggestion.merge.id}-${suggestion.keep.id}`

export default function MergeSuggestions({ suggestions, failed, onMerge }: MergeSuggestionsProps) {
  const [openKey, setOpenKey] = useState<string | null>(null)
  const [focusKey, setFocusKey] = useState<string | null>(null)
  const buttons = useRef(new Map<string, HTMLButtonElement>())

  // Cancel (or a failed merge) puts the item's button back, then focus returns to it.
  useEffect(() => {
    if (focusKey === null) return
    buttons.current.get(focusKey)?.focus()
    setFocusKey(null)
  }, [focusKey])

  async function confirm(suggestion: MergeSuggestion) {
    const merged = await onMerge(suggestion.merge, suggestion.keep)
    setOpenKey(null)
    if (!merged) setFocusKey(keyOf(suggestion))
  }

  return (
    <section aria-labelledby="duplicates-heading" className="mt-6 rounded border border-muted/30 p-4">
      <h2 id="duplicates-heading" className="mb-3 text-lg font-semibold">
        Possible duplicates
      </h2>
      {failed ? (
        <p role="alert" className="flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5" />
          Possible duplicates couldn't be loaded.
        </p>
      ) : suggestions.length === 0 ? (
        <p className="text-muted">No possible duplicates.</p>
      ) : (
        <ul className="divide-y divide-muted/30">
          {suggestions.map((suggestion) => {
            const key = keyOf(suggestion)
            return (
              <li key={key} className="py-2">
                <p className="text-sm text-muted">{suggestion.reason}</p>
                {openKey === key ? (
                  <MergeConfirm
                    from={suggestion.merge}
                    into={suggestion.keep}
                    onConfirm={() => void confirm(suggestion)}
                    onCancel={() => {
                      setOpenKey(null)
                      setFocusKey(key)
                    }}
                  />
                ) : (
                  <button
                    type="button"
                    ref={(element) => {
                      if (element) buttons.current.set(key, element)
                      else buttons.current.delete(key)
                    }}
                    onClick={() => setOpenKey(key)}
                    className="mt-1 rounded border border-muted px-3 py-1"
                  >
                    Merge {suggestion.merge.name} into {suggestion.keep.name}
                  </button>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
