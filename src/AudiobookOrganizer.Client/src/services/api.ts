import type { Dashboard, ScanRun, StartScanResult } from '../types/dashboard'
import type {
  CreateGroupRequest,
  CreateGroupResult,
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
