import { useCallback, useEffect, useRef, useState } from 'react'
import { ArrowDown, ArrowUp, CircleAlert } from 'lucide-react'
import { getGroup, reorderGroup } from '../../services/api'
import type { GroupDetail, GroupFile } from '../../types/groups'
import MoveBar, { type MoveTarget } from './MoveBar'

export type { MoveTarget }

type Direction = 'up' | 'down'

interface GroupFilesProps {
  groupId: number
  onReordered: (detail: GroupDetail) => void
  /** The other books the selected files can move to. */
  targets: MoveTarget[]
  onMoveStart: () => void
  onMoved: (message: string) => void
}

function fileName(relativePath: string) {
  return relativePath.slice(relativePath.lastIndexOf('/') + 1)
}

// The file name, or the whole path when another file in the group shares that name.
function displayNames(files: GroupFile[]) {
  const counts = new Map<string, number>()
  files.forEach((f) => counts.set(fileName(f.relativePath), (counts.get(fileName(f.relativePath)) ?? 0) + 1))
  return new Map(
    files.map((f) => {
      const name = fileName(f.relativePath)
      return [f.fileId, (counts.get(name) ?? 0) > 1 ? f.relativePath : name]
    }),
  )
}

const moveButtonClass =
  'inline-flex items-center gap-1 rounded border border-muted px-2 py-1 text-xs font-medium disabled:opacity-50'

