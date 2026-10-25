import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { chooseCover, getCovers, uploadCover } from '../../services/api'
import type { BookCovers, SaveCoverResult } from '../../types/metadata'
import CoverPicker from './CoverPicker'

vi.mock('../../services/api', () => ({
  getCovers: vi.fn(),
  chooseCover: vi.fn(),
  uploadCover: vi.fn(),
  fileCoverUrl: (groupId: number, fileId: number) => `/api/groups/${groupId}/files/${fileId}/cover`,
  chosenCoverUrl: (groupId: number) => `/api/groups/${groupId}/cover/image`,
}))

const getCoversMock = vi.mocked(getCovers)
const chooseCoverMock = vi.mocked(chooseCover)
const uploadCoverMock = vi.mocked(uploadCover)

const covers: BookCovers = {
  groupId: 4,
  chosen: { source: 'embedded', fileId: 11, mimeType: 'image/jpeg', chosenAtUtc: '2026-10-19T10:00:00Z' },
  embedded: [
    { fileId: 11, relativePath: 'Disc 1/01.mp3' },
    { fileId: 12, relativePath: 'Disc 2/01.mp3' },
  ],
  provider: { provider: 'audible', coverUrl: 'https://m.media-amazon.com/images/I/phm.jpg', isChosen: false },
}

const empty: BookCovers = { groupId: 4, chosen: null, embedded: [], provider: null }

function renderPicker(reloadKey = 0, onSaved?: () => void) {
  return render(
    <MemoryRouter>
      <CoverPicker groupId={4} reloadKey={reloadKey} onSaved={onSaved} />
    </MemoryRouter>,
  )
}

async function loaded(value: BookCovers = covers, onSaved?: () => void) {
  getCoversMock.mockResolvedValue(value)
  const view = renderPicker(0, onSaved)
  await screen.findByRole('heading', { level: 2, name: 'Cover' })
  await waitFor(() => expect(screen.queryByText('Loading the covers…')).not.toBeInTheDocument())
  return view
}

function optionFor(text: string) {
  return screen.getByText(text).closest('li')!
}

