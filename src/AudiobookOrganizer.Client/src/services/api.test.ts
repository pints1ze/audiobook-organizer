import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  chooseCover,
  chosenCoverUrl,
  createGroup,
  fileCoverUrl,
  getCovers,
  excludeFiles,
  getDashboard,
  getGroup,
  getGroups,
  getHealth,
  getLatestScan,
  getMatch,
  getMetadata,
  pickMatch,
  saveMetadata,
  getSettings,
  saveSettings,
  searchMetadata,
  startScan,
  moveFilesToGroup,
  unresolveFiles,
  reorderGroup,
  updateGroup,
  uploadCover,
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
    const dashboard = {
      incomingPath: '/in',
      libraryPath: '/lib',
      lastScan: null,
      audioFileCount: 3,
      filesWithErrors: 1,
      bookCount: 2,
      booksByConfidence: { low: 0, medium: 1, high: 1 },
      correctedBookCount: 0,
      unresolvedFileCount: 1,
      excludedFileCount: 0,
    }
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

describe('searchMetadata', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const candidate = {
    provider: 'audible',
    asin: 'B08G9PRS1K',
    title: 'Project Hail Mary',
    subtitle: null,
    authors: ['Andy Weir'],
    narrators: ['Ray Porter'],
    series: null,
    publisher: 'Audible Studios',
    releaseDate: '2021-05-04',
    language: 'english',
    runtimeMinutes: 970,
    format: 'unabridged',
    coverUrl: 'https://example.test/cover.jpg',
    isbn: null,
  }

  it('encodes the keywords and returns candidates and provider errors on a 200', async () => {
    const result = { candidates: [candidate], providerErrors: ['Audible search failed: timeout'] }
    const fetchMock = mockFetch(200, result)

    await expect(searchMetadata('hail mary & co/2')).resolves.toEqual({ ok: true, result })
    expect(fetchMock).toHaveBeenCalledWith('/api/metadata/search?keywords=hail%20mary%20%26%20co%2F2')
  })

  it('returns the field errors on a 400', async () => {
    mockFetch(400, { title: 'One or more validation errors occurred.', errors: { keywords: ['Enter a title.'] } })

    await expect(searchMetadata(' ')).resolves.toEqual({ ok: false, errors: { keywords: ['Enter a title.'] } })
  })

  it('returns the problem detail on a 502', async () => {
    mockFetch(502, { title: 'Bad Gateway', status: 502, detail: 'Audible search failed. Open Library search failed.' })

    await expect(searchMetadata('dune')).resolves.toEqual({
      ok: false,
      providerFailure: 'Audible search failed. Open Library search failed.',
    })
  })

  it('throws for any other status', async () => {
    mockFetch(500, { title: 'error' })

    await expect(searchMetadata('dune')).rejects.toThrow()
  })
})

describe('match', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const candidate = {
    provider: 'audible',
    asin: 'B08G9PRS1K',
    title: 'Project Hail Mary',
    subtitle: null,
    authors: ['Andy Weir'],
    narrators: ['Ray Porter'],
    series: null,
    publisher: 'Audible Studios',
    releaseDate: '2021-05-04',
    language: 'english',
    runtimeMinutes: 970,
    format: 'unabridged',
    coverUrl: 'https://example.test/cover.jpg',
    isbn: null,
  }
  const bookMatch = { groupId: 4, match: candidate, pickedAtUtc: '2026-10-19T10:00:00+00:00' }

  it('getMatch returns the match on a 200', async () => {
    const fetchMock = mockFetch(200, bookMatch)

    await expect(getMatch(4)).resolves.toEqual(bookMatch)
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/4/match')
  })

  it('getMatch returns null on a 204', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(getMatch(4)).resolves.toBeNull()
  })

  it('getMatch throws for any other status', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(getMatch(4)).rejects.toThrow()
  })

  it('pickMatch sends a PUT with the candidate and returns the match on a 200', async () => {
    const fetchMock = mockFetch(200, bookMatch)

    await expect(pickMatch(4, candidate)).resolves.toEqual({ ok: true, match: bookMatch })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/4/match', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(candidate),
    })
  })

  it('pickMatch returns the field errors on a 400', async () => {
    const errors = { title: ['The title is required.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(pickMatch(4, candidate)).resolves.toEqual({ ok: false, errors })
  })

  it('pickMatch returns notFound on a 404', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(pickMatch(4, candidate)).resolves.toEqual({ ok: false, notFound: true })
  })

  it('pickMatch throws for any other status', async () => {
    mockFetch(500, { title: 'error' })

    await expect(pickMatch(4, candidate)).rejects.toThrow()
  })
})

