import type { Dashboard, ScanRun, StartScanResult } from '../types/dashboard'
import type {
  CreateGroupRequest,
  CreateGroupResult,
  FileListResult,
  GroupDetail,
  GroupFieldErrors,
  GroupSummary,
  GroupsResponse,
  MoveFilesResult,
  ReorderGroupResult,
  UpdateGroupRequest,
  UpdateGroupResult,
} from '../types/groups'
import type { HealthResponse } from '../types/health'
import type {
  BookCovers,
  BookMatch,
  BookMetadata,
  CoverChoice,
  MetadataCandidate,
  MetadataSearchResult,
  PickMatchResult,
  SaveCoverResult,
  SaveMetadataResult,
  SearchMetadataResult,
  UpdateBookMetadataFields,
} from '../types/metadata'
import type {
  CanonicalEntry,
  EntryFieldErrors,
  EntryRequest,
  MergeSuggestion,
  NameKind,
  SaveEntryResult,
} from '../types/names'
import type { SaveSettingsResult, Settings, SettingsFieldErrors } from '../types/settings'

// The only place the client calls the backend.

export async function getHealth(): Promise<HealthResponse> {
  const response = await fetch('/api/health')
  // 503 carries the same JSON body, reporting a degraded database.
  if (response.status !== 200 && response.status !== 503) {
    throw new Error(`Health check failed with status ${response.status}`)
  }
  return (await response.json()) as HealthResponse
}

export async function getSettings(): Promise<Settings> {
  const response = await fetch('/api/settings')
  if (response.status !== 200) {
    throw new Error(`Loading settings failed with status ${response.status}`)
  }
  return (await response.json()) as Settings
}

export async function saveSettings(settings: Settings): Promise<SaveSettingsResult> {
  const response = await fetch('/api/settings', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings),
  })
  if (response.status === 200) {
    return { ok: true, settings: (await response.json()) as Settings }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: SettingsFieldErrors }
    return { ok: false, errors: problem.errors ?? {} }
  }
  throw new Error(`Saving settings failed with status ${response.status}`)
}

export async function getDashboard(): Promise<Dashboard> {
  const response = await fetch('/api/dashboard')
  if (response.status !== 200) {
    throw new Error(`Loading the dashboard failed with status ${response.status}`)
  }
  return (await response.json()) as Dashboard
}

export async function getLatestScan(): Promise<ScanRun | null> {
  const response = await fetch('/api/scans/latest')
  if (response.status === 204) return null
  if (response.status !== 200) {
    throw new Error(`Loading the latest scan failed with status ${response.status}`)
  }
  return (await response.json()) as ScanRun
}

export async function startScan(): Promise<StartScanResult> {
  const response = await fetch('/api/scans', { method: 'POST' })
  if (response.status === 202) {
    return { started: true, run: (await response.json()) as ScanRun }
  }
  if (response.status === 409) {
    const problem = (await response.json()) as { title?: string; detail?: string }
    return { started: false, message: problem.detail || problem.title || 'A scan is already running.' }
  }
  throw new Error(`Starting a scan failed with status ${response.status}`)
}

export async function getGroups(): Promise<GroupsResponse> {
  const response = await fetch('/api/groups')
  if (response.status !== 200) {
    throw new Error(`Loading groups failed with status ${response.status}`)
  }
  return (await response.json()) as GroupsResponse
}

