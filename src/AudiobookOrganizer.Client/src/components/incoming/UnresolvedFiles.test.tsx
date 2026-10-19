import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { flushSync } from 'react-dom'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createGroup, excludeFiles, moveFilesToGroup, unresolveFiles } from '../../services/api'
import type { GroupDetail, UnresolvedFile } from '../../types/groups'
import UnresolvedFiles from './UnresolvedFiles'

vi.mock('../../services/api', () => ({
  createGroup: vi.fn(),
  excludeFiles: vi.fn(),
  moveFilesToGroup: vi.fn(),
  unresolveFiles: vi.fn(),
}))

const moveFilesMock = vi.mocked(moveFilesToGroup)
const createGroupMock = vi.mocked(createGroup)
const excludeFilesMock = vi.mocked(excludeFiles)
const unresolveFilesMock = vi.mocked(unresolveFiles)

const files: UnresolvedFile[] = [
  { fileId: 7, relativePath: 'stray/track.mp3', durationMs: 125_000 },
  { fileId: 8, relativePath: 'loose.m4b', durationMs: null },
]

const targets = [
  { id: 2, label: 'Unknown (group 2)' },
  { id: 1, label: 'The Hobbit' },
]

const group: GroupDetail = {
  id: 1,
  title: 'The Hobbit',
  author: null,
  confidence: 'High',
  evidence: [],
  fileCount: 1,
  totalDurationMs: 0,
  userCorrected: true,
  files: [],
}

function renderList(props: Partial<Parameters<typeof UnresolvedFiles>[0]> = {}) {
  const handlers = { onMoveStart: vi.fn(), onMoved: vi.fn(), onFailed: vi.fn() }
  render(<UnresolvedFiles kind="unresolved" files={files} targets={targets} {...handlers} {...props} />)
  return handlers
}

