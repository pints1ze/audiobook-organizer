// Mirrors Models/Api/PreviewDtos.cs.

export interface FilePreview {
  fileId: number
  sourcePath: string
  relativePath: string | null
  destinationPath: string | null
  missing: string[]
  error: string | null
  duplicate: boolean
  exists: boolean
}

export interface BookPreview {
  groupId: number
  libraryPath: string | null
  title: string | null
  author: string | null
  files: FilePreview[]
}

export interface PreviewCounts {
  files: number
  missing: number
  errors: number
  duplicates: number
  existing: number
}

export interface LibraryPreview {
  libraryPath: string | null
  books: BookPreview[]
  counts: PreviewCounts
}
