export type GroupConfidence = 'Low' | 'Medium' | 'High'

export interface GroupSummary {
  id: number
  title: string | null
  author: string | null
  confidence: GroupConfidence
  evidence: string[]
  fileCount: number
  totalDurationMs: number
  /** True once the user has corrected the group; rescans leave it unchanged. */
  userCorrected: boolean
}

export interface UnresolvedFile {
  fileId: number
  /** Relative to incoming, with '/' separators. */
  relativePath: string
  durationMs: number | null
}

export interface GroupsResponse {
  groups: GroupSummary[]
  unresolved: UnresolvedFile[]
}

export interface UpdateGroupRequest {
  title: string | null
  author: string | null
}

export interface GroupFieldErrors {
  title?: string[]
  author?: string[]
}

export type UpdateGroupResult =
  | { ok: true; group: GroupSummary }
  | { ok: false; errors: GroupFieldErrors }
  | { ok: false; notFound: true }

export interface GroupFile {
  fileId: number
  /** Relative to incoming, with '/' separators. */
  relativePath: string
  /** 1-based place in the book. */
  position: number
  disc: number | null
  track: number | null
  durationMs: number | null
}

export interface GroupDetail extends GroupSummary {
  /** In position order. */
  files: GroupFile[]
}

export type ReorderGroupResult =
  | { ok: true; group: GroupDetail }
  | { ok: false; errors: { fileIds?: string[] } }
  | { ok: false; notFound: true }

export type MoveFilesResult = ReorderGroupResult

export interface CreateGroupRequest {
  fileIds: number[]
  title: string | null
  author: string | null
}

export type CreateGroupResult =
  | { ok: true; group: GroupDetail }
  | { ok: false; errors: { fileIds?: string[]; title?: string[]; author?: string[] } }
