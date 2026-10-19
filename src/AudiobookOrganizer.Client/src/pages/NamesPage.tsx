import { useCallback, useEffect, useRef, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import EntryForm from '../components/names/EntryForm'
import MergeConfirm from '../components/names/MergeConfirm'
import MergeSuggestions from '../components/names/MergeSuggestions'
import { createEntry, getEntries, getSuggestions, mergeEntry, updateEntry } from '../services/api'
import type { CanonicalEntry, MergeSuggestion, NameKind, SaveEntryResult } from '../types/names'

type LoadState = 'loading' | 'loaded' | 'failed'

const labels: Record<
  NameKind,
  { heading: string; newHeading: string; add: string; empty: string; loading: string; loadFailed: string; aliases: string }
> = {
  people: {
    heading: 'People',
    newHeading: 'New person',
    add: 'Add a person',
    empty: 'No people yet.',
    loading: 'Loading people…',
    loadFailed: "People couldn't be loaded.",
    aliases: 'Also known as',
  },
  genres: {
    heading: 'Genres',
    newHeading: 'New genre',
    add: 'Add a genre',
    empty: 'No genres yet.',
    loading: 'Loading genres…',
    loadFailed: "Genres couldn't be loaded.",
    aliases: 'Also called',
  },
}

export default function NamesPage({ kind }: { kind: NameKind }) {
  const text = labels[kind]
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [entries, setEntries] = useState<CanonicalEntry[]>([])
  const [editingId, setEditingId] = useState<number | null>(null)
  const [focusId, setFocusId] = useState<number | null>(null)
  const [status, setStatus] = useState('')
  const [suggestions, setSuggestions] = useState<MergeSuggestion[]>([])
  const [suggestionsFailed, setSuggestionsFailed] = useState(false)
  const [mergeTargets, setMergeTargets] = useState<Record<number, string>>({})
  const [confirmFromId, setConfirmFromId] = useState<number | null>(null)
  const [mergeError, setMergeError] = useState('')
  const editButtons = useRef(new Map<number, HTMLButtonElement>())
  const mergeButtons = useRef(new Map<number, HTMLButtonElement>())

  // The list is always reloaded from the server so order and dropped aliases match it.
  // A failed suggestions load only affects the duplicates section.
  const load = useCallback(async () => {
    const suggestionsLoad = getSuggestions(kind).then(
      (loaded) => {
        setSuggestions(loaded)
        setSuggestionsFailed(false)
      },
      () => setSuggestionsFailed(true),
    )
    try {
      setEntries(await getEntries(kind))
      setLoadState('loaded')
    } catch {
      setLoadState('failed')
    }
    await suggestionsLoad
  }, [kind])

  useEffect(() => {
    void load()
  }, [load])

  // After a save or cancel, focus goes back to the row's Edit button once it is rendered again.
  useEffect(() => {
    if (focusId === null || editingId !== null) return
    editButtons.current.get(focusId)?.focus()
    setFocusId(null)
  }, [focusId, editingId, entries])

  async function save(send: () => Promise<SaveEntryResult>, id?: number) {
    setStatus('')
    const result = await send()
    if (result.ok) {
      setStatus(`Saved ${result.entry.name}.`)
      await load()
      if (id !== undefined) {
        setEditingId(null)
        setFocusId(id)
      }
    }
    return result
  }

  /** Both merge paths end here; resolves true when the merge went through. */
  async function merge(from: CanonicalEntry, into: CanonicalEntry) {
    setStatus('')
    setMergeError('')
    try {
      const result = await mergeEntry(kind, into.id, from.id)
      if (result.ok || 'notFound' in result) {
        setConfirmFromId(null)
        setMergeTargets({})
        await load()
        if (result.ok) {
          setStatus(`Merged ${from.name} into ${into.name}.`)
          setFocusId(into.id)
          return true
        }
        setMergeError('That entry no longer exists.')
        return false
      }
    } catch {
      // Falls through to the alert below; the list stays as it was.
    }
    setMergeError(`${from.name} couldn't be merged into ${into.name}.`)
    setConfirmFromId(null)
    return false
  }

  function retry() {
    setLoadState('loading')
    void load()
  }

  return (
    <>
      <h1 className="text-2xl font-semibold">{text.heading}</h1>
      <p role="status" className="mt-2 min-h-6">
        {status}
      </p>
      {mergeError && (
        <p role="alert" className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5" />
          {mergeError}
        </p>
      )}
      {loadState === 'loading' && <p className="mt-4 text-muted">{text.loading}</p>}
      {loadState === 'failed' && (
        <div role="alert" className="mt-4 flex flex-wrap items-center gap-3 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5" />
          {text.loadFailed}
          <button type="button" onClick={retry} className="rounded border border-muted px-3 py-1 text-foreground">
            Try again
          </button>
        </div>
      )}
      {loadState === 'loaded' && (
        <>
          <section aria-labelledby={`${kind}-new-heading`} className="mt-4 rounded border border-muted/30 p-4">
            <h2 id={`${kind}-new-heading`} className="mb-3 text-lg font-semibold">
              {text.newHeading}
            </h2>
            <EntryForm
              kind={kind}
              submitLabel={text.add}
              onSave={(request) => save(() => createEntry(kind, request))}
            />
          </section>
          <MergeSuggestions suggestions={suggestions} failed={suggestionsFailed} onMerge={merge} />
          {entries.length === 0 ? (
            <p className="mt-6 text-muted">{text.empty}</p>
          ) : (
            <ul aria-label={text.heading} className="mt-6 divide-y divide-muted/30">
              {entries.map((entry) => {
                const targetId = mergeTargets[entry.id] ?? ''
                const target = entries.find((other) => String(other.id) === targetId)
                return (
                  <li key={entry.id} className="py-3">
                    {editingId === entry.id ? (
                      <EntryForm
                        kind={kind}
                        initial={entry}
                        submitLabel="Save"
                        onSave={(request) => save(() => updateEntry(kind, entry.id, request), entry.id)}
                        onCancel={() => {
                          setEditingId(null)
                          setFocusId(entry.id)
                        }}
                      />
                    ) : (
                      <div className="flex items-start justify-between gap-4">
                        <div>
                          <span className="font-medium">{entry.name}</span>
                          {entry.aliases.length > 0 && (
                            <p className="text-sm text-muted">
                              {text.aliases}: {entry.aliases.join(', ')}
                            </p>
                          )}
                        </div>
                        <button
                          type="button"
                          ref={(element) => {
                            if (element) editButtons.current.set(entry.id, element)
                            else editButtons.current.delete(entry.id)
                          }}
                          onClick={() => setEditingId(entry.id)}
                          aria-label={`Edit ${entry.name}`}
                          className="rounded border border-muted px-3 py-1"
                        >
                          Edit
                        </button>
                      </div>
                    )}
                    {editingId !== entry.id && entries.length > 1 && (
                      <>
                        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
                          <label htmlFor={`${kind}-merge-${entry.id}`}>Merge {entry.name} into</label>
                          <select
                            id={`${kind}-merge-${entry.id}`}
                            value={targetId}
                            onChange={(event) =>
                              setMergeTargets((targets) => ({ ...targets, [entry.id]: event.target.value }))
                            }
                            className="rounded border border-muted bg-background px-2 py-1"
                          >
                            <option value="">Choose…</option>
                            {entries
                              .filter((other) => other.id !== entry.id)
                              .map((other) => (
                                <option key={other.id} value={other.id}>
                                  {other.name}
                                </option>
                              ))}
                          </select>
                          <button
                            type="button"
                            ref={(element) => {
                              if (element) mergeButtons.current.set(entry.id, element)
                              else mergeButtons.current.delete(entry.id)
                            }}
                            disabled={!target}
                            onClick={() => setConfirmFromId(entry.id)}
                            className="rounded border border-muted px-3 py-1 disabled:opacity-50"
                          >
                            Merge
                          </button>
                        </div>
                        {confirmFromId === entry.id && target && (
                          <MergeConfirm
                            from={entry}
                            into={target}
                            onConfirm={async () => {
                              if (!(await merge(entry, target))) mergeButtons.current.get(entry.id)?.focus()
                            }}
                            onCancel={() => {
                              setConfirmFromId(null)
                              mergeButtons.current.get(entry.id)?.focus()
                            }}
                          />
                        )}
                      </>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </>
      )}
    </>
  )
}
