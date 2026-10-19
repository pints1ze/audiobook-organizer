import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createGroup,
  excludeFiles,
  getDashboard,
  getGroup,
  getGroups,
  getHealth,
  getLatestScan,
  getSettings,
  saveSettings,
  startScan,
  moveFilesToGroup,
  unresolveFiles,
  reorderGroup,
  updateGroup,
} from './api'

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

describe('scans', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const run = {
    id: 1,
    startedAt: '2026-10-18T10:00:00Z',
    finishedAt: null,
    status: 'Running',
    filesFound: 0,
    filesAdded: 0,
    filesUpdated: 0,
    filesUnchanged: 0,
    filesMissing: 0,
    filesFailed: 0,
    error: null,
  }

  it('getDashboard returns the parsed body', async () => {
    const dashboard = { incomingPath: '/in', libraryPath: '/lib', lastScan: null, audioFileCount: 3, filesWithErrors: 1 }
    const fetchMock = mockFetch(200, dashboard)

    await expect(getDashboard()).resolves.toEqual(dashboard)
    expect(fetchMock).toHaveBeenCalledWith('/api/dashboard')
  })

  it('getLatestScan returns the run on a 200', async () => {
    const fetchMock = mockFetch(200, run)

    await expect(getLatestScan()).resolves.toEqual(run)
    expect(fetchMock).toHaveBeenCalledWith('/api/scans/latest')
  })

  it('getLatestScan returns null on a 204', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(getLatestScan()).resolves.toBeNull()
  })

  it('startScan returns started with the run on a 202', async () => {
    const fetchMock = mockFetch(202, run)

    await expect(startScan()).resolves.toEqual({ started: true, run })
    expect(fetchMock).toHaveBeenCalledWith('/api/scans', { method: 'POST' })
  })

  it('startScan returns the problem detail on a 409', async () => {
    mockFetch(409, {
      title: 'A scan is already running.',
      detail: 'Wait for the current scan to finish, then start another.',
      status: 409,
    })

    await expect(startScan()).resolves.toEqual({
      started: false,
      message: 'Wait for the current scan to finish, then start another.',
    })
  })

  it('startScan falls back to the problem title on a 409 without detail', async () => {
    mockFetch(409, { title: 'A scan is already running.', status: 409 })

    await expect(startScan()).resolves.toEqual({ started: false, message: 'A scan is already running.' })
  })

  it('startScan throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(startScan()).rejects.toThrow()
  })
})

