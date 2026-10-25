import { useCallback, useEffect, useRef, useState, type FormEvent, type RefObject } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import GroupFiles, { type MoveTarget } from '../components/incoming/GroupFiles'
import UnresolvedFiles from '../components/incoming/UnresolvedFiles'
import { getGroups, updateGroup } from '../services/api'
import type { GroupConfidence, GroupDetail, GroupFieldErrors, GroupSummary, GroupsResponse } from '../types/groups'
import { formatDuration } from '../utils/formatDuration'

type IncomingState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: GroupsResponse }
  | { kind: 'error' }

// A subtle extra cue per level; the level is always shown as text.
const confidenceClass: Record<GroupConfidence, string> = {
  Low: 'font-semibold',
  Medium: 'font-medium',
  High: '',
}

const inputClass =
  'mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:border-2'

// Matches the API's limit; the API still validates.
const maxTextLength = 500

export default function IncomingPage() {
  const [state, setState] = useState<IncomingState>({ kind: 'loading' })
  const mounted = useRef(false)
  const loadedOnce = useRef(false)
  const loadErrorAlert = useRef<HTMLParagraphElement>(null)

  const load = useCallback(() => {
    return getGroups()
      .then((data) => {
        if (!mounted.current) return
        loadedOnce.current = true
        setState({ kind: 'loaded', data })
      })
      .catch(() => {
        if (mounted.current) setState({ kind: 'error' })
      })
  }, [])

  useEffect(() => {
    mounted.current = true
    load()
    return () => {
      mounted.current = false
    }
  }, [load])

  // A reload that fails mid-session replaces the list (and any focused control), so focus the error.
  useEffect(() => {
    if (state.kind === 'error' && loadedOnce.current) loadErrorAlert.current?.focus()
  }, [state.kind])

  function replaceGroup(group: GroupSummary) {
    setState((current) =>
      current.kind === 'loaded'
        ? {
            kind: 'loaded',
            data: { ...current.data, groups: current.data.groups.map((g) => (g.id === group.id ? group : g)) },
          }
        : current,
    )
  }

  return (
    <>
      <h1 className="text-2xl font-semibold">Incoming</h1>
      {state.kind === 'loading' && <p className="mt-4 text-muted">Loading…</p>}
      {state.kind === 'error' && (
        <p ref={loadErrorAlert} role="alert" tabIndex={-1} className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The incoming groups couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' && <IncomingContent data={state.data} onSaved={replaceGroup} onReload={load} />}
    </>
  )
}

interface IncomingContentProps {
  data: GroupsResponse
  onSaved: (group: GroupSummary) => void
  onReload: () => Promise<void>
}

function bookLabel(group: GroupSummary) {
  return group.title ?? `Unknown (group ${group.id})`
}

function IncomingContent({ data, onSaved, onReload }: IncomingContentProps) {
  const { groups, unresolved, excluded } = data
  const [editingId, setEditingId] = useState<number | null>(null)
  const [saveFailed, setSaveFailed] = useState(false)
  const saveFailedAlert = useRef<HTMLParagraphElement>(null)
  // Kept here, by group id, so a book's Files list stays open across its edit form.
  const [openFiles, setOpenFiles] = useState<Set<number>>(() => new Set())
  const editButtons = useRef(new Map<number, HTMLButtonElement>())
  const focusAfterClose = useRef<number | null>(null)
  const [moveMessage, setMoveMessage] = useState('')
  const moveStatus = useRef<HTMLParagraphElement>(null)

  // A move can remove or re-sort the row it came from, so focus goes to the page-level message.
  useEffect(() => {
    if (moveMessage) moveStatus.current?.focus()
  }, [moveMessage])

  // The edit form has gone, so focus the failure alert rather than leave it at the top of the page.
  useEffect(() => {
    if (saveFailed) saveFailedAlert.current?.focus()
  }, [saveFailed])

  // Return focus to the row's Edit button once its form has closed.
  useEffect(() => {
    if (editingId === null && focusAfterClose.current !== null) {
      editButtons.current.get(focusAfterClose.current)?.focus()
      focusAfterClose.current = null
    }
  }, [editingId])

  function openEditor(id: number) {
    setSaveFailed(false)
    setMoveMessage('')
    setEditingId(id)
  }

  function closeEditor(id: number) {
    focusAfterClose.current = id
    setEditingId(null)
  }

  function handleSaved(group: GroupSummary) {
    onSaved(group)
    closeEditor(group.id)
  }

  async function handleMoved(message: string) {
    await onReload()
    setMoveMessage(message)
  }

  // The group may be gone or renumbered after a rescan, so show the current list.
  function handleFailed() {
    setEditingId(null)
    setSaveFailed(true)
    onReload()
  }

  function setFilesOpen(id: number, open: boolean) {
    setOpenFiles((current) => {
      if (current.has(id) === open) return current
      const next = new Set(current)
      if (open) next.add(id)
      else next.delete(id)
      return next
    })
  }

  const allBooks = groups.map((g) => ({ id: g.id, label: bookLabel(g) }))

  const titleCounts = new Map<string, number>()
  for (const g of groups) {
    if (g.title) titleCounts.set(g.title, (titleCounts.get(g.title) ?? 0) + 1)
  }

  // Books that share a title get their id too, so every row control has a unique name.
  function uniqueName(group: GroupSummary) {
    if (!group.title) return `Unknown (group ${group.id})`
    return (titleCounts.get(group.title) ?? 0) > 1 ? `${group.title} (group ${group.id})` : group.title
  }

  function filesDisclosure(group: GroupSummary) {
    return (
      <FilesDisclosure
        group={group}
        open={openFiles.has(group.id)}
        onToggle={(open) => setFilesOpen(group.id, open)}
        targets={groups.filter((g) => g.id !== group.id).map((g) => ({ id: g.id, label: bookLabel(g) }))}
        onReordered={onSaved}
        onMoveStart={() => setMoveMessage('')}
        onMoved={handleMoved}
      />
    )
  }

  if (groups.length === 0 && unresolved.length === 0 && excluded.length === 0) {
    return (
      <p className="mt-4">
        No books found yet.{' '}
        <Link to="/dashboard" className="underline underline-offset-4">
          Run a scan from the Dashboard
        </Link>
      </p>
    )
  }

  return (
    <>
      <section aria-labelledby="groups-heading" className="mt-6">
        <h2 id="groups-heading" className="text-xl font-semibold">
          Proposed books ({groups.length})
        </h2>
        {saveFailed && (
          <p
            ref={saveFailedAlert}
            role="alert"
            tabIndex={-1}
            className="mt-2 flex items-center gap-2 rounded text-destructive"
          >
            <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
            That book couldn't be saved. The list has been reloaded.
          </p>
        )}
        {moveMessage && (
          <p ref={moveStatus} role="status" tabIndex={-1} className="mt-2 rounded">
            {moveMessage}
          </p>
        )}
        {groups.length === 0 ? (
          <p className="mt-2">No proposed books.</p>
        ) : (
          <div className="relative mt-2 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <caption className="sr-only">Proposed books, least confident first</caption>
              <thead>
                <tr className="border-b border-muted/30">
                  <th scope="col" className="p-2 font-medium">Title</th>
                  <th scope="col" className="p-2 font-medium">Author</th>
                  <th scope="col" className="p-2 font-medium">Files</th>
                  <th scope="col" className="p-2 font-medium">Duration</th>
                  <th scope="col" className="p-2 font-medium">Confidence</th>
                  <th scope="col" className="p-2 font-medium">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {groups.map((group) =>
                  group.id === editingId ? (
                    <tr key={group.id} className="border-b border-muted/30 align-top">
                      <td colSpan={6} className="p-2">
                        <EditGroupForm
                          group={group}
                          onSaved={handleSaved}
                          onCancel={() => closeEditor(group.id)}
                          onFailed={handleFailed}
                        />
                        {filesDisclosure(group)}
                      </td>
                    </tr>
                  ) : (
                    <tr key={group.id} className="border-b border-muted/30 align-top">
                      <td className="p-2">
                        <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                          <span>{group.title ?? 'Unknown'}</span>
                          {group.userCorrected && <span className="text-xs text-muted">Edited</span>}
                          <button
                            type="button"
                            ref={(element) => {
                              if (element) editButtons.current.set(group.id, element)
                              else editButtons.current.delete(group.id)
                            }}
                            onClick={() => openEditor(group.id)}
                            aria-label={`Edit ${uniqueName(group)}`}
                            className="rounded underline underline-offset-4"
                          >
                            Edit
                          </button>
                          <Link
                            to={`/books/${group.id}`}
                            aria-label={`Find metadata for ${uniqueName(group)}`}
                            className="rounded underline underline-offset-4"
                          >
                            Find metadata
                          </Link>
                        </div>
                      </td>
                      <td className="p-2">{group.author ?? 'Unknown'}</td>
                      <td className="p-2">{group.fileCount}</td>
                      <td className="p-2 whitespace-nowrap">{formatDuration(group.totalDurationMs)}</td>
                      <td className={`p-2 ${confidenceClass[group.confidence]}`}>{group.confidence}</td>
                      <td className="p-2">
                        <details>
                          <summary className="cursor-pointer rounded">Evidence</summary>
                          <ul className="mt-1 list-disc pl-5">
                            {group.evidence.map((item, index) => (
                              <li key={index}>{item}</li>
                            ))}
                          </ul>
                        </details>
                        {filesDisclosure(group)}
                      </td>
                    </tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section aria-labelledby="unresolved-heading" className="mt-6">
        <h2 id="unresolved-heading" className="text-xl font-semibold">
          Unresolved files ({unresolved.length})
        </h2>
        <p className="text-sm text-muted">These files aren't in a book yet.</p>
        {unresolved.length === 0 ? (
          <p className="mt-2">None.</p>
        ) : (
          <UnresolvedFiles
            kind="unresolved"
            files={unresolved}
            targets={allBooks}
            onMoveStart={() => setMoveMessage('')}
            onMoved={handleMoved}
            onFailed={onReload}
          />
        )}
      </section>

      <section aria-labelledby="excluded-heading" className="mt-6">
        <h2 id="excluded-heading" className="text-xl font-semibold">
          Excluded files ({excluded.length})
        </h2>
        <p className="text-sm text-muted">Excluded files stay out of books until you send them back.</p>
        {excluded.length === 0 ? (
          <p className="mt-2">None.</p>
        ) : (
          <UnresolvedFiles
            kind="excluded"
            files={excluded}
            targets={allBooks}
            onMoveStart={() => setMoveMessage('')}
            onMoved={handleMoved}
            onFailed={onReload}
          />
        )}
      </section>
    </>
  )
}

interface FilesDisclosureProps {
  group: GroupSummary
  open: boolean
  onToggle: (open: boolean) => void
  targets: MoveTarget[]
  onReordered: (group: GroupSummary) => void
  onMoveStart: () => void
  onMoved: (message: string) => void
}

// GroupFiles mounts only while open, so the files load when the user asks for them.
function FilesDisclosure({ group, open, onToggle, targets, onReordered, onMoveStart, onMoved }: FilesDisclosureProps) {
  function handleReordered(detail: GroupDetail) {
    // Drop the files so the row keeps a plain summary.
    const { files: _files, ...summary } = detail
    onReordered(summary)
  }

  return (
    <details className="mt-2" open={open} onToggle={(event) => onToggle(event.currentTarget.open)}>
      <summary className="cursor-pointer rounded">Files ({group.fileCount})</summary>
      {/* Keyed by the count so the list reloads when files move in or out of this book. */}
      {open && (
        <GroupFiles
          key={group.fileCount}
          groupId={group.id}
          onReordered={handleReordered}
          targets={targets}
          onMoveStart={onMoveStart}
          onMoved={onMoved}
        />
      )}
    </details>
  )
}

interface EditGroupFormProps {
  group: GroupSummary
  onSaved: (group: GroupSummary) => void
  onCancel: () => void
  onFailed: () => void
}

function EditGroupForm({ group, onSaved, onCancel, onFailed }: EditGroupFormProps) {
  const [title, setTitle] = useState(group.title ?? '')
  const [author, setAuthor] = useState(group.author ?? '')
  const [errors, setErrors] = useState<GroupFieldErrors>({})
  const [saving, setSaving] = useState(false)
  const titleRef = useRef<HTMLInputElement>(null)
  const authorRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    titleRef.current?.focus()
  }, [])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    let result
    try {
      result = await updateGroup(group.id, { title, author })
    } catch {
      onFailed()
      return
    }
    if (result.ok) {
      onSaved(result.group)
      return
    }
    if ('notFound' in result) {
      onFailed()
      return
    }
    const fieldErrors: GroupFieldErrors = { title: result.errors.title, author: result.errors.author }
    // A 400 with no field we show (e.g. a malformed body) still needs feedback.
    if (!fieldErrors.title?.length && !fieldErrors.author?.length) {
      onFailed()
      return
    }
    setErrors(fieldErrors)
    setSaving(false)
    if (fieldErrors.title?.length) titleRef.current?.focus()
    else authorRef.current?.focus()
  }

  return (
    <form
      onSubmit={handleSubmit}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault()
          onCancel()
        }
      }}
      noValidate
      className="flex flex-wrap items-start gap-4"
    >
      <TextField
        id={`group-${group.id}-title`}
        label="Title"
        value={title}
        onChange={setTitle}
        errors={errors.title}
        inputRef={titleRef}
      />
      <TextField
        id={`group-${group.id}-author`}
        label="Author"
        value={author}
        onChange={setAuthor}
        errors={errors.author}
        inputRef={authorRef}
      />
      <div className="flex items-center gap-2 self-end">
        <button
          type="submit"
          disabled={saving}
          className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
        >
          Save
        </button>
        <button type="button" onClick={onCancel} className="rounded border border-muted px-4 py-2 font-medium">
          Cancel
        </button>
      </div>
    </form>
  )
}

interface TextFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  errors: string[] | undefined
  inputRef: RefObject<HTMLInputElement | null>
}

function TextField({ id, label, value, onChange, errors, inputRef }: TextFieldProps) {
  const hasErrors = !!errors?.length
  const errorId = `${id}-error`
  return (
    <div className="min-w-0 grow basis-48">
      <label htmlFor={id} className="font-medium">
        {label}
      </label>
      <input
        id={id}
        ref={inputRef}
        type="text"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        maxLength={maxTextLength}
        aria-invalid={hasErrors || undefined}
        aria-describedby={hasErrors ? errorId : undefined}
        autoComplete="off"
        className={inputClass}
      />
      {hasErrors && (
        <ul id={errorId} className="mt-1 text-sm text-destructive">
          {errors.map((error) => (
            <li key={error} className="flex items-center gap-1">
              <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
              {error}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
