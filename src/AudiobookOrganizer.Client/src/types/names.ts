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
