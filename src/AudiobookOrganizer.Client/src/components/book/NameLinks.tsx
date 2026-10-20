import { useEffect, useRef, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import { createEntry, getBookGenres, getBookPeople, updateEntry } from '../../services/api'
import type { CanonicalEntry, NameKind, NameLink, SaveEntryResult } from '../../types/names'

/** One list of names; `label` is null when the section has a single list, shown without an `h3`. */
type NameList = { id: string; label: string | null; links: NameLink[] }

type LoadState = { kind: 'loading' } | { kind: 'loaded'; lists: NameList[] } | { kind: 'failed' }

/** Which row an error belongs to: the list and the name's position in it, e.g. `narrators-0`. */
type RowKey = string

/** What differs between the People and Genres sections; everything else is shared. */
const kinds: Record<
  NameKind,
  { heading: string; loading: string; failed: string; empty: string; noun: string; load: (groupId: number) => Promise<NameList[]> }
> = {
  people: {
    heading: 'People',
    loading: 'Loading the people…',
    failed: "People links couldn't be loaded.",
    empty: 'No authors or narrators yet.',
    noun: 'person',
    load: async (groupId) => {
      const people = await getBookPeople(groupId)
      return [
        { id: 'authors', label: 'Authors', links: people.authors },
        { id: 'narrators', label: 'Narrators', links: people.narrators },
      ]
    },
  },
  genres: {
    heading: 'Genres',
    loading: 'Loading the genres…',
    failed: "Genre links couldn't be loaded.",
    empty: 'No genres yet.',
    noun: 'genre',
    load: async (groupId) => [{ id: 'genres', label: null, links: (await getBookGenres(groupId)).genres }],
  },
}

type Props = { kind: NameKind; groupId: number; reloadKey: number }

export default function NameLinks({ kind, groupId, reloadKey }: Props) {
  const text = kinds[kind]
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [busy, setBusy] = useState(false)
  const [rowError, setRowError] = useState<{ key: RowKey; text: string } | null>(null)
  const [statusMessage, setStatusMessage] = useState('')
  const [localReload, setLocalReload] = useState(0)
  const heading = useRef<HTMLHeadingElement>(null)
  const focusHeading = useRef(false)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    let current = true
    text
      .load(groupId)
      .then((lists) => {
        if (!current) return
        setState({ kind: 'loaded', lists })
        if (focusHeading.current) {
          focusHeading.current = false
          heading.current?.focus()
        }
      })
      .catch(() => {
        if (!current) return
        focusHeading.current = false
        setState({ kind: 'failed' })
      })
    return () => {
      current = false
    }
  }, [text, groupId, reloadKey, localReload])

  async function run(key: RowKey, name: string, button: HTMLButtonElement, action: () => Promise<SaveEntryResult>) {
    setBusy(true)
    setRowError(null)
    setStatusMessage('')
    let error: string | null = null
    let linkedTo: CanonicalEntry | null = null
    try {
      const result = await action()
      if (result.ok) linkedTo = result.entry
      else if ('notFound' in result) error = `That ${text.noun} no longer exists.`
      else error = Object.values(result.errors).flat()[0] ?? 'Check the name and try again.'
    } catch {
      error = "The link couldn't be saved. Check the connection and try again."
    }
    if (!mounted.current) return
    setBusy(false)
    if (linkedTo) {
      setStatusMessage(`${name} linked to ${linkedTo.name}.`)
      // The clicked button disappears on reload, so focus moves to the heading once the new list is in.
      focusHeading.current = true
      setLocalReload((value) => value + 1)
      return
    }
    setRowError({ key, text: error! })
    button.focus()
  }

  function link(key: RowKey, name: string, entry: CanonicalEntry, button: HTMLButtonElement) {
    return run(key, name, button, () => updateEntry(kind, entry.id, { name: entry.name, aliases: [...entry.aliases, name] }))
  }

  function add(key: RowKey, name: string, button: HTMLButtonElement) {
    return run(key, name, button, () => createEntry(kind, { name, aliases: [] }))
  }

  const lists = state.kind === 'loaded' ? state.lists : []
  const headingId = `${kind}-heading`
  const empty = state.kind === 'loaded' && lists.every((list) => list.links.length === 0)

  return (
    <section aria-labelledby={headingId} className="mt-6">
      <h2 id={headingId} ref={heading} tabIndex={-1} className="rounded text-xl font-semibold">
        {text.heading}
      </h2>
      <p role="status" className="mt-2">
        {statusMessage}
      </p>
      {state.kind === 'loading' && <p className="mt-2 text-muted">{text.loading}</p>}
      {state.kind === 'failed' && (
        <p className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          {text.failed}
        </p>
      )}
      {empty && <p className="mt-2">{text.empty}</p>}
      {!empty &&
        lists.map(({ id, label, links }) => (
          <div key={id} className="mt-4">
            {label && <h3 className="font-semibold">{label}</h3>}
            {links.length === 0 ? (
              <p className="mt-1 text-muted">None</p>
            ) : (
              <ul className="mt-1 divide-y divide-muted/30">
                {links.map((nameLink, index) => {
                  const key = `${id}-${index}`
                  const { name, entry, suggestions } = nameLink
                  const error = rowError?.key === key ? rowError.text : null
                  return (
                    <li key={key} className="py-2">
                      <span className="font-medium">{name}</span>
                      {entry ? (
                        <span className="ml-2 text-sm">Normalized: {entry.name}</span>
                      ) : (
                        <>
                          <span className="ml-2 text-sm text-muted">Not linked</span>
                          {suggestions.length > 0 && (
                            <ul className="mt-1 space-y-1">
                              {suggestions.map((suggestion) => (
                                <li key={suggestion.entry.id} className="flex flex-wrap items-center gap-2 text-sm">
                                  <span>
                                    {suggestion.entry.name} ({suggestion.reason})
                                  </span>
                                  <button
                                    type="button"
                                    disabled={busy}
                                    onClick={(event) => void link(key, name, suggestion.entry, event.currentTarget)}
                                    aria-label={`Link ${name} to ${suggestion.entry.name}`}
                                    className="rounded border border-foreground px-3 py-1 font-medium disabled:opacity-60"
                                  >
                                    Link to {suggestion.entry.name}
                                  </button>
                                </li>
                              ))}
                            </ul>
                          )}
                          <button
                            type="button"
                            disabled={busy}
                            onClick={(event) => void add(key, name, event.currentTarget)}
                            aria-label={`Add ${name} as a new ${text.noun}`}
                            className="mt-2 block rounded border border-foreground px-3 py-1 text-sm font-medium disabled:opacity-60"
                          >
                            Add as new {text.noun}
                          </button>
                        </>
                      )}
                      {error && (
                        <p role="alert" className="mt-2 flex items-center gap-2 text-sm text-destructive">
                          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
                          {error}
                        </p>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        ))}
    </section>
  )
}
