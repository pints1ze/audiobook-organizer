import type { HealthResponse } from '../types/health'

// The only place the client calls the backend.

export async function getHealth(): Promise<HealthResponse> {
  const response = await fetch('/api/health')
  // 503 carries the same JSON body, reporting a degraded database.
  if (response.status !== 200 && response.status !== 503) {
    throw new Error(`Health check failed with status ${response.status}`)
  }
  return (await response.json()) as HealthResponse
}
