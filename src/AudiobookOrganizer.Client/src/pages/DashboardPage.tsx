import { useEffect, useState } from 'react'
import { CircleAlert } from 'lucide-react'
import { getHealth } from '../services/api'
import type { HealthResponse } from '../types/health'

type HealthState =
  | { kind: 'loading' }
  | { kind: 'loaded'; health: HealthResponse }
  | { kind: 'error' }

export default function DashboardPage() {
  const [state, setState] = useState<HealthState>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    getHealth()
      .then((health) => {
        if (!cancelled) setState({ kind: 'loaded', health })
      })
      .catch(() => {
        if (!cancelled) setState({ kind: 'error' })
      })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-semibold">Audiobook Organizer</h1>
      <div role="status" className="mt-4">
        {state.kind === 'loading' && <p className="text-muted">Checking API…</p>}
        {state.kind === 'loaded' && (
          <>
            <p>API: {state.health.status}</p>
            <p>Database: {state.health.database}</p>
          </>
        )}
        {state.kind === 'error' && (
          <p className="flex items-center gap-2 text-destructive">
            <CircleAlert aria-hidden="true" className="size-5" />
            API unreachable
          </p>
        )}
      </div>
    </main>
  )
}
