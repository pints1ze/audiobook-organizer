import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getGroup, reorderGroup } from '../../services/api'
import type { GroupDetail, GroupFile } from '../../types/groups'
import GroupFiles from './GroupFiles'

vi.mock('../../services/api', () => ({ getGroup: vi.fn(), reorderGroup: vi.fn() }))

const getGroupMock = vi.mocked(getGroup)
const reorderGroupMock = vi.mocked(reorderGroup)

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
  })

  it('loads and lists the files in order with path, disc and track, disabling the end moves', async () => {
    getGroupMock.mockResolvedValue(detail([a, b, c]))
    render(<GroupFiles groupId={9} onReordered={vi.fn()} />)

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
    render(<GroupFiles groupId={9} onReordered={vi.fn()} />)

    expect(await screen.findByRole('button', { name: 'Move book/CD1/01.mp3 down' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move book/CD2/01.mp3 up' })).toBeInTheDocument()
  })

  it('moves a file up, shows the new order, keeps focus and announces the move', async () => {
    getGroupMock.mockResolvedValue(detail([a, b, c]))
    const saved = detail([{ ...b, position: 1 }, { ...a, position: 2 }, c], true)
    let resolve: (value: Awaited<ReturnType<typeof reorderGroup>>) => void = () => {}
    reorderGroupMock.mockReturnValue(new Promise((r) => (resolve = r)))
    const onReordered = vi.fn()
    render(<GroupFiles groupId={9} onReordered={onReordered} />)

    const moveUp = await screen.findByRole('button', { name: 'Move b.mp3 up' })
    moveUp.focus()
    fireEvent.click(moveUp)

    expect(reorderGroupMock).toHaveBeenCalledWith(9, [2, 1, 3])
    screen.getAllByRole('button').forEach((button) => expect(button).toBeDisabled())

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
    render(<GroupFiles groupId={9} onReordered={vi.fn()} />)

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
    render(<GroupFiles groupId={9} onReordered={onReordered} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Move b.mp3 up' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be saved/)
    await waitFor(() => expect(getGroupMock).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Move b.mp3 up' })).toBeEnabled())
    expect(itemNames()).toEqual(['a.mp3', 'b.mp3', 'c.mp3'])
    expect(onReordered).not.toHaveBeenCalled()
  })

  it('shows an alert when the files cannot be loaded', async () => {
    getGroupMock.mockRejectedValue(new Error('boom'))
    render(<GroupFiles groupId={9} onReordered={vi.fn()} />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't be loaded/)
    expect(screen.queryByText('Loading files…')).not.toBeInTheDocument()
  })
})
