import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createGroup, excludeFiles, getGroup, moveFilesToGroup, reorderGroup, unresolveFiles } from '../../services/api'
import type { GroupDetail, GroupFile } from '../../types/groups'
import GroupFiles from './GroupFiles'

vi.mock('../../services/api', () => ({
  createGroup: vi.fn(),
  excludeFiles: vi.fn(),
  getGroup: vi.fn(),
  moveFilesToGroup: vi.fn(),
  reorderGroup: vi.fn(),
  unresolveFiles: vi.fn(),
}))

const getGroupMock = vi.mocked(getGroup)
const reorderGroupMock = vi.mocked(reorderGroup)
const moveFilesMock = vi.mocked(moveFilesToGroup)
const createGroupMock = vi.mocked(createGroup)
const excludeFilesMock = vi.mocked(excludeFiles)
const unresolveFilesMock = vi.mocked(unresolveFiles)

function file(fileId: number, name: string, position: number, extra: Partial<GroupFile> = {}): GroupFile {
  return { fileId, relativePath: `book/${name}`, position, disc: null, track: null, durationMs: 1000, ...extra }
}

const a = file(1, 'a.mp3', 1, { disc: 1, track: 1 })
const b = file(2, 'b.mp3', 2, { track: 2 })
const c = file(3, 'c.mp3', 3)

function detail(files: GroupFile[], userCorrected = false): GroupDetail {
  return {
    id: 9,
    title: 'Book',
    author: 'Author',
    confidence: 'High',
    evidence: [],
    fileCount: files.length,
    totalDurationMs: 3000,
    userCorrected,
    files,
  }
}

function itemNames() {
  return within(screen.getByRole('list')).getAllByRole('listitem').map((li) => li.querySelector('span')?.textContent)
}