describe('CoverPicker', () => {
  beforeEach(() => {
    getCoversMock.mockReset()
    chooseCoverMock.mockReset()
    uploadCoverMock.mockReset()
  })

  it('shows the chosen embedded cover and lists the options, marking the chosen one current', async () => {
    await loaded()

    expect(getCoversMock).toHaveBeenCalledWith(4)
    const image = screen.getByRole('img', { name: 'Chosen cover' })
    expect(image).toHaveAttribute('src', `/api/groups/4/cover/image?v=${encodeURIComponent('2026-10-19T10:00:00Z')}`)
    expect(screen.getByText('From Disc 1/01.mp3')).toBeInTheDocument()

    const first = optionFor('Disc 1/01.mp3')
    expect(within(first).getByText('Current cover')).toBeInTheDocument()
    expect(within(first).queryByRole('button')).not.toBeInTheDocument()
    expect(within(first).getByRole('presentation')).toHaveAttribute('src', '/api/groups/4/files/11/cover')

    const second = optionFor('Disc 2/01.mp3')
    expect(within(second).getByRole('button', { name: 'Use this cover: Disc 2/01.mp3' })).toBeEnabled()

    const match = screen.getByRole('button', { name: "Use the match's cover: Audible" }).closest('li')!
    expect(within(match).getByRole('presentation')).toHaveAttribute('src', covers.provider!.coverUrl)
  })

  it('with no chosen cover and no options shows both empty messages', async () => {
    await loaded(empty)

    expect(screen.getByText('No cover chosen yet.')).toBeInTheDocument()
    expect(
      screen.getByText('This book has no embedded cover and no match with a cover. Upload one below.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('says the covers could not be loaded when the load fails', async () => {
    getCoversMock.mockRejectedValue(new Error('Loading the covers failed with status 500'))
    renderPicker()

    expect(await screen.findByText("The covers couldn't be loaded.")).toBeInTheDocument()
  })

  it("chooses the match's cover, announces it and shows the new source", async () => {
    let resolve!: (value: SaveCoverResult) => void
    chooseCoverMock.mockReturnValue(new Promise((r) => (resolve = r)))
    await loaded()

    const button = screen.getByRole('button', { name: "Use the match's cover: Audible" })
    fireEvent.click(button)
    expect(chooseCoverMock).toHaveBeenCalledWith(4, { source: 'provider' })
    expect(button).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Use this cover: Disc 2/01.mp3' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Upload' })).toBeDisabled()

    resolve({
      ok: true,
      cover: {
        ...covers,
        chosen: { source: 'provider', fileId: null, mimeType: 'image/jpeg', chosenAtUtc: '2026-10-19T11:00:00Z' },
        provider: { ...covers.provider!, isChosen: true },
      },
    })
    expect(await screen.findByText('Cover saved.')).toHaveAttribute('role', 'status')
    expect(screen.getByText('From the match (Audible)')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Chosen cover' }).getAttribute('src')).toContain(
      encodeURIComponent('2026-10-19T11:00:00Z'),
    )
    expect(screen.getByRole('heading', { level: 2, name: 'Cover' })).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Use this cover: Disc 1/01.mp3' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: /Use the match's cover/ })).not.toBeInTheDocument()
  })

  it("marks the match's cover current only when the chosen cover came from the current match", async () => {
    const fromProvider = {
      ...covers,
      chosen: { source: 'provider', fileId: null, mimeType: 'image/jpeg', chosenAtUtc: '2026-10-19T11:00:00Z' },
    } satisfies BookCovers
    const view = await loaded(fromProvider)

    const match = screen.getByText("The match's cover (Audible)").closest('li')!
    expect(within(match).getByRole('button', { name: "Use the match's cover: Audible" })).toBeEnabled()
    expect(within(match).queryByText('Current cover')).not.toBeInTheDocument()
    expect(screen.getByText('From the match (Audible)')).toBeInTheDocument()
    view.unmount()

    await loaded({ ...fromProvider, provider: { ...covers.provider!, isChosen: true } })
    const current = screen.getByText("The match's cover (Audible)").closest('li')!
    expect(within(current).getByText('Current cover')).toBeInTheDocument()
    expect(within(current).queryByRole('button')).not.toBeInTheDocument()
  })

  it('chooses an embedded cover by its file id', async () => {
    chooseCoverMock.mockResolvedValue({
      ok: true,
      cover: { ...covers, chosen: { ...covers.chosen!, fileId: 12, chosenAtUtc: '2026-10-19T11:00:00Z' } },
    })
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Use this cover: Disc 2/01.mp3' }))

    expect(await screen.findByText('From Disc 2/01.mp3')).toBeInTheDocument()
    expect(chooseCoverMock).toHaveBeenCalledWith(4, { source: 'embedded', fileId: 12 })
  })

  it('calls onSaved once after a cover saves, and not after a failed save', async () => {
    const onSaved = vi.fn()
    chooseCoverMock.mockResolvedValueOnce({ ok: false, downloadFailed: 'The cover download failed: 404 Not Found.' })
    await loaded(covers, onSaved)

    fireEvent.click(screen.getByRole('button', { name: "Use the match's cover: Audible" }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The cover download failed')
    expect(onSaved).not.toHaveBeenCalled()

    chooseCoverMock.mockResolvedValueOnce({
      ok: true,
      cover: { ...covers, chosen: { ...covers.chosen!, fileId: 12, chosenAtUtc: '2026-10-19T11:00:00Z' } },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Use this cover: Disc 2/01.mp3' }))
    expect(await screen.findByText('Cover saved.')).toBeInTheDocument()
    expect(onSaved).toHaveBeenCalledTimes(1)
  })

  it('shows a 502 downloadFailed message as an alert', async () => {
    chooseCoverMock.mockResolvedValue({ ok: false, downloadFailed: 'The cover download failed: 404 Not Found.' })
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: "Use the match's cover: Audible" }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The cover download failed: 404 Not Found.')
    expect(screen.getByText('From Disc 1/01.mp3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: "Use the match's cover: Audible" })).toBeEnabled()
  })

  it('shows the first 400 message, a 404 with a link to Incoming, and a thrown error as alerts', async () => {
    chooseCoverMock.mockResolvedValueOnce({ ok: false, errors: { fileId: ['That file has no embedded cover.', 'Other'] } })
    await loaded()
    const button = screen.getByRole('button', { name: 'Use this cover: Disc 2/01.mp3' })

    fireEvent.click(button)
    expect(await screen.findByRole('alert')).toHaveTextContent('That file has no embedded cover.')
    expect(screen.getByRole('alert')).not.toHaveTextContent('Other')

    chooseCoverMock.mockResolvedValueOnce({ ok: false, notFound: true })
    fireEvent.click(button)
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('This book no longer exists.'))
    expect(within(screen.getByRole('alert')).getByRole('link', { name: /incoming/i })).toHaveAttribute('href', '/incoming')

    chooseCoverMock.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    fireEvent.click(button)
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        "The cover couldn't be saved. Check the connection and try again.",
      ),
    )
    expect(button).toBeEnabled()
  })

  it('uploads the selected file and updates the section', async () => {
    uploadCoverMock.mockResolvedValue({
      ok: true,
      cover: {
        ...covers,
        chosen: { source: 'upload', fileId: null, mimeType: 'image/png', chosenAtUtc: '2026-10-19T12:00:00Z' },
      },
    })
    await loaded()
    const input = screen.getByLabelText('Upload a cover')
    expect(input).toHaveAttribute('accept', 'image/jpeg,image/png')
    const file = new File(['png'], 'cover.png', { type: 'image/png' })

    fireEvent.change(input, { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(uploadCoverMock).toHaveBeenCalledWith(4, file)
    expect(await screen.findByText('Uploaded')).toBeInTheDocument()
    expect(screen.getByText('Cover saved.')).toBeInTheDocument()
    expect(within(optionFor('Disc 1/01.mp3')).getByRole('button', { name: 'Use this cover: Disc 1/01.mp3' })).toBeEnabled()
  })

  it('shows a 400 file error next to the input', async () => {
    uploadCoverMock.mockResolvedValue({ ok: false, errors: { file: ['The file must be a JPEG or PNG image.'] } })
    await loaded()
    const input = screen.getByLabelText('Upload a cover')

    fireEvent.change(input, { target: { files: [new File(['text'], 'cover.png', { type: 'image/png' })] } })
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The file must be a JPEG or PNG image.')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input.getAttribute('aria-describedby')).toContain(alert.id)
    expect(screen.getByText('From Disc 1/01.mp3')).toBeInTheDocument()
  })

  it('asks for a file when Upload is pressed without one', async () => {
    await loaded()

    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Choose a JPEG or PNG image to upload.')
    expect(screen.getByLabelText('Upload a cover')).toHaveAttribute('aria-invalid', 'true')
    expect(uploadCoverMock).not.toHaveBeenCalled()
  })

  it('reloads when reloadKey changes', async () => {
    const view = await loaded(empty)
    getCoversMock.mockResolvedValue(covers)

    view.rerender(
      <MemoryRouter>
        <CoverPicker groupId={4} reloadKey={1} />
      </MemoryRouter>,
    )

    expect(await screen.findByRole('button', { name: "Use the match's cover: Audible" })).toBeInTheDocument()
    expect(getCoversMock).toHaveBeenCalledTimes(2)
  })
})
