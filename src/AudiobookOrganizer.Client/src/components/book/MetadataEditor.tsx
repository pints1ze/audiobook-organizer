import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { getMetadata, saveMetadata } from '../../services/api'
import type {
  BookMetadata,
  BookMetadataFields,
  MetadataCandidate,
  MetadataField,
  UpdateBookMetadataFields,
} from '../../types/metadata'

type FieldKey = keyof BookMetadataFields

/** The 12 fields in display order; authors and narrators are lists. */
const fieldDefs: { key: FieldKey; label: string; list?: true }[] = [
  { key: 'title', label: 'Title' },
  { key: 'subtitle', label: 'Subtitle' },
  { key: 'authors', label: 'Authors', list: true },
  { key: 'narrators', label: 'Narrators', list: true },
  { key: 'series', label: 'Series' },
  { key: 'seriesPart', label: 'Series part' },
  { key: 'publisher', label: 'Publisher' },
  { key: 'releaseDate', label: 'Release date' },
  { key: 'language', label: 'Language' },
  { key: 'format', label: 'Format' },
  { key: 'asin', label: 'ASIN' },
  { key: 'isbn', label: 'ISBN' },
]

export const providerNames: Record<string, string> = { audible: 'Audible', openlibrary: 'Open Library' }

type Value = string | null | string[]

/** The candidate's value for each metadata field. */
function candidateValues(candidate: MetadataCandidate): Record<FieldKey, Value> {
  return {
    title: candidate.title,
    subtitle: candidate.subtitle,
    authors: candidate.authors,
    narrators: candidate.narrators,
    series: candidate.series?.name ?? null,
    seriesPart: candidate.series?.part ?? null,
    publisher: candidate.publisher,
    releaseDate: candidate.releaseDate,
    language: candidate.language,
    format: candidate.format,
    asin: candidate.asin,
    isbn: candidate.isbn,
  }
}

/** The value as shown: lists comma-separated, empty as "". */
function display(value: Value) {
  return Array.isArray(value) ? value.join(', ') : (value ?? '')
}

function originLabel({ origin, provider }: MetadataField<Value>) {
  if (origin === 'file') return 'From files'
  if (origin === 'user') return 'Edited by you'
  if (origin === 'provider') return `From ${provider ? (providerNames[provider] ?? provider) : 'a provider'}`
  return null
}

type Draft = Record<FieldKey, string>

/** The form's text for each field: lists one name per line. */
function toDraft(fields: BookMetadataFields): Draft {
  const draft = {} as Draft
  for (const { key } of fieldDefs) {
    const value = fields[key].value
    draft[key] = Array.isArray(value) ? value.join('\n') : (value ?? '')
  }
  return draft
}

/** Only the fields whose cleaned form value differs from the stored one: text trimmed, lists split by line without blanks. */
function changedFields(fields: BookMetadataFields, draft: Draft): UpdateBookMetadataFields {
  const changes: Record<string, string | string[]> = {}
  for (const { key, list } of fieldDefs) {
    const stored = fields[key].value
    if (list) {
      const names = draft[key]
        .split('\n')
        .map((name) => name.trim())
        .filter(Boolean)
      if (JSON.stringify(names) !== JSON.stringify(stored)) changes[key] = names
    } else {
      const text = draft[key].trim()
      if (text !== (stored ?? '')) changes[key] = text
    }
  }
  return changes as UpdateBookMetadataFields
}

const inputClass =
  'mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:border-2'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; metadata: BookMetadata } | { kind: 'failed' }

/** Why a save failed, beyond field errors: the book is gone, or a message to show. */
type SaveError = { kind: 'notFound' } | { kind: 'message'; text: string }

type Props = { groupId: number; match: MetadataCandidate | null; reloadKey: number }

