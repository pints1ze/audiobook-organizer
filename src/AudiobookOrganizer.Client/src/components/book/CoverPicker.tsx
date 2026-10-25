import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert } from 'lucide-react'
import { chooseCover, chosenCoverUrl, fileCoverUrl, getCovers, uploadCover } from '../../services/api'
import type { BookCovers, CoverChoice, SaveCoverResult } from '../../types/metadata'
import { providerNames } from './MetadataEditor'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; covers: BookCovers } | { kind: 'failed' }

/** Why a save failed, beyond the upload's file error: the book is gone, or a message to show. */
type SaveError = { kind: 'notFound' } | { kind: 'message'; text: string }

const unsavedMessage = "The cover couldn't be saved. Check the connection and try again."

function providerName(provider: string) {
  return providerNames[provider] ?? provider
}

/** Where the chosen cover came from, in words. */
function sourceText(covers: BookCovers) {
  const chosen = covers.chosen
  if (!chosen) return ''
  if (chosen.source === 'upload') return 'Uploaded'
  if (chosen.source === 'provider')
    return covers.provider ? `From the match (${providerName(covers.provider.provider)})` : 'From the match'
  const file = covers.embedded.find((cover) => cover.fileId === chosen.fileId)
  return file ? `From ${file.relativePath}` : 'From a file of this book'
}

type Props = { groupId: number; reloadKey: number; onSaved?: () => void }

export default function CoverPicker({ groupId, reloadKey, onSaved }: Props) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<SaveError | null>(null)
  const [fileError, setFileError] = useState('')
  const [savedMessage, setSavedMessage] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    let current = true
    getCovers(groupId)
      .then((covers) => {
        if (current) setState({ kind: 'loaded', covers })
      })
      .catch(() => {
        if (current) setState({ kind: 'failed' })
      })
    return () => {
      current = false
    }
  }, [groupId, reloadKey])

  /** Runs a choose or upload request and applies its result; true when it saved. */
  async function save(request: () => Promise<SaveCoverResult>) {
    setSaving(true)
    setSaveError(null)
    setFileError('')
    setSavedMessage('')
    let error: SaveError | null = null
    let fileMessage = ''
    let saved: BookCovers | null = null
    try {
      const response = await request()
      if (response.ok) saved = response.cover
      else if ('notFound' in response) error = { kind: 'notFound' }
      else if ('downloadFailed' in response) error = { kind: 'message', text: response.downloadFailed }
      else {
        fileMessage = response.errors.file?.[0] ?? ''
        if (!fileMessage)
          error = { kind: 'message', text: Object.values(response.errors).flat()[0] ?? 'Check the cover and try again.' }
      }
    } catch {
      error = { kind: 'message', text: unsavedMessage }
    }
    if (!mounted.current) return false
    setSaving(false)
    setSaveError(error)
    setFileError(fileMessage)
    if (!saved) return false
    setState({ kind: 'loaded', covers: saved })
    setSavedMessage('Cover saved.')
    onSaved?.()
    return true
  }

  async function handleChoose(choice: CoverChoice) {
    // The pressed button becomes "Current cover", so focus moves to the heading rather than being lost.
    if (await save(() => chooseCover(groupId, choice))) heading.current?.focus()
  }

  async function handleUpload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!file) {
      setSaveError(null)
      setSavedMessage('')
      setFileError('Choose a JPEG or PNG image to upload.')
      fileInput.current?.focus()
      return
    }
    const form = event.currentTarget
    if (await save(() => uploadCover(groupId, file))) {
      form.reset()
      setFile(null)
    } else if (mounted.current) {
      fileInput.current?.focus()
    }
  }

  const buttonClass = 'mt-2 rounded border border-foreground px-3 py-1 text-sm font-medium disabled:opacity-60'

  return (
    <section aria-labelledby="cover-heading" className="mt-6">
      <h2 id="cover-heading" ref={heading} tabIndex={-1} className="rounded text-xl font-semibold">
        Cover
      </h2>
      <p role="status" className="mt-2">
        {savedMessage}
      </p>
      {state.kind === 'loading' && <p className="mt-2 text-muted">Loading the covers…</p>}
      {state.kind === 'failed' && (
        <p className="mt-2 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          The covers couldn't be loaded.
        </p>
      )}
      {state.kind === 'loaded' && (
        <CoverOptions
          groupId={groupId}
          covers={state.covers}
          saving={saving}
          buttonClass={buttonClass}
          onChoose={handleChoose}
        />
      )}
      {saveError && (
        <p role="alert" className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5 shrink-0" />
          {saveError.kind === 'notFound' ? (
            <span>
              This book no longer exists.{' '}
              <Link to="/incoming" className="rounded underline underline-offset-4">
                Back to Incoming
              </Link>
            </span>
          ) : (
            saveError.text
          )}
        </p>
      )}
      <form onSubmit={handleUpload} noValidate className="mt-4 flex flex-wrap items-start gap-4">
        <div className="min-w-0 grow basis-64">
          <label htmlFor="cover-file" className="font-medium">
            Upload a cover
          </label>
          <input
            id="cover-file"
            ref={fileInput}
            type="file"
            accept="image/jpeg,image/png"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            aria-invalid={fileError ? true : undefined}
            aria-describedby={fileError ? 'cover-file-error' : undefined}
            className="mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-2 aria-[invalid=true]:border-destructive"
          />
          {fileError && (
            <p id="cover-file-error" role="alert" className="mt-1 flex items-center gap-1 text-sm text-destructive">
              <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
              {fileError}
            </p>
          )}
        </div>
        <button
          type="submit"
          disabled={saving}
          className="mt-7 rounded border border-foreground px-4 py-2 font-medium disabled:opacity-60"
        >
          Upload
        </button>
      </form>
    </section>
  )
}

