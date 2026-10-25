import { describe, expect, it } from 'vitest'
import type { BookPreview, FilePreview } from '../types/preview'
import { fileProblems, hasProblems } from './previewProblems'

function file(overrides: Partial<FilePreview> = {}): FilePreview {
  return {
    fileId: 1,
    sourcePath: '/incoming/Dune/01.mp3',
    relativePath: 'Frank Herbert/Dune/001. Dune.mp3',
    destinationPath: '/lib/Frank Herbert/Dune/001. Dune.mp3',
    missing: [],
    error: null,
    duplicate: false,
    exists: false,
    ...overrides,
  }
}

function bookWith(files: FilePreview[]): BookPreview {
  return { groupId: 1, libraryPath: '/lib', title: 'Dune', author: 'Frank Herbert', files }
}

describe('fileProblems', () => {
  it('is empty for a clean file', () => {
    expect(fileProblems(file())).toEqual([])
  })

  it('lists missing tokens, the error, a duplicate target and an existing destination', () => {
    expect(
      fileProblems(file({ missing: ['Series', 'Year'], error: 'Outside the library', duplicate: true, exists: true })),
    ).toEqual(['Missing: Series, Year', 'Outside the library', 'Duplicate target', 'Already exists'])
  })
})

describe('hasProblems', () => {
  it('is false for a clean book', () => {
    expect(hasProblems(bookWith([file(), file({ fileId: 2 })]))).toBe(false)
  })

  it('is true when any file has a problem', () => {
    expect(hasProblems(bookWith([file(), file({ fileId: 2, exists: true })]))).toBe(true)
  })
})
