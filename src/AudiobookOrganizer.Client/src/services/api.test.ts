import { afterEach, describe, expect, it, vi } from 'vitest'
import { getHealth, getSettings, saveSettings } from './api'

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

describe('settings', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const settings = { incomingPath: '/incoming', libraryPath: '/library', keepSourceFiles: false }

  it('getSettings returns the parsed body', async () => {
    const fetchMock = mockFetch(200, settings)

    await expect(getSettings()).resolves.toEqual(settings)
    expect(fetchMock).toHaveBeenCalledWith('/api/settings')
  })

  it('getSettings throws for a non-200 status', async () => {
    mockFetch(500, { title: 'error' })

    await expect(getSettings()).rejects.toThrow()
  })

  it('saveSettings returns ok with the saved settings on a 200', async () => {
    const fetchMock = mockFetch(200, settings)

    await expect(saveSettings(settings)).resolves.toEqual({ ok: true, settings })
    expect(fetchMock).toHaveBeenCalledWith('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    })
  })

  it('saveSettings returns the field errors on a 400 problem body', async () => {
    const errors = { libraryPath: ["The library directory can't be inside the incoming directory."] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(saveSettings(settings)).resolves.toEqual({ ok: false, errors })
  })

  it('saveSettings throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(saveSettings(settings)).rejects.toThrow()
  })
})
