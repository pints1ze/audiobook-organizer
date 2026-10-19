import { useEffect, useRef, useState } from 'react'
import { createGroup, excludeFiles, moveFilesToGroup, unresolveFiles } from '../../services/api'

export type FileListName = 'unresolved' | 'excluded'

export interface MoveTarget {
  id: number
  label: string
}

interface MoveBarProps {
  /** The select's id. */
  id: string
  /** The books the files can move to; "New book" and the file lists are offered after them. */
  targets: MoveTarget[]
  /** The list the files are in, which isn't offered as a destination. Omit for a book's files. */
  source?: FileListName
  /** The selected files, in list order. */
  fileIds: number[]
  disabled: boolean
  onMoveStart: () => void
  /** True when a move starts; false only once it succeeds. After a failure the parent ends saving itself. */
  onSaving: (saving: boolean) => void
  onMoved: (message: string) => void
  onFailed: () => void
}

const newBookValue = 'new'

const fileLists: { value: FileListName; label: string }[] = [
  { value: 'unresolved', label: 'Unresolved files' },
  { value: 'excluded', label: 'Excluded files' },
]

const buttonClass =
  'inline-flex items-center gap-1 rounded border border-muted px-2 py-1 text-xs font-medium disabled:opacity-50'

export default function MoveBar({
  id,
  targets,
  source,
  fileIds,
  disabled,
  onMoveStart,
  onSaving,
  onMoved,
  onFailed,
}: MoveBarProps) {
  const [destination, setDestination] = useState('')
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const listOptions = fileLists.filter((l) => l.value !== source)

  // Fall back to the first option when the chosen book is no longer listed.
  const destinationValue =
    destination === newBookValue ||
    listOptions.some((l) => l.value === destination) ||
    targets.some((t) => String(t.id) === destination)
      ? destination
      : targets.length > 0
        ? String(targets[0].id)
        : newBookValue

  async function moveSelected() {
    if (fileIds.length === 0) return
    const ids = [...fileIds]
    const chosen = destinationValue
    const target = targets.find((t) => String(t.id) === chosen)

    onMoveStart()
    onSaving(true)
    let ok = false
    try {
      const result =
        chosen === newBookValue
          ? await createGroup({ fileIds: ids, title: null, author: null })
          : chosen === 'unresolved'
            ? await unresolveFiles(ids)
            : chosen === 'excluded'
              ? await excludeFiles(ids)
              : await moveFilesToGroup(Number(chosen), ids)
      ok = result.ok
    } catch {
      ok = false
    }
    if (!mounted.current) return
    if (ok) {
      const count = `${ids.length} ${ids.length === 1 ? 'file' : 'files'}`
      onSaving(false)
      onMoved(
        chosen === newBookValue
          ? `Moved ${count} to a new book`
          : chosen === 'unresolved'
            ? `Moved ${count} to unresolved files`
            : chosen === 'excluded'
              ? `Excluded ${count}`
              : `Moved ${count} to ${target?.label ?? 'another book'}`,
      )
      return
    }
    onFailed()
  }

  return (
    <div className="mt-2 flex flex-wrap items-end gap-2">
      <div>
        <label htmlFor={id} className="block text-xs font-medium">
          Move selected to
        </label>
        <select
          id={id}
          value={destinationValue}
          onChange={(event) => setDestination(event.target.value)}
          disabled={disabled}
          className="mt-1 max-w-full rounded border border-muted bg-background px-2 py-1 text-sm"
        >
          {targets.map((t) => (
            <option key={t.id} value={String(t.id)}>
              {t.label}
            </option>
          ))}
          <option value={newBookValue}>New book</option>
          {listOptions.map((l) => (
            <option key={l.value} value={l.value}>
              {l.label}
            </option>
          ))}
        </select>
      </div>
      <button type="button" onClick={moveSelected} disabled={disabled || fileIds.length === 0} className={buttonClass}>
        Move
      </button>
    </div>
  )
}
