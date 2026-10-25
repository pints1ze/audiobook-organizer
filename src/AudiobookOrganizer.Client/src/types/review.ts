// Mirrors Models/Api/ReviewDtos.cs.

export type ReviewStage = 'NeedsMatch' | 'NeedsReview' | 'Ready' | 'Queued' | 'Failed'

export interface ReviewBook {
  groupId: number
  title: string | null
  author: string | null
  narrator: string | null
  durationMs: number
  fileCount: number
  stage: ReviewStage
  coverSource: 'chosen' | 'embedded' | null
  coverFileId: number | null
  /** When the chosen cover was chosen; null unless coverSource is "chosen". Versions the cover URL. */
  coverChosenAtUtc: string | null
}

export interface ReviewCounts {
  needsMatch: number
  needsReview: number
  ready: number
  queued: number
  failed: number
}

export interface ReviewList {
  books: ReviewBook[]
  counts: ReviewCounts
}