describe('GroupFiles', () => {
  beforeEach(() => {
    getGroupMock.mockReset()
    reorderGroupMock.mockReset()
    moveFilesMock.mockReset()
    createGroupMock.mockReset()
    excludeFilesMock.mockReset()
    unresolveFilesMock.mockReset()
  })

  it('loads and lists the files in order with path, disc and track, disabling the end moves', async () => {
    getGroupMock.mockResolvedValue(detail([a, b, c]))
    render(<GroupFiles groupId={9} onReordered={vi.fn()} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

    expect(screen.getByText('Loading files…')).toBeInTheDocument()
    const list = await screen.findByRole('list')
    expect(list.tagName).toBe('OL')
    expect(getGroupMock).toHaveBeenCalledWith(9)
    expect(itemNames()).toEqual(['a.mp3', 'b.mp3', 'c.mp3'])

    const items = within(list).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('book/a.mp3')
    expect(items[0]).toHaveTextContent('Disc 1')
    expect(items[0]).toHaveTextContent('Track 1')
    expect(items[1]).toHaveTextContent('Track 2')
    expect(items[1]).not.toHaveTextContent('Disc')

    expect(screen.getByRole('button', { name: 'Move a.mp3 up' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Move a.mp3 down' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Move c.mp3 up' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Move c.mp3 down' })).toBeDisabled()
  })

  it('names buttons by path when two files share a file name', async () => {
    getGroupMock.mockResolvedValue(
      detail([file(1, 'CD1/01.mp3', 1), file(2, 'CD2/01.mp3', 2)]),
    )
    render(<GroupFiles groupId={9} onReordered={vi.fn()} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

    expect(await screen.findByRole('button', { name: 'Move book/CD1/01.mp3 down' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move book/CD2/01.mp3 up' })).toBeInTheDocument()
  })

  it('moves a file up, shows the new order, keeps focus and announces the move', async () => {
    getGroupMock.mockResolvedValue(detail([a, b, c]))
    const saved = detail([{ ...b, position: 1 }, { ...a, position: 2 }, c], true)
    let resolve: (value: Awaited<ReturnType<typeof reorderGroup>>) => void = () => {}
    reorderGroupMock.mockReturnValue(new Promise((r) => (resolve = r)))
    const onReordered = vi.fn()
    render(<GroupFiles groupId={9} onReordered={onReordered} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

    const moveUp = await screen.findByRole('button', { name: 'Move b.mp3 up' })
    moveUp.focus()
    fireEvent.click(moveUp)

    expect(reorderGroupMock).toHaveBeenCalledWith(9, [2, 1, 3])
    screen.getAllByRole('button', { name: /^Move .* (up|down)$/ }).forEach((button) => expect(button).toBeDisabled())

    resolve({ ok: true, group: saved })

    await waitFor(() => expect(itemNames()).toEqual(['b.mp3', 'a.mp3', 'c.mp3']))
    expect(onReordered).toHaveBeenCalledWith(saved)
    expect(screen.getByText('Moved b.mp3 to position 1')).toBeInTheDocument()
    expect(screen.getByText('Moved b.mp3 to position 1')).toHaveAttribute('aria-live', 'polite')
    // b is now first, so its Move up is disabled and focus goes to its Move down.
    expect(screen.getByRole('button', { name: 'Move b.mp3 up' })).toBeDisabled()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Move b.mp3 down' })).toHaveFocus())
  })

  it('keeps focus on the same button when it is still enabled', async () => {
    getGroupMock.mockResolvedValue(detail([a, b, c]))
    reorderGroupMock.mockResolvedValue({
      ok: true,
      group: detail([a, { ...c, position: 2 }, { ...b, position: 3 }], true),
    })
    render(<GroupFiles groupId={9} onReordered={vi.fn()} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Move c.mp3 up' }))

    await waitFor(() => expect(itemNames()).toEqual(['a.mp3', 'c.mp3', 'b.mp3']))
    expect(reorderGroupMock).toHaveBeenCalledWith(9, [1, 3, 2])
    expect(screen.getByText('Moved c.mp3 to position 2')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Move c.mp3 up' })).toHaveFocus())
  })

  it.each([
    ['a 400', () => reorderGroupMock.mockResolvedValue({ ok: false, errors: { fileIds: ['bad'] } })],
    ['a 404', () => reorderGroupMock.mockResolvedValue({ ok: false, notFound: true })],
    ['a thrown error', () => reorderGroupMock.mockRejectedValue(new Error('offline'))],
  ])('shows an alert and reloads the files after %s', async (_, arrange) => {
    getGroupMock.mockResolvedValue(detail([a, b, c]))
    arrange()
    const onReordered = vi.fn()
    render(<GroupFiles groupId={9} onReordered={onReordered} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Move b.mp3 up' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be saved/)
    await waitFor(() => expect(getGroupMock).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Move b.mp3 up' })).toBeEnabled())
    expect(itemNames()).toEqual(['a.mp3', 'b.mp3', 'c.mp3'])
    expect(onReordered).not.toHaveBeenCalled()
  })

  describe('sorting by path', () => {
    const p10 = file(1, 'part 10.m4b', 1)
    const p2 = file(2, 'part 2.m4b', 2)
    const p1 = file(3, 'part 1.m4b', 3)

    it('saves the files in path order, shows it, keeps focus and announces it', async () => {
      getGroupMock.mockResolvedValue(detail([p10, p2, p1]))
      const saved = detail([{ ...p1, position: 1 }, { ...p2, position: 2 }, { ...p10, position: 3 }], true)
      let resolve: (value: Awaited<ReturnType<typeof reorderGroup>>) => void = () => {}
      reorderGroupMock.mockReturnValue(new Promise((r) => (resolve = r)))
      const onReordered = vi.fn()
      render(<GroupFiles groupId={9} onReordered={onReordered} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

      const sort = await screen.findByRole('button', { name: 'Sort by path' })
      sort.focus()
      fireEvent.click(sort)

      expect(reorderGroupMock).toHaveBeenCalledWith(9, [3, 2, 1])
      expect(sort).toHaveAttribute('aria-disabled', 'true')
      expect(sort).toHaveFocus()
      fireEvent.click(sort)
      expect(reorderGroupMock).toHaveBeenCalledTimes(1)
      screen.getAllByRole('button', { name: /^Move .* (up|down)$/ }).forEach((button) => expect(button).toBeDisabled())

      resolve({ ok: true, group: saved })

      await waitFor(() => expect(itemNames()).toEqual(['part 1.m4b', 'part 2.m4b', 'part 10.m4b']))
      expect(onReordered).toHaveBeenCalledWith(saved)
      expect(screen.getByText('Sorted 3 files by path.')).toHaveAttribute('aria-live', 'polite')
      expect(screen.getByRole('button', { name: 'Sort by path' })).not.toHaveAttribute('aria-disabled')
      expect(screen.getByRole('button', { name: 'Sort by path' })).toHaveFocus()
    })

    it('sends nothing and says so when the files are already in path order', async () => {
      getGroupMock.mockResolvedValue(detail([p1, p2, p10]))
      const onReordered = vi.fn()
      render(<GroupFiles groupId={9} onReordered={onReordered} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

      fireEvent.click(await screen.findByRole('button', { name: 'Sort by path' }))

      expect(await screen.findByText('The files are already sorted by path.')).toHaveAttribute('aria-live', 'polite')
      expect(reorderGroupMock).not.toHaveBeenCalled()
      expect(onReordered).not.toHaveBeenCalled()
    })

    it('is not shown for a book with one file', async () => {
      getGroupMock.mockResolvedValue(detail([p1]))
      render(<GroupFiles groupId={9} onReordered={vi.fn()} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

      await screen.findByRole('list')
      expect(screen.queryByRole('button', { name: 'Sort by path' })).not.toBeInTheDocument()
    })

    it('shows the save alert and reloads the files when the save is rejected', async () => {
      getGroupMock.mockResolvedValue(detail([p10, p2, p1]))
      reorderGroupMock.mockRejectedValue(new Error('offline'))
      const onReordered = vi.fn()
      render(<GroupFiles groupId={9} onReordered={onReordered} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

      fireEvent.click(await screen.findByRole('button', { name: 'Sort by path' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be saved/)
      await waitFor(() => expect(getGroupMock).toHaveBeenCalledTimes(2))
      await waitFor(() => expect(screen.getByRole('button', { name: 'Sort by path' })).not.toHaveAttribute('aria-disabled'))
      expect(itemNames()).toEqual(['part 10.m4b', 'part 2.m4b', 'part 1.m4b'])
      expect(onReordered).not.toHaveBeenCalled()
    })
  })

  it('shows an alert when the files cannot be loaded', async () => {
    getGroupMock.mockRejectedValue(new Error('boom'))
    render(<GroupFiles groupId={9} onReordered={vi.fn()} targets={[]} onMoveStart={vi.fn()} onMoved={vi.fn()} />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be loaded/)
    expect(screen.queryByText('Loading files…')).not.toBeInTheDocument()
  })
  describe('moving selected files', () => {
    const targets = [
      { id: 4, label: 'Other book' },
      { id: 6, label: 'Unknown (group 6)' },
    ]

    function renderMove(onMoved = vi.fn(), onMoveStart = vi.fn()) {
      render(
        <GroupFiles groupId={9} onReordered={vi.fn()} targets={targets} onMoveStart={onMoveStart} onMoved={onMoved} />,
      )
      return { onMoved, onMoveStart }
    }

    function checkbox(name: string) {
      return screen.getByRole('checkbox', { name }) as HTMLInputElement
    }

    it('labels each checkbox and the select, and disables Move with nothing selected', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      renderMove()

      expect(await screen.findByRole('checkbox', { name: 'Select a.mp3' })).not.toBeChecked()
      expect(checkbox('Select b.mp3')).not.toBeChecked()
      expect(checkbox('Select c.mp3')).not.toBeChecked()
      const select = screen.getByLabelText('Move selected to') as HTMLSelectElement
      expect(select.tagName).toBe('SELECT')
      expect(Array.from(select.options).map((o) => o.textContent)).toEqual([
        'Other book',
        'Unknown (group 6)',
        'New book',
        'Unresolved files',
        'Excluded files',
      ])
      expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()

      fireEvent.click(checkbox('Select b.mp3'))
      expect(screen.getByRole('button', { name: 'Move' })).toBeEnabled()
    })

    it('"Select all files" selects every file and shows mixed after one is unticked', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      renderMove()

      const all = (await screen.findByRole('checkbox', { name: 'Select all files' })) as HTMLInputElement
      expect(all.indeterminate).toBe(false)
      fireEvent.click(all)

      expect(all).toBeChecked()
      ;['a.mp3', 'b.mp3', 'c.mp3'].forEach((name) => expect(checkbox(`Select ${name}`)).toBeChecked())

      fireEvent.click(checkbox('Select b.mp3'))
      expect(all.indeterminate).toBe(true)
      expect(all).toBePartiallyChecked()

      fireEvent.click(all)
      ;['a.mp3', 'b.mp3', 'c.mp3'].forEach((name) => expect(checkbox(`Select ${name}`)).toBeChecked())
      fireEvent.click(all)
      ;['a.mp3', 'b.mp3', 'c.mp3'].forEach((name) => expect(checkbox(`Select ${name}`)).not.toBeChecked())
      expect(all.indeterminate).toBe(false)
    })

    it('moves a selected file to another book and reports the move', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      let resolve: (value: Awaited<ReturnType<typeof moveFilesToGroup>>) => void = () => {}
      moveFilesMock.mockReturnValue(new Promise((r) => (resolve = r)))
      const { onMoved, onMoveStart } = renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select b.mp3' }))
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      expect(onMoveStart).toHaveBeenCalled()
      expect(moveFilesMock).toHaveBeenCalledWith(4, [2])
      expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()
      expect(screen.getByLabelText('Move selected to')).toBeDisabled()

      resolve({ ok: true, group: { ...detail([b]), id: 4, title: 'Other book' } })

      await waitFor(() => expect(onMoved).toHaveBeenCalledWith('Moved 1 file to Other book'))
      expect(createGroupMock).not.toHaveBeenCalled()
    })

    it('moves files to a new book in list order', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      createGroupMock.mockResolvedValue({ ok: true, group: { ...detail([a, c]), id: 10, title: null } })
      const { onMoved } = renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select c.mp3' }))
      fireEvent.click(checkbox('Select a.mp3'))
      fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'new' } })
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      await waitFor(() => expect(onMoved).toHaveBeenCalledWith('Moved 2 files to a new book'))
      expect(createGroupMock).toHaveBeenCalledWith({ fileIds: [1, 3], title: null, author: null })
      expect(moveFilesMock).not.toHaveBeenCalled()
    })

    const list = { groups: [], unresolved: [], excluded: [] }

    it('excludes a selected file and reports it', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      excludeFilesMock.mockResolvedValue({ ok: true, list })
      const { onMoved } = renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select b.mp3' }))
      fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'excluded' } })
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      await waitFor(() => expect(onMoved).toHaveBeenCalledWith('Excluded 1 file'))
      expect(excludeFilesMock).toHaveBeenCalledWith([2])
      expect(moveFilesMock).not.toHaveBeenCalled()
      expect(unresolveFilesMock).not.toHaveBeenCalled()
    })

    it('sends a selected file to unresolved and reports it', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      unresolveFilesMock.mockResolvedValue({ ok: true, list })
      const { onMoved } = renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select b.mp3' }))
      fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'unresolved' } })
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      await waitFor(() => expect(onMoved).toHaveBeenCalledWith('Moved 1 file to unresolved files'))
      expect(unresolveFilesMock).toHaveBeenCalledWith([2])
      expect(excludeFilesMock).not.toHaveBeenCalled()
    })

    it('shows the alert and reloads when excluding is rejected', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      excludeFilesMock.mockResolvedValue({ ok: false, errors: { fileIds: ['bad'] } })
      const { onMoved } = renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select a.mp3' }))
      fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'excluded' } })
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be moved/)
      await waitFor(() => expect(getGroupMock).toHaveBeenCalledTimes(2))
      expect(onMoved).not.toHaveBeenCalled()
    })

    it.each([
      ['a 400', () => moveFilesMock.mockResolvedValue({ ok: false, errors: { fileIds: ['bad'] } })],
      ['a 404', () => moveFilesMock.mockResolvedValue({ ok: false, notFound: true })],
      ['a thrown error', () => moveFilesMock.mockRejectedValue(new Error('offline'))],
    ])('shows an alert and reloads the files after %s', async (_, arrange) => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      arrange()
      const { onMoved } = renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select b.mp3' }))
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be moved/)
      await waitFor(() => expect(getGroupMock).toHaveBeenCalledTimes(2))
      await waitFor(() => expect(checkbox('Select b.mp3')).not.toBeChecked())
      expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()
      expect(screen.getByLabelText('Move selected to')).toBeEnabled()
      expect(onMoved).not.toHaveBeenCalled()
    })

    it('shows the alert when creating a new book is rejected', async () => {
      getGroupMock.mockResolvedValue(detail([a, b, c]))
      createGroupMock.mockResolvedValue({ ok: false, errors: { fileIds: ['bad'] } })
      renderMove()

      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select a.mp3' }))
      fireEvent.change(screen.getByLabelText('Move selected to'), { target: { value: 'new' } })
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be moved/)
      await waitFor(() => expect(getGroupMock).toHaveBeenCalledTimes(2))
    })
  })
})