describe('UnresolvedFiles', () => {
  beforeEach(() => {
    moveFilesMock.mockReset()
    createGroupMock.mockReset()
    excludeFilesMock.mockReset()
    unresolveFilesMock.mockReset()
  })

  it('keeps a file ticked straight after the list renders, before React runs its effects', async () => {
    // Outside act, React runs effects in a later task, as in the browser. The click goes in the microtask right after
    // the commit, before that task. The old reset-on-new-list effect then cleared the tick, so Move did nothing (the
    // flaky IncomingPage move test).
    const actEnvironment = globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }
    const previous = actEnvironment.IS_REACT_ACT_ENVIRONMENT
    actEnvironment.IS_REACT_ACT_ENVIRONMENT = false
    const container = document.body.appendChild(document.createElement('div'))
    const root = createRoot(container)
    try {
      const handlers = { onMoveStart: vi.fn(), onMoved: vi.fn(), onFailed: vi.fn() }
      const clicked = new Promise<HTMLInputElement>((resolve) => {
        const clickOnCommit = (element: HTMLDivElement | null) => {
          if (!element) return
          queueMicrotask(() => {
            const checkbox = within(element).getByRole('checkbox', { name: 'Select loose.m4b' }) as HTMLInputElement
            fireEvent.click(checkbox)
            resolve(checkbox)
          })
        }
        root.render(
          <div ref={clickOnCommit}>
            <UnresolvedFiles kind="unresolved" files={files} targets={targets} {...handlers} />
          </div>,
        )
      })

      const checkbox = await clicked
      await new Promise((resolve) => setTimeout(resolve, 50))

      expect(checkbox).toBeChecked()
    } finally {
      flushSync(() => root.unmount())
      container.remove()
      actEnvironment.IS_REACT_ACT_ENVIRONMENT = previous
    }
  })

  it('lists each file with its path, duration and a labelled checkbox', () => {
    renderList()
    const items = screen.getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('stray/track.mp3')
    expect(items[0]).toHaveTextContent('2:05')
    expect(items[1]).toHaveTextContent('loose.m4b')
    expect(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' })).not.toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Select loose.m4b' })).not.toBeChecked()
    const select = screen.getByLabelText('Move selected to') as HTMLSelectElement
    expect(select.id).toBe('unresolved-move-to')
    expect(Array.from(select.options).map((o) => o.textContent)).toEqual([
      'Unknown (group 2)',
      'The Hobbit',
      'New book',
      'Excluded files',
    ])
  })

  it('disables Move until a file is selected, and select all ticks every file then shows mixed', () => {
    renderList()
    const move = screen.getByRole('button', { name: 'Move' })
    expect(move).toBeDisabled()

    const all = screen.getByRole('checkbox', { name: 'Select all unresolved files' }) as HTMLInputElement
    fireEvent.click(all)
    expect(all).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Select loose.m4b' })).toBeChecked()
    expect(all.indeterminate).toBe(false)
    expect(move).toBeEnabled()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select loose.m4b' }))
    expect(all.indeterminate).toBe(true)
    expect(all).toBePartiallyChecked()

    fireEvent.click(all)
    expect(all).toBeChecked()
    fireEvent.click(all)
    expect(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' })).not.toBeChecked()
    expect(move).toBeDisabled()
  })

  it('moves the selected file to a book and reports it', async () => {
    let resolve: (value: Awaited<ReturnType<typeof moveFilesToGroup>>) => void = () => {}
    moveFilesMock.mockReturnValue(new Promise((r) => (resolve = r)))
    const handlers = renderList()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select loose.m4b' }))
    fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Move' }))

    expect(moveFilesMock).toHaveBeenCalledWith(1, [8])
    expect(handlers.onMoveStart).toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()
    expect(screen.getByLabelText('Move selected to')).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' })).toBeDisabled()

    resolve({ ok: true, group })
    await waitFor(() => expect(handlers.onMoved).toHaveBeenCalledWith('Moved 1 file to The Hobbit'))
    expect(createGroupMock).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('moves every file to a new book in list order', async () => {
    createGroupMock.mockResolvedValue({ ok: true, group })
    const handlers = renderList()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select loose.m4b' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' }))
    fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'new' } })
    fireEvent.click(screen.getByRole('button', { name: 'Move' }))

    await waitFor(() => expect(handlers.onMoved).toHaveBeenCalledWith('Moved 2 files to a new book'))
    expect(createGroupMock).toHaveBeenCalledWith({ fileIds: [7, 8], title: null, author: null })
    expect(moveFilesMock).not.toHaveBeenCalled()
  })

  it('clears the selection when the files change', () => {
    const handlers = { onMoveStart: vi.fn(), onMoved: vi.fn(), onFailed: vi.fn() }
    const { rerender } = render(<UnresolvedFiles kind="unresolved" files={files} targets={targets} {...handlers} />)
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' }))

    rerender(<UnresolvedFiles kind="unresolved" files={[files[0]]} targets={targets} {...handlers} />)
    expect(screen.getByRole('checkbox', { name: 'Select stray/track.mp3' })).not.toBeChecked()
    expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()
  })

  it.each([
    ['a 400', () => moveFilesMock.mockResolvedValue({ ok: false, errors: { fileIds: ['Unknown file.'] } })],
    ['a 404', () => moveFilesMock.mockResolvedValue({ ok: false, notFound: true })],
    ['a thrown error', () => moveFilesMock.mockRejectedValue(new Error('offline'))],
  ])('shows an alert and asks for a reload on %s', async (_name, arrange) => {
    arrange()
    const handlers = renderList()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select loose.m4b' }))
    fireEvent.click(screen.getByRole('button', { name: 'Move' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("The files couldn't be moved")
    expect(handlers.onFailed).toHaveBeenCalledTimes(1)
    expect(handlers.onMoved).not.toHaveBeenCalled()
    expect(screen.getByRole('checkbox', { name: 'Select loose.m4b' })).toBeEnabled()
  })

  it('excludes the selected unresolved file', async () => {
    excludeFilesMock.mockResolvedValue({ ok: true, list: { groups: [], unresolved: [], excluded: [] } })
    const handlers = renderList()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select loose.m4b' }))
    fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'excluded' } })
    fireEvent.click(screen.getByRole('button', { name: 'Move' }))

    await waitFor(() => expect(handlers.onMoved).toHaveBeenCalledWith('Excluded 1 file'))
    expect(excludeFilesMock).toHaveBeenCalledWith([8])
  })

  describe('the excluded kind', () => {
    it('labels its controls for excluded files and offers Unresolved files, not Excluded files', () => {
      renderList({ kind: 'excluded' })

      expect(screen.getByRole('checkbox', { name: 'Select all excluded files' })).toBeInTheDocument()
      expect(screen.queryByRole('checkbox', { name: 'Select all unresolved files' })).not.toBeInTheDocument()
      const select = screen.getByLabelText('Move selected to') as HTMLSelectElement
      expect(select.id).toBe('excluded-move-to')
      expect(Array.from(select.options).map((o) => o.textContent)).toEqual([
        'Unknown (group 2)',
        'The Hobbit',
        'New book',
        'Unresolved files',
      ])
      expect(screen.getByRole('checkbox', { name: 'Select loose.m4b' }).id).toBe('excluded-file-8')
    })

    it('sends the selected files back to unresolved (the undo)', async () => {
      unresolveFilesMock.mockResolvedValue({ ok: true, list: { groups: [], unresolved: [], excluded: [] } })
      const handlers = renderList({ kind: 'excluded' })

      fireEvent.click(screen.getByRole('checkbox', { name: 'Select all excluded files' }))
      fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'unresolved' } })
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      await waitFor(() => expect(handlers.onMoved).toHaveBeenCalledWith('Moved 2 files to unresolved files'))
      expect(unresolveFilesMock).toHaveBeenCalledWith([7, 8])
      expect(excludeFilesMock).not.toHaveBeenCalled()
    })
  })
})