describe('groups', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('getGroups returns the parsed body on a 200', async () => {
    const body = {
      groups: [
        { id: 1, title: 'T', author: null, confidence: 'High', evidence: ['e'], fileCount: 2, totalDurationMs: 1000, userCorrected: false },
      ],
      unresolved: [{ fileId: 3, relativePath: 'a.mp3', durationMs: null }],
      excluded: [{ fileId: 4, relativePath: 'b.mp3', durationMs: 5 }],
    }
    const fetchMock = mockFetch(200, body)

    await expect(getGroups()).resolves.toEqual(body)
    expect(fetchMock).toHaveBeenCalledWith('/api/groups')
  })

  it('getGroups throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(getGroups()).rejects.toThrow()
  })

  const group = {
    id: 5,
    title: 'The Hobbit',
    author: 'J.R.R. Tolkien',
    confidence: 'High',
    evidence: ['e'],
    fileCount: 2,
    totalDurationMs: 1000,
    userCorrected: true,
  }
  const request = { title: 'The Hobbit', author: 'J.R.R. Tolkien' }

  it('updateGroup sends a PUT with the JSON body and returns the group on a 200', async () => {
    const fetchMock = mockFetch(200, group)

    await expect(updateGroup(5, request)).resolves.toEqual({ ok: true, group })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/5', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  })

  it('updateGroup returns the field errors on a 400 problem body', async () => {
    const errors = { title: ['The title can be at most 500 characters.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(updateGroup(5, request)).resolves.toEqual({ ok: false, errors })
  })

  it('updateGroup returns notFound on a 404', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(updateGroup(5, request)).resolves.toEqual({ ok: false, notFound: true })
  })

  it('updateGroup throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(updateGroup(5, request)).rejects.toThrow()
  })
  const detail = {
    ...group,
    files: [
      { fileId: 11, relativePath: 'book/01.mp3', position: 1, disc: null, track: 1, durationMs: 500 },
      { fileId: 12, relativePath: 'book/02.mp3', position: 2, disc: null, track: 2, durationMs: 500 },
    ],
  }

  it('getGroup returns the detail on a 200', async () => {
    const fetchMock = mockFetch(200, detail)

    await expect(getGroup(5)).resolves.toEqual(detail)
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/5')
  })

  it('getGroup throws on a 404', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(getGroup(5)).rejects.toThrow()
  })

  it('reorderGroup sends a PUT of the file ids and returns the group on a 200', async () => {
    const fetchMock = mockFetch(200, detail)

    await expect(reorderGroup(5, [12, 11])).resolves.toEqual({ ok: true, group: detail })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/5/order', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fileIds: [12, 11] }),
    })
  })

  it('reorderGroup returns the errors on a 400 problem body', async () => {
    const errors = { fileIds: ['The list must contain every file in the group exactly once.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(reorderGroup(5, [11])).resolves.toEqual({ ok: false, errors })
  })

  it('reorderGroup returns notFound on a 404', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(reorderGroup(5, [11])).resolves.toEqual({ ok: false, notFound: true })
  })

  it('reorderGroup throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(reorderGroup(5, [11])).rejects.toThrow()
  })
  it('moveFilesToGroup POSTs the file ids and returns the group on a 200', async () => {
    const fetchMock = mockFetch(200, detail)

    await expect(moveFilesToGroup(5, [12, 11])).resolves.toEqual({ ok: true, group: detail })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/5/files', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fileIds: [12, 11] }),
    })
  })

  it('moveFilesToGroup returns the errors on a 400 problem body', async () => {
    const errors = { fileIds: ['Every file must exist.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(moveFilesToGroup(5, [99])).resolves.toEqual({ ok: false, errors })
  })

  it('moveFilesToGroup returns notFound on a 404', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(moveFilesToGroup(5, [11])).resolves.toEqual({ ok: false, notFound: true })
  })

  it('moveFilesToGroup throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(moveFilesToGroup(5, [11])).rejects.toThrow()
  })

  it('createGroup POSTs the request and returns the group on a 201', async () => {
    const fetchMock = mockFetch(201, detail)
    const request = { fileIds: [11, 12], title: null, author: null }

    await expect(createGroup(request)).resolves.toEqual({ ok: true, group: detail })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  })

  it('createGroup returns the errors on a 400 problem body', async () => {
    const errors = { fileIds: ['Choose at least one file.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(createGroup({ fileIds: [], title: null, author: null })).resolves.toEqual({ ok: false, errors })
  })

  it('createGroup throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(createGroup({ fileIds: [11], title: null, author: null })).rejects.toThrow()
  })
})

describe.each([
  ['excludeFiles', excludeFiles, '/api/groups/excluded'],
  ['unresolveFiles', unresolveFiles, '/api/groups/unresolved'],
])('%s', (_name, call, url) => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const list = {
    groups: [],
    unresolved: [{ fileId: 3, relativePath: 'a.mp3', durationMs: null }],
    excluded: [{ fileId: 4, relativePath: 'b.mp3', durationMs: 1000 }],
  }

  it(`POSTs the file ids to ${url} and returns the list on a 200`, async () => {
    const fetchMock = mockFetch(200, list)

    await expect(call([4, 3])).resolves.toEqual({ ok: true, list })
    expect(fetchMock).toHaveBeenCalledWith(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fileIds: [4, 3] }),
    })
  })

  it('returns the errors on a 400 problem body', async () => {
    const errors = { fileIds: ['Every file must exist.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(call([99])).resolves.toEqual({ ok: false, errors })
  })

  it('throws on a 500', async () => {
    mockFetch(500, { title: 'error' })

    await expect(call([4])).rejects.toThrow()
  })
})