export default function MetadataEditor({ groupId, match, reloadKey }: Props) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [draft, setDraft] = useState<Draft | null>(null)
  const [saving, setSaving] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saveError, setSaveError] = useState<SaveError | null>(null)
  const [savedMessage, setSavedMessage] = useState('')
  const heading = useRef<HTMLHeadingElement>(null)
  const editButton = useRef<HTMLButtonElement>(null)
  const focusEditButton = useRef(false)
  const inputs = useRef<Partial<Record<FieldKey, HTMLInputElement | HTMLTextAreaElement | null>>>({})
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const editing = draft !== null
  useEffect(() => {
    if (!editing && focusEditButton.current) {
      focusEditButton.current = false
      editButton.current?.focus()
    }
  }, [editing])

  useEffect(() => {
    let current = true
    getMetadata(groupId)
      .then((metadata) => {
        if (current) setState({ kind: 'loaded', metadata })
      })
      .catch(() => {
        if (current) setState({ kind: 'failed' })
      })
    return () => {
      current = false
    }
  }, [groupId, reloadKey])

  function startEditing(metadata: BookMetadata) {
    setDraft(toDraft(metadata.fields))
    setFieldErrors({})
    setSaveError(null)
    setSavedMessage('')
  }

  function closeForm() {
    focusEditButton.current = true
    setDraft(null)
  }

  async function handleSave(event: FormEvent<HTMLFormElement>, metadata: BookMetadata, values: Draft) {
    event.preventDefault()
    const changes = changedFields(metadata.fields, values)
    if (Object.keys(changes).length === 0) {
      closeForm()
      return
    }
    setSaving(true)
    setSaveError(null)
    let errors: Record<string, string[]> = {}
    let error: SaveError | null = null
    let saved: BookMetadata | null = null
    try {
      const response = await saveMetadata(groupId, changes)
      if (response.ok) saved = response.metadata
      else if ('notFound' in response) error = { kind: 'notFound' }
      else {
        errors = response.errors
        const other = Object.entries(errors).filter(([key]) => !fieldDefs.some((def) => def.key === key))
        if (other.length > 0 || Object.keys(errors).length === 0)
          error = { kind: 'message', text: other.flatMap(([, messages]) => messages)[0] ?? 'Check the values and try again.' }
      }
    } catch {
      error = { kind: 'message', text: "The metadata couldn't be saved. Check the connection and try again." }
    }
    if (!mounted.current) return
    setSaving(false)
    setFieldErrors(errors)
    setSaveError(error)
    if (saved) {
      setState({ kind: 'loaded', metadata: saved })
      setDraft(null)
      setSavedMessage('Metadata saved.')
      heading.current?.focus()
      return
    }
    const firstInvalid = fieldDefs.find((def) => errors[def.key]?.length)
    if (firstInvalid) inputs.current[firstInvalid.key]?.focus()
  }

  const proposed = match ? candidateValues(match) : null

  return (
    <section aria-labelledby="metadata-heading" className="mt-6">
      <h2 id="metadata-heading" ref={heading} tabIndex={-1} className="rounded text-xl font-semibold">
        Metadata
      </h2>
      <p role="status" className="mt-2">
        {savedMessage}
      </p>
      {state.kind === 'loading' && <p className="mt-2 text-muted">Loading the metadata…</p>}
      {state.kind === 'failed' && (
        <p className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The metadata couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' && draft && (
        <form onSubmit={(event) => handleSave(event, state.metadata, draft)} noValidate className="mt-2 space-y-4">
          {fieldDefs.map(({ key, label, list }) => {
            const id = `metadata-${key}`
            const errors = fieldErrors[key] ?? []
            const hasErrors = errors.length > 0
            const describedBy = [list && `${id}-hint`, hasErrors && `${id}-error`].filter(Boolean).join(' ') || undefined
            const common = {
              id,
              value: draft[key],
              'aria-invalid': hasErrors || undefined,
              'aria-describedby': describedBy,
              className: inputClass,
            }
            return (
              <div key={key}>
                <label htmlFor={id} className="font-medium">
                  {label}
                </label>
                {list ? (
                  <>
                    <p id={`${id}-hint`} className="text-sm text-muted">
                      One name per line
                    </p>
                    <textarea
                      {...common}
                      ref={(element) => {
                        inputs.current[key] = element
                      }}
                      rows={3}
                      onChange={(event) => setDraft({ ...draft, [key]: event.target.value })}
                    />
                  </>
                ) : (
                  <input
                    {...common}
                    ref={(element) => {
                      inputs.current[key] = element
                    }}
                    type="text"
                    autoComplete="off"
                    onChange={(event) => setDraft({ ...draft, [key]: event.target.value })}
                  />
                )}
                {hasErrors && (
                  <ul id={`${id}-error`} className="mt-1 text-sm text-destructive">
                    {errors.map((message) => (
                      <li key={message} className="flex items-center gap-1">
                        <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
                        {message}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )
          })}
          {saveError && (
            <p role="alert" className="flex items-center gap-2 text-destructive">
              <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
              {saveError.kind === 'notFound' ? (
                <span>
                  This book no longer exists.{' '}
                  <Link to="/incoming" className="rounded underline underline-offset-4">
                    Back to Incoming
                  </Link>
                </span>
              ) : (
                saveError.text
              )}
            </p>
          )}
          <div className="flex gap-4">
            <button
              type="submit"
              disabled={saving}
              className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
            >
              Save
            </button>
            <button type="button" onClick={closeForm} className="rounded border border-foreground px-4 py-2 font-medium">
              Cancel
            </button>
          </div>
        </form>
      )}
      {state.kind === 'loaded' && !draft && (
        <>
          <dl className="mt-2 grid grid-cols-[max-content_1fr] gap-x-4 gap-y-2">
            {fieldDefs.map(({ key, label }) => {
              const current = state.metadata.fields[key] as MetadataField<Value>
              const shown = display(current.value)
              const origin = originLabel(current)
              const proposal = proposed ? display(proposed[key]) : ''
              return (
                <div key={key} className="contents">
                  <dt className="font-medium">{label}</dt>
                  <dd>
                    {shown ? <span>{shown}</span> : <span className="text-muted">Not set</span>}
                    {origin && <span className="ml-2 text-sm text-muted">{origin}</span>}
                    {proposal && proposal !== shown && <span className="block text-sm">Match: {proposal}</span>}
                  </dd>
                </div>
              )
            })}
          </dl>
          <button
            type="button"
            ref={editButton}
            onClick={() => startEditing(state.metadata)}
            className="mt-4 rounded border border-foreground px-4 py-2 font-medium"
          >
            Edit metadata
          </button>
        </>
      )}
    </section>
  )
}