export async function updateGroup(id: number, request: UpdateGroupRequest): Promise<UpdateGroupResult> {
  const response = await fetch(`/api/groups/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
  if (response.status === 200) {
    return { ok: true, group: (await response.json()) as GroupSummary }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: GroupFieldErrors }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  throw new Error(`Saving the group failed with status ${response.status}`)
}

export async function getGroup(id: number): Promise<GroupDetail> {
  const response = await fetch(`/api/groups/${id}`)
  if (response.status !== 200) {
    throw new Error(`Loading the group failed with status ${response.status}`)
  }
  return (await response.json()) as GroupDetail
}

export async function reorderGroup(id: number, fileIds: number[]): Promise<ReorderGroupResult> {
  const response = await fetch(`/api/groups/${id}/order`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fileIds }),
  })
  if (response.status === 200) {
    return { ok: true, group: (await response.json()) as GroupDetail }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: { fileIds?: string[] } }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  throw new Error(`Reordering the group failed with status ${response.status}`)
}

export async function moveFilesToGroup(id: number, fileIds: number[]): Promise<MoveFilesResult> {
  const response = await fetch(`/api/groups/${id}/files`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fileIds }),
  })
  if (response.status === 200) {
    return { ok: true, group: (await response.json()) as GroupDetail }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: { fileIds?: string[] } }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  throw new Error(`Moving the files failed with status ${response.status}`)
}

export async function createGroup(request: CreateGroupRequest): Promise<CreateGroupResult> {
  const response = await fetch('/api/groups', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
  if (response.status === 201) {
    return { ok: true, group: (await response.json()) as GroupDetail }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: { fileIds?: string[]; title?: string[]; author?: string[] } }
    return { ok: false, errors: problem.errors ?? {} }
  }
  throw new Error(`Creating the group failed with status ${response.status}`)
}

async function postFileList(url: string, fileIds: number[], action: string): Promise<FileListResult> {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fileIds }),
  })
  if (response.status === 200) {
    return { ok: true, list: (await response.json()) as GroupsResponse }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: { fileIds?: string[] } }
    return { ok: false, errors: problem.errors ?? {} }
  }
  throw new Error(`${action} failed with status ${response.status}`)
}

export function excludeFiles(fileIds: number[]): Promise<FileListResult> {
  return postFileList('/api/groups/excluded', fileIds, 'Excluding the files')
}

export function unresolveFiles(fileIds: number[]): Promise<FileListResult> {
  return postFileList('/api/groups/unresolved', fileIds, 'Sending the files to unresolved')
}

export async function searchMetadata(keywords: string): Promise<SearchMetadataResult> {
  const response = await fetch(`/api/metadata/search?keywords=${encodeURIComponent(keywords)}`)
  if (response.status === 200) {
    return { ok: true, result: (await response.json()) as MetadataSearchResult }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: { keywords?: string[] } }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 502) {
    const problem = (await response.json()) as { title?: string; detail?: string }
    return { ok: false, providerFailure: problem.detail || problem.title || 'The metadata providers failed.' }
  }
  throw new Error(`Searching metadata failed with status ${response.status}`)
}

export async function getMatch(groupId: number): Promise<BookMatch | null> {
  const response = await fetch(`/api/groups/${groupId}/match`)
  if (response.status === 204) return null
  if (response.status !== 200) {
    throw new Error(`Loading the match failed with status ${response.status}`)
  }
  return (await response.json()) as BookMatch
}

export async function pickMatch(groupId: number, candidate: MetadataCandidate): Promise<PickMatchResult> {
  const response = await fetch(`/api/groups/${groupId}/match`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(candidate),
  })
  if (response.status === 200) {
    return { ok: true, match: (await response.json()) as BookMatch }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: Record<string, string[]> }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  throw new Error(`Saving the match failed with status ${response.status}`)
}

export async function getMetadata(groupId: number): Promise<BookMetadata> {
  const response = await fetch(`/api/groups/${groupId}/metadata`)
  if (response.status !== 200) {
    throw new Error(`Loading the metadata failed with status ${response.status}`)
  }
  return (await response.json()) as BookMetadata
}

export async function saveMetadata(groupId: number, fields: UpdateBookMetadataFields): Promise<SaveMetadataResult> {
  const response = await fetch(`/api/groups/${groupId}/metadata`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fields }),
  })
  if (response.status === 200) {
    return { ok: true, metadata: (await response.json()) as BookMetadata }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: Record<string, string[]> }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  throw new Error(`Saving the metadata failed with status ${response.status}`)
}

export function fileCoverUrl(groupId: number, fileId: number): string {
  return `/api/groups/${groupId}/files/${fileId}/cover`
}

export function chosenCoverUrl(groupId: number): string {
  return `/api/groups/${groupId}/cover/image`
}

export async function getCovers(groupId: number): Promise<BookCovers> {
  const response = await fetch(`/api/groups/${groupId}/cover`)
  if (response.status !== 200) {
    throw new Error(`Loading the covers failed with status ${response.status}`)
  }
  return (await response.json()) as BookCovers
}

/** Maps a cover PUT's 200, 400 and 404 (and 502 for a download); throws for any other status. */
async function toSaveCoverResult(response: Response, action: string): Promise<SaveCoverResult> {
  if (response.status === 200) {
    return { ok: true, cover: (await response.json()) as BookCovers }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: Record<string, string[]> }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  if (response.status === 502) {
    const problem = (await response.json()) as { title?: string; detail?: string }
    return { ok: false, downloadFailed: problem.detail || problem.title || "The cover couldn't be downloaded." }
  }
  throw new Error(`${action} failed with status ${response.status}`)
}

export async function chooseCover(groupId: number, choice: CoverChoice): Promise<SaveCoverResult> {
  const response = await fetch(`/api/groups/${groupId}/cover`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(choice),
  })
  return toSaveCoverResult(response, 'Choosing the cover')
}

export async function uploadCover(groupId: number, file: File): Promise<SaveCoverResult> {
  const body = new FormData()
  body.append('file', file)
  // No Content-Type header: the browser sets multipart/form-data with its boundary.
  const response = await fetch(`/api/groups/${groupId}/cover/upload`, { method: 'PUT', body })
  if (response.status === 502) throw new Error('Uploading the cover failed with status 502')
  return toSaveCoverResult(response, 'Uploading the cover')
}

export async function getEntries(kind: NameKind): Promise<CanonicalEntry[]> {
  const response = await fetch(`/api/${kind}`)
  if (response.status !== 200) {
    throw new Error(`Loading ${kind} failed with status ${response.status}`)
  }
  return (await response.json()) as CanonicalEntry[]
}

/** Maps a create (201) or update (200) response, its 400 and its 404; throws for any other status. */
async function toSaveEntryResult(response: Response, successStatus: number, action: string): Promise<SaveEntryResult> {
  if (response.status === successStatus) {
    return { ok: true, entry: (await response.json()) as CanonicalEntry }
  }
  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: EntryFieldErrors }
    return { ok: false, errors: problem.errors ?? {} }
  }
  if (response.status === 404) {
    return { ok: false, notFound: true }
  }
  throw new Error(`${action} failed with status ${response.status}`)
}

export async function createEntry(kind: NameKind, request: EntryRequest): Promise<SaveEntryResult> {
  const response = await fetch(`/api/${kind}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
  return toSaveEntryResult(response, 201, 'Creating the entry')
}

export async function updateEntry(kind: NameKind, id: number, request: EntryRequest): Promise<SaveEntryResult> {
  const response = await fetch(`/api/${kind}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
  return toSaveEntryResult(response, 200, 'Saving the entry')
}

export async function getSuggestions(kind: NameKind): Promise<MergeSuggestion[]> {
  const response = await fetch(`/api/${kind}/suggestions`)
  if (response.status !== 200) {
    throw new Error(`Loading ${kind} suggestions failed with status ${response.status}`)
  }
  return (await response.json()) as MergeSuggestion[]
}

/** Folds `fromId` into `keepId`: its name and aliases become aliases of the kept entry. */
export async function mergeEntry(kind: NameKind, keepId: number, fromId: number): Promise<SaveEntryResult> {
  const response = await fetch(`/api/${kind}/${keepId}/merge`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fromId }),
  })
  return toSaveEntryResult(response, 200, 'Merging the entry')
}
