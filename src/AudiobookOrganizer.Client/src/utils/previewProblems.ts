import type { BookPreview, FilePreview } from '../types/preview'

/** Each problem that keeps a file from being approved, as text: missing tokens, the error, a duplicate or existing target. */
export function fileProblems(file: FilePreview): string[] {
  const problems: string[] = []
  if (file.missing.length > 0) problems.push(`Missing: ${file.missing.join(', ')}`)
  if (file.error) problems.push(file.error)
  if (file.duplicate) problems.push('Duplicate target')
  if (file.exists) problems.push('Already exists')
  return problems
}

/** Whether any of the book's files has a problem. */
export function hasProblems(book: BookPreview) {
  return book.files.some((file) => fileProblems(file).length > 0)
}