export default function GroupFiles({ groupId, onReordered, targets, onMoveStart, onMoved }: GroupFilesProps) {
  const [files, setFiles] = useState<GroupFile[] | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)
  const [moveFailed, setMoveFailed] = useState(false)
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [saving, setSaving] = useState(false)
  const [announcement, setAnnouncement] = useState('')
  const [pendingFocus, setPendingFocus] = useState<{ fileId: number; direction: Direction } | null>(null)
  const buttons = useRef(new Map<string, HTMLButtonElement>())
  const mounted = useRef(false)
  const selectAll = useRef<HTMLInputElement>(null)

  const load = useCallback(() => {
    setLoadFailed(false)
    getGroup(groupId)
      .then((detail) => {
        if (!mounted.current) return
        setFiles(detail.files)
        setSelected(new Set())
      })
      .catch(() => {
        if (mounted.current) setLoadFailed(true)
      })
      .finally(() => {
        if (mounted.current) setSaving(false)
      })
  }, [groupId])

  useEffect(() => {
    mounted.current = true
    load()
    return () => {
      mounted.current = false
    }
  }, [load])

  // Keep focus with the moved file once the list has re-rendered; if its button is now disabled, use the other one.
  useEffect(() => {
    if (!pendingFocus || !files) return
    const index = files.findIndex((f) => f.fileId === pendingFocus.fileId)
    let direction = pendingFocus.direction
    if (direction === 'up' && index === 0) direction = 'down'
    else if (direction === 'down' && index === files.length - 1) direction = 'up'
    buttons.current.get(`${pendingFocus.fileId}-${direction}`)?.focus()
    setPendingFocus(null)
  }, [files, pendingFocus])

  async function move(index: number, direction: Direction) {
    if (!files) return
    const target = direction === 'up' ? index - 1 : index + 1
    const ids = files.map((f) => f.fileId)
    const movedId = ids[index]
    ;[ids[index], ids[target]] = [ids[target], ids[index]]
    const names = displayNames(files)

    setSaving(true)
    setSaveFailed(false)
    setMoveFailed(false)
    setAnnouncement('')
    let result
    try {
      result = await reorderGroup(groupId, ids)
    } catch {
      result = null
    }
    if (!mounted.current) return
    if (result?.ok) {
      const newFiles = result.group.files
      const position = newFiles.findIndex((f) => f.fileId === movedId) + 1
      setFiles(newFiles)
      setSaving(false)
      setAnnouncement(`Moved ${names.get(movedId)} to position ${position}`)
      setPendingFocus({ fileId: movedId, direction })
      onReordered(result.group)
      return
    }
    // The group may have changed or gone since it loaded, so show its current files.
    setSaveFailed(true)
    load()
  }

  const someSelected = selected.size > 0 && !!files && selected.size < files.length
  const allSelected = !!files && files.length > 0 && selected.size === files.length

  useEffect(() => {
    if (selectAll.current) selectAll.current.indeterminate = someSelected
  }, [someSelected, files])

  function toggle(fileId: number) {
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(fileId)) next.delete(fileId)
      else next.add(fileId)
      return next
    })
  }

  function toggleAll() {
    setSelected(allSelected || !files ? new Set() : new Set(files.map((f) => f.fileId)))
  }

  function handleMoveStart() {
    onMoveStart()
    setSaveFailed(false)
    setMoveFailed(false)
    setAnnouncement('')
  }

  function handleMoved(message: string) {
    setSelected(new Set())
    onMoved(message)
  }

  // The books may have changed since the list loaded, so show the current files.
  function handleMoveFailed() {
    setMoveFailed(true)
    load()
  }

  const names = files ? displayNames(files) : new Map<number, string>()

  return (
    <div className="mt-1">
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
      {saveFailed && (
        <p role="alert" className="mt-1 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
          The new order couldn't be saved. The files have been reloaded.
        </p>
      )}
      {moveFailed && (
        <p role="alert" className="mt-1 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
          The files couldn't be moved. The files have been reloaded.
        </p>
      )}
      {loadFailed && (
        <p role="alert" className="mt-1 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
          The files couldn't be loaded.
        </p>
      )}
      {!files && !loadFailed && <p className="mt-1 text-muted">Loading files…</p>}
      {files && files.length > 0 && (
        <label className="mt-1 flex items-center gap-2">
          <input
            ref={selectAll}
            type="checkbox"
            checked={allSelected}
            onChange={toggleAll}
            disabled={saving}
            className="size-4"
          />
          Select all files
        </label>
      )}
      {files && (
        <ol className="mt-1 list-decimal space-y-2 pl-6">
          {files.map((file, index) => {
            const name = names.get(file.fileId) ?? file.relativePath
            return (
              <li key={file.fileId}>
                <div className="flex items-start gap-2">
                  <input
                    id={`group-${groupId}-file-${file.fileId}`}
                    type="checkbox"
                    checked={selected.has(file.fileId)}
                    onChange={() => toggle(file.fileId)}
                    disabled={saving}
                    aria-label={`Select ${name}`}
                    className="mt-1 size-4 shrink-0"
                  />
                  <label htmlFor={`group-${groupId}-file-${file.fileId}`}>
                    <span className="font-medium break-all">{fileName(file.relativePath)}</span>
                  </label>
                </div>
                <div className="text-xs text-muted break-all">{file.relativePath}</div>
                {(file.disc !== null || file.track !== null) && (
                  <div className="text-xs text-muted">
                    {[file.disc !== null && `Disc ${file.disc}`, file.track !== null && `Track ${file.track}`]
                      .filter(Boolean)
                      .join(' · ')}
                  </div>
                )}
                <div className="mt-1 flex flex-wrap gap-2">
                  {(['up', 'down'] as const).map((direction) => (
                    <button
                      key={direction}
                      type="button"
                      ref={(element) => {
                        const key = `${file.fileId}-${direction}`
                        if (element) buttons.current.set(key, element)
                        else buttons.current.delete(key)
                      }}
                      onClick={() => move(index, direction)}
                      disabled={saving || (direction === 'up' ? index === 0 : index === files.length - 1)}
                      aria-label={`Move ${name} ${direction}`}
                      className={moveButtonClass}
                    >
                      {direction === 'up' ? (
                        <ArrowUp aria-hidden="true" className="size-3" />
                      ) : (
                        <ArrowDown aria-hidden="true" className="size-3" />
                      )}
                      {direction === 'up' ? 'Up' : 'Down'}
                    </button>
                  ))}
                </div>
              </li>
            )
          })}
        </ol>
      )}
      {files && files.length > 0 && (
        <MoveBar
          id={`group-${groupId}-move-to`}
          targets={targets}
          // Keep the list order so the files stay in sequence in their new book.
          fileIds={files.filter((f) => selected.has(f.fileId)).map((f) => f.fileId)}
          disabled={saving}
          onMoveStart={handleMoveStart}
          onSaving={setSaving}
          onMoved={handleMoved}
          onFailed={handleMoveFailed}
        />
      )}
    </div>
  )
}
