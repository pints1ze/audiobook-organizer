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
