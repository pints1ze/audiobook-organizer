export type NameKind = 'people' | 'genres'

export interface CanonicalEntry {
  id: number
  name: string
  aliases: string[]
}

export interface EntryRequest {
  name: string
  aliases: string[]
}

export interface EntryFieldErrors {
  name?: string[]
  aliases?: string[]
}

export type SaveEntryResult =
  | { ok: true; entry: CanonicalEntry }
  | { ok: false; errors: EntryFieldErrors }
  | { ok: false; notFound: true }

export interface MergeSuggestion {
  keep: CanonicalEntry
  merge: CanonicalEntry
  reason: string
}

export interface NameSuggestion {
  entry: CanonicalEntry
  reason: string
}

/** One stored name on a book: the person it resolves to, or likely people. */
export interface NameLink {
  name: string
  entry: CanonicalEntry | null
  suggestions: NameSuggestion[]
}

export interface BookPeople {
  groupId: number
  authors: NameLink[]
  narrators: NameLink[]
}
