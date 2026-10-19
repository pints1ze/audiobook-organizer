export type GroupConfidence = 'Low' | 'Medium' | 'High'

export interface GroupSummary {
  id: number
  title: string | null
  author: string | null
  confidence: GroupConfidence
  evidence: string[]
  fileCount: number
  totalDurationMs: number
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
