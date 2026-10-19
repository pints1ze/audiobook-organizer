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