type CoverOptionsProps = {
  groupId: number
  covers: BookCovers
  saving: boolean
  buttonClass: string
  onChoose: (choice: CoverChoice) => void
}

/** The chosen cover and the list of covers to choose from. */
function CoverOptions({ groupId, covers, saving, buttonClass, onChoose }: CoverOptionsProps) {
  const { chosen, embedded, provider } = covers
  return (
    <>
      {chosen ? (
        <div className="mt-2 flex items-center gap-4">
          <img
            src={`${chosenCoverUrl(groupId)}?v=${encodeURIComponent(chosen.chosenAtUtc)}`}
            alt="Chosen cover"
            width={160}
            height={160}
            className="size-40 shrink-0 rounded object-cover"
          />
          <p>{sourceText(covers)}</p>
        </div>
      ) : (
        <p className="mt-2">No cover chosen yet.</p>
      )}
      {embedded.length === 0 && !provider ? (
        <p className="mt-4">This book has no embedded cover and no match with a cover. Upload one below.</p>
      ) : (
        <ul className="mt-4 space-y-4">
          {embedded.map((cover) => (
            <li key={cover.fileId} className="flex gap-4 border-b border-muted/30 pb-4">
              <Thumbnail src={fileCoverUrl(groupId, cover.fileId)} />
              <div className="min-w-0">
                <p className="break-words">{cover.relativePath}</p>
                {chosen?.source === 'embedded' && chosen.fileId === cover.fileId ? (
                  <p className="mt-2 text-sm font-medium">Current cover</p>
                ) : (
                  <button
                    type="button"
                    disabled={saving}
                    onClick={() => onChoose({ source: 'embedded', fileId: cover.fileId })}
                    aria-label={`Use this cover: ${cover.relativePath}`}
                    className={buttonClass}
                  >
                    Use this cover
                  </button>
                )}
              </div>
            </li>
          ))}
          {provider && (
            <li className="flex gap-4 border-b border-muted/30 pb-4">
              <Thumbnail src={provider.coverUrl} />
              <div className="min-w-0">
                <p>The match's cover ({providerName(provider.provider)})</p>
                {provider.isChosen ? (
                  <p className="mt-2 text-sm font-medium">Current cover</p>
                ) : (
                  <button
                    type="button"
                    disabled={saving}
                    onClick={() => onChoose({ source: 'provider' })}
                    aria-label={`Use the match's cover: ${providerName(provider.provider)}`}
                    className={buttonClass}
                  >
                    Use the match's cover
                  </button>
                )}
              </div>
            </li>
          )}
        </ul>
      )}
    </>
  )
}

/** Decorative: the option's path or provider is in the text beside it. */
function Thumbnail({ src }: { src: string }) {
  return <img src={src} alt="" width={80} height={80} loading="lazy" className="size-20 shrink-0 rounded object-cover" />
}