describe('metadata', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const field = <T,>(value: T, origin: 'file' | 'provider' | 'user' | null = null, provider: string | null = null) => ({
    value,
    origin,
    provider,
  })
  const metadata = {
    groupId: 4,
    fields: {
      title: field('Project Hail Mary', 'file'),
      subtitle: field(null),
      authors: field(['Andy Weir'], 'file'),
      narrators: field(['A', 'B'], 'user'),
      series: field(null),
      seriesPart: field(null),
      publisher: field('', 'user'),
      releaseDate: field(null),
      language: field(null),
      format: field(null),
      asin: field(null),
      isbn: field(null),
    },
  }

  it('getMetadata returns the parsed body on a 200', async () => {
    const fetchMock = mockFetch(200, metadata)

    await expect(getMetadata(4)).resolves.toEqual(metadata)
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/4/metadata')
  })

  it('getMetadata throws for any other status', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(getMetadata(4)).rejects.toThrow()
  })

  it('saveMetadata sends a PUT with { fields } and returns the metadata on a 200', async () => {
    const fetchMock = mockFetch(200, metadata)
    const fields = { narrators: ['A', 'B'], publisher: '' }

    await expect(saveMetadata(4, fields)).resolves.toEqual({ ok: true, metadata })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/4/metadata', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fields }),
    })
  })

  it('saveMetadata returns the field errors on a 400', async () => {
    const errors = { title: ['The title is required.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })

    await expect(saveMetadata(4, { title: '' })).resolves.toEqual({ ok: false, errors })
  })

  it('saveMetadata returns notFound on a 404', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(saveMetadata(4, { title: 'x' })).resolves.toEqual({ ok: false, notFound: true })
  })

  it('saveMetadata throws for any other status', async () => {
    mockFetch(500, { title: 'error' })

    await expect(saveMetadata(4, { title: 'x' })).rejects.toThrow()
  })
})

describe('covers', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const cover = {
    groupId: 4,
    chosen: { source: 'upload', fileId: null, mimeType: 'image/png', chosenAtUtc: '2026-10-19T10:00:00+00:00' },
    embedded: [{ fileId: 7, relativePath: 'book/track1.mp3' }],
    provider: { provider: 'audible', coverUrl: 'https://m.media-amazon.com/images/I/x.jpg' },
  }

  it('builds the image URLs', () => {
    expect(fileCoverUrl(4, 7)).toBe('/api/groups/4/files/7/cover')
    expect(chosenCoverUrl(4)).toBe('/api/groups/4/cover/image')
  })

  it('getCovers returns the parsed body on a 200', async () => {
    const fetchMock = mockFetch(200, cover)

    await expect(getCovers(4)).resolves.toEqual(cover)
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/4/cover')
  })

  it('getCovers throws for any other status', async () => {
    mockFetch(404, { title: 'Not Found', status: 404 })

    await expect(getCovers(4)).rejects.toThrow()
  })

  it('chooseCover sends the choice as JSON and returns the covers on a 200', async () => {
    const fetchMock = mockFetch(200, cover)

    await expect(chooseCover(4, { source: 'provider' })).resolves.toEqual({ ok: true, cover })
    expect(fetchMock).toHaveBeenCalledWith('/api/groups/4/cover', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source: 'provider' }),
    })
  })

  it('chooseCover maps 400, 404 and 502, and throws for any other status', async () => {
    const errors = { fileId: ['This file has no embedded cover.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })
    await expect(chooseCover(4, { source: 'embedded', fileId: 7 })).resolves.toEqual({ ok: false, errors })

    mockFetch(404, { title: 'Not Found', status: 404 })
    await expect(chooseCover(4, { source: 'provider' })).resolves.toEqual({ ok: false, notFound: true })

    const detail = "The cover couldn't be downloaded. Try again later."
    mockFetch(502, { title: 'Bad Gateway', status: 502, detail })
    await expect(chooseCover(4, { source: 'provider' })).resolves.toEqual({ ok: false, downloadFailed: detail })

    mockFetch(500, { title: 'error' })
    await expect(chooseCover(4, { source: 'provider' })).rejects.toThrow()
  })

  it('uploadCover sends a PUT with the file under "file" and returns the covers on a 200', async () => {
    const fetchMock = mockFetch(200, cover)
    const file = new File([new Uint8Array([0x89, 0x50])], 'cover.png', { type: 'image/png' })

    await expect(uploadCover(4, file)).resolves.toEqual({ ok: true, cover })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/groups/4/cover/upload')
    expect(init.method).toBe('PUT')
    expect(init.headers).toBeUndefined()
    const body = init.body as FormData
    expect(body).toBeInstanceOf(FormData)
    expect(body.get('file')).toBeInstanceOf(File)
    expect((body.get('file') as File).name).toBe('cover.png')
  })

  it('uploadCover maps 400 and 404, and throws for any other status', async () => {
    const file = new File(['x'], 'cover.png')
    const errors = { file: ['The cover must be a JPEG or PNG.'] }
    mockFetch(400, { title: 'One or more validation errors occurred.', status: 400, errors })
    await expect(uploadCover(4, file)).resolves.toEqual({ ok: false, errors })

    mockFetch(404, { title: 'Not Found', status: 404 })
    await expect(uploadCover(4, file)).resolves.toEqual({ ok: false, notFound: true })

    mockFetch(413, { title: 'Payload Too Large' })
    await expect(uploadCover(4, file)).rejects.toThrow()
  })
})
