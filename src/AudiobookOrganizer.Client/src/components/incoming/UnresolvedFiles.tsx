import { useEffect, useRef, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import type { UnresolvedFile } from '../../types/groups'
import { formatDuration } from '../../utils/formatDuration'
import MoveBar, { type MoveTarget } from './MoveBar'

interface UnresolvedFilesProps {
  files: UnresolvedFile[]
  /** Every book the selected files can move to. */
  targets: MoveTarget[]
  onMoveStart: () => void
  onMoved: (message: string) => void
  /** The move failed; the parent reloads the list. */
  onFailed: () => void
}

export default function UnresolvedFiles({ files, targets, onMoveStart, onMoved, onFailed }: UnresolvedFilesProps) {
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [saving, setSaving] = useState(false)
  const [moveFailed, setMoveFailed] = useState(false)
  const selectAll = useRef<HTMLInputElement>(null)

  // A reload brings a new list, so start again with nothing selected.
  useEffect(() => {
    setSelected(new Set())
  }, [files])

  const someSelected = selected.size > 0 && selected.size < files.length
  const allSelected = files.length > 0 && selected.size === files.length

  useEffect(() => {
    if (selectAll.current) selectAll.current.indeterminate = someSelected
  }, [someSelected])

  function toggle(fileId: number) {
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(fileId)) next.delete(fileId)
      else next.add(fileId)
      return next
    })
  }

  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(files.map((f) => f.fileId)))
  }

  function handleMoveStart() {
    setMoveFailed(false)
    onMoveStart()
  }

  function handleMoved(message: string) {
    setSelected(new Set())
    onMoved(message)
  }

  function handleFailed() {
    setMoveFailed(true)
    setSaving(false)
    onFailed()
  }

  return (
    <div className="mt-2">
      {moveFailed && (
        <p role="alert" className="flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
          The files couldn't be moved. The list has been reloaded.
        </p>
      )}
      <label className="mt-1 flex items-center gap-2">
        <input
          ref={selectAll}
          type="checkbox"
          checked={allSelected}
          onChange={toggleAll}
          disabled={saving}
          className="size-4"
        />
        Select all unresolved files
      </label>
      <ul className="mt-1 space-y-1">
        {files.map((file) => {
          const checkboxId = `unresolved-file-${file.fileId}`
          return (
            <li key={file.fileId} className="flex items-start gap-2">
              <input
                id={checkboxId}
                type="checkbox"
                checked={selected.has(file.fileId)}
                onChange={() => toggle(file.fileId)}
                disabled={saving}
                aria-label={`Select ${file.relativePath}`}
                className="mt-1 size-4 shrink-0"
              />
              <label htmlFor={checkboxId} className="flex min-w-0 flex-wrap gap-x-4">
                <span className="break-all">{file.relativePath}</span>
                <span className="text-muted">{formatDuration(file.durationMs)}</span>
              </label>
            </li>
          )
        })}
      </ul>
      <MoveBar
        id="unresolved-move-to"
        targets={targets}
        fileIds={files.filter((f) => selected.has(f.fileId)).map((f) => f.fileId)}
        disabled={saving}
        onMoveStart={handleMoveStart}
        onSaving={setSaving}
        onMoved={handleMoved}
        onFailed={handleFailed}
      />
    </div>
  )
}
