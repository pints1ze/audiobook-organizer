import { afterEach, describe, expect, it, vi } from 'vitest'
import { getHealth } from './api'

function mockFetch(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('getHealth', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('returns the parsed body for a 200 response', async () => {
    const fetchMock = mockFetch(200, { status: 'ok', database: 'ok' })

    await expect(getHealth()).resolves.toEqual({ status: 'ok', database: 'ok' })
    expect(fetchMock).toHaveBeenCalledWith('/api/health')
  })

  it('returns the body for a 503 response', async () => {
    mockFetch(503, { status: 'degraded', database: 'error' })

    await expect(getHealth()).resolves.toEqual({ status: 'degraded', database: 'error' })
  })

  it('throws when fetch rejects', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    await expect(getHealth()).rejects.toThrow()
  })

  it('throws for any other status', async () => {
    mockFetch(500, { title: 'error' })

    await expect(getHealth()).rejects.toThrow()
  })
})
