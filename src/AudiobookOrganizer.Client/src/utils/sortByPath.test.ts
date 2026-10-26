import { describe, expect, it } from 'vitest'
import { sortByPath } from './sortByPath'

const paths = (files: { relativePath: string }[]) => files.map((f) => f.relativePath)

describe('sortByPath', () => {
  it('compares digit runs by value', () => {
    const files = [{ relativePath: 'book/Part 10.m4b' }, { relativePath: 'book/Part 2.m4b' }]
    expect(paths(sortByPath(files))).toEqual(['book/Part 2.m4b', 'book/Part 10.m4b'])
  })

  it('orders by folder before file name', () => {
    const files = [{ relativePath: 'book/disc 2/track 1.mp3' }, { relativePath: 'book/disc 1/track 9.mp3' }]
    expect(paths(sortByPath(files))).toEqual(['book/disc 1/track 9.mp3', 'book/disc 2/track 1.mp3'])
  })

  it('ignores case', () => {
    const files = [{ relativePath: 'book/b.mp3' }, { relativePath: 'book/A.mp3' }, { relativePath: 'book/C.mp3' }]
    expect(paths(sortByPath(files))).toEqual(['book/A.mp3', 'book/b.mp3', 'book/C.mp3'])
  })

  it('keeps ties in their current order and leaves the input unchanged', () => {
    const files = [
      { relativePath: 'book/b.mp3', id: 1 },
      { relativePath: 'book/A.mp3', id: 2 },
      { relativePath: 'book/a.mp3', id: 3 },
    ]
    const before = [...files]
    expect(sortByPath(files).map((f) => f.id)).toEqual([2, 3, 1])
    expect(files).toEqual(before)
  })
})
