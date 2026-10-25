/** A series and the book's place in it. Part stays a string ("1", "1.5" or empty). */
export interface SeriesRef {
  name: string
  part: string | null
}

/** A book a metadata provider proposes. Provider is e.g. "audible" or "openlibrary". */
export interface MetadataCandidate {
  provider: string
  asin: string | null
  title: string | null
  subtitle: string | null
  authors: string[]
  narrators: string[]
  series: SeriesRef | null
  publisher: string | null
  releaseDate: string | null
  language: string | null
  runtimeMinutes: number | null
  format: string | null
  coverUrl: string | null
  isbn: string | null
}

/** Candidates from the first provider that found any, plus the errors of providers that failed before it. */
export interface MetadataSearchResult {
  candidates: MetadataCandidate[]
  providerErrors: string[]
}

export type SearchMetadataResult =
  | { ok: true; result: MetadataSearchResult }
  | { ok: false; errors: { keywords?: string[] } }
  | { ok: false; providerFailure: string }

/** The candidate the user picked for a book (a group), and when. */
export interface BookMatch {
  groupId: number
  match: MetadataCandidate
  pickedAtUtc: string
}

export type PickMatchResult =
  | { ok: true; match: BookMatch }
  | { ok: false; errors: Record<string, string[]> }
  | { ok: false; notFound: true }

/** One stored metadata value and where it came from; provider is set only for origin "provider". */
export interface MetadataField<T> {
  value: T
  origin: 'file' | 'provider' | 'user' | null
  provider: string | null
}

export interface BookMetadataFields {
  title: MetadataField<string | null>
  subtitle: MetadataField<string | null>
  authors: MetadataField<string[]>
  narrators: MetadataField<string[]>
  genres: MetadataField<string[]>
  series: MetadataField<string | null>
  seriesPart: MetadataField<string | null>
  publisher: MetadataField<string | null>
  releaseDate: MetadataField<string | null>
  language: MetadataField<string | null>
  format: MetadataField<string | null>
  asin: MetadataField<string | null>
  isbn: MetadataField<string | null>
}

/** A book's (a group's) metadata values. */
export interface BookMetadata {
  groupId: number
  fields: BookMetadataFields
}

/** The values a user sets; a missing field is left alone, "" or [] clears it. */
export interface UpdateBookMetadataFields {
  title?: string
  subtitle?: string
  authors?: string[]
  narrators?: string[]
  genres?: string[]
  series?: string
  seriesPart?: string
  publisher?: string
  releaseDate?: string
  language?: string
  format?: string
  asin?: string
  isbn?: string
}

export type SaveMetadataResult =
  | { ok: true; metadata: BookMetadata }
  | { ok: false; errors: Record<string, string[]> }
  | { ok: false; notFound: true }

/** The book's chosen cover: source "embedded" (with the file id), "provider" or "upload". */
export interface ChosenCover {
  source: 'embedded' | 'provider' | 'upload'
  fileId: number | null
  mimeType: string
  chosenAtUtc: string
}

/** A file of the book whose scan found an embedded cover. */
export interface EmbeddedCover {
  fileId: number
  relativePath: string
}

/** The current match's cover, which the "provider" choice downloads. */
export interface ProviderCover {
  provider: string
  coverUrl: string
  /** True when the chosen cover was downloaded from this match (not from an earlier one). */
  isChosen: boolean
}

/** A book's chosen cover, if any, and the covers it can choose from. */
export interface BookCovers {
  groupId: number
  chosen: ChosenCover | null
  embedded: EmbeddedCover[]
  provider: ProviderCover | null
}

export type CoverChoice = { source: 'embedded'; fileId: number } | { source: 'provider' }

export type SaveCoverResult =
  | { ok: true; cover: BookCovers }
  | { ok: false; errors: Record<string, string[]> }
  | { ok: false; notFound: true }
  | { ok: false; downloadFailed: string }
