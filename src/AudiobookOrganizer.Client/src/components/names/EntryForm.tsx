import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { CircleAlert } from 'lucide-react'
import type { CanonicalEntry, EntryFieldErrors, EntryRequest, NameKind, SaveEntryResult } from '../../types/names'

const inputClass =
  'mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:border-2'

const aliasHints: Record<NameKind, string> = {
  people: 'Other spellings or pen names, one per line.',
  genres: 'Other names for this genre, one per line.',
}

interface EntryFormProps {
  kind: NameKind
  /** The entry being edited; omitted for the add form, which clears itself after a save. */
  initial?: CanonicalEntry
  onSave: (request: EntryRequest) => Promise<SaveEntryResult>
  onCancel?: () => void
  submitLabel: string
}

export default function EntryForm({ kind, initial, onSave, onCancel, submitLabel }: EntryFormProps) {
  const id = useId()
  const [name, setName] = useState(initial?.name ?? '')
  const [aliases, setAliases] = useState(initial?.aliases.join('\n') ?? '')
  const [errors, setErrors] = useState<EntryFieldErrors>({})
  const [failed, setFailed] = useState(false)
  const [saving, setSaving] = useState(false)
  const nameRef = useRef<HTMLInputElement>(null)
  const aliasesRef = useRef<HTMLTextAreaElement>(null)

  // The edit form opens in place of its row, so focus moves into it.
  const isEdit = initial !== undefined
  useEffect(() => {
    if (isEdit) nameRef.current?.focus()
  }, [isEdit])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setFailed(false)
    const request: EntryRequest = {
      name: name.trim(),
      aliases: aliases
        .split('\n')
        .map((alias) => alias.trim())
        .filter(Boolean),
    }
    try {
      const result = await onSave(request)
      if (result.ok) {
        setErrors({})
        if (!initial) {
          setName('')
          setAliases('')
        }
      } else if ('notFound' in result) {
        setFailed(true)
      } else {
        const fieldErrors = { name: result.errors.name, aliases: result.errors.aliases }
        setErrors(fieldErrors)
        if (fieldErrors.name?.length) nameRef.current?.focus()
        else if (fieldErrors.aliases?.length) aliasesRef.current?.focus()
        // A 400 with no field we show still needs feedback.
        else setFailed(true)
      }
    } catch {
      setFailed(true)
    } finally {
      setSaving(false)
    }
  }

  const nameId = `${id}-name`
  const aliasesId = `${id}-aliases`
  const nameErrors = errors.name ?? []
  const aliasErrors = errors.aliases ?? []

  return (
    <form onSubmit={handleSubmit} noValidate className="space-y-4">
      <div>
        <label htmlFor={nameId} className="font-medium">
          Name
        </label>
        <input
          id={nameId}
          ref={nameRef}
          type="text"
          value={name}
          onChange={(event) => setName(event.target.value)}
          aria-invalid={nameErrors.length > 0 || undefined}
          aria-describedby={nameErrors.length > 0 ? `${nameId}-error` : undefined}
          autoComplete="off"
          className={inputClass}
        />
        <FieldErrors id={`${nameId}-error`} errors={nameErrors} />
      </div>
      <div>
        <label htmlFor={aliasesId} className="font-medium">
          Aliases
        </label>
        <p id={`${aliasesId}-hint`} className="text-sm text-muted">
          {aliasHints[kind]}
        </p>
        <textarea
          id={aliasesId}
          ref={aliasesRef}
          rows={3}
          value={aliases}
          onChange={(event) => setAliases(event.target.value)}
          aria-invalid={aliasErrors.length > 0 || undefined}
          aria-describedby={
            aliasErrors.length > 0 ? `${aliasesId}-hint ${aliasesId}-error` : `${aliasesId}-hint`
          }
          className={inputClass}
        />
        <FieldErrors id={`${aliasesId}-error`} errors={aliasErrors} />
      </div>
      <div className="flex items-center gap-4">
        <button
          type="submit"
          disabled={saving}
          className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
        >
          {submitLabel}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel} className="rounded border border-muted px-4 py-2">
            Cancel
          </button>
        )}
      </div>
      {failed && (
        <p role="alert" className="flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5" />
          This entry couldn't be saved. Please try again.
        </p>
      )}
    </form>
  )
}

function FieldErrors({ id, errors }: { id: string; errors: string[] }) {
  if (errors.length === 0) return null
  return (
    <div id={id} role="alert" className="mt-1 text-sm text-destructive">
      <ul>
        {errors.map((error) => (
          <li key={error} className="flex items-center gap-1">
            <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
            {error}
          </li>
        ))}
      </ul>
    </div>
  )
}
