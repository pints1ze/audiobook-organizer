import { useEffect, useRef, useState, type FormEvent, type RefObject } from 'react'
import { CircleAlert } from 'lucide-react'
import { getSettings, saveSettings } from '../services/api'
import type { Settings, SettingsFieldErrors } from '../types/settings'

type LoadState = 'loading' | 'loaded' | 'error'

const tokenHint =
  'Tokens: %artist% %album% %series% %series-part% %track% %disc% %title% %year% %narrator%. Pad with %track:3%; wrap optional parts in [ ].'

const inputClass =
  'mt-1 block w-full rounded border border-muted px-3 py-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:border-2'

export default function SettingsPage() {
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [incomingPath, setIncomingPath] = useState('')
  const [libraryPath, setLibraryPath] = useState('')
  const [directoryTemplate, setDirectoryTemplate] = useState('')
  const [fileTemplate, setFileTemplate] = useState('')
  const [keepSourceFiles, setKeepSourceFiles] = useState(false)
  const [errors, setErrors] = useState<SettingsFieldErrors>({})
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<'saved' | 'failed' | null>(null)
  const incomingRef = useRef<HTMLInputElement>(null)
  const libraryRef = useRef<HTMLInputElement>(null)
  const directoryTemplateRef = useRef<HTMLInputElement>(null)
  const fileTemplateRef = useRef<HTMLInputElement>(null)

  function show(settings: Settings) {
    setIncomingPath(settings.incomingPath ?? '')
    setLibraryPath(settings.libraryPath ?? '')
    setDirectoryTemplate(settings.directoryTemplate ?? '')
    setFileTemplate(settings.fileTemplate ?? '')
    setKeepSourceFiles(settings.keepSourceFiles)
  }

  useEffect(() => {
    let cancelled = false
    getSettings()
      .then((settings) => {
        if (cancelled) return
        show(settings)
        setLoadState('loaded')
      })
      .catch(() => {
        if (!cancelled) setLoadState('error')
      })
    return () => {
      cancelled = true
    }
  }, [])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setMessage(null)
    try {
      const result = await saveSettings({ incomingPath, libraryPath, directoryTemplate, fileTemplate, keepSourceFiles })
      if (result.ok) {
        setErrors({})
        show(result.settings)
        setMessage('saved')
      } else {
        const fieldErrors: SettingsFieldErrors = {
          incomingPath: result.errors.incomingPath,
          libraryPath: result.errors.libraryPath,
          directoryTemplate: result.errors.directoryTemplate,
          fileTemplate: result.errors.fileTemplate,
        }
        setErrors(fieldErrors)
        if (fieldErrors.incomingPath?.length) incomingRef.current?.focus()
        else if (fieldErrors.libraryPath?.length) libraryRef.current?.focus()
        else if (fieldErrors.directoryTemplate?.length) directoryTemplateRef.current?.focus()
        else if (fieldErrors.fileTemplate?.length) fileTemplateRef.current?.focus()
        // A 400 with no field we show (e.g. a malformed body) still needs feedback.
        else setMessage('failed')
      }
    } catch {
      setMessage('failed')
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <h1 className="text-2xl font-semibold">Settings</h1>
      {loadState === 'loading' && <p className="mt-4 text-muted">Loading settings…</p>}
      {loadState === 'error' && (
        <p role="alert" className="mt-4 flex items-center gap-2 text-destructive">
          <CircleAlert aria-hidden="true" className="size-5" />
          Settings couldn't be loaded.
        </p>
      )}
      {loadState === 'loaded' && (
        <form onSubmit={handleSubmit} noValidate className="mt-6 space-y-6">
          <TextField
            id="incomingPath"
            label="Incoming directory"
            hint="Full path to the folder you drop new audiobooks into, e.g. /incoming in Docker."
            value={incomingPath}
            onChange={setIncomingPath}
            errors={errors.incomingPath}
            inputRef={incomingRef}
          />
          <TextField
            id="libraryPath"
            label="Library directory"
            hint="Full path to the organised library, e.g. /library in Docker."
            value={libraryPath}
            onChange={setLibraryPath}
            errors={errors.libraryPath}
            inputRef={libraryRef}
          />
          <TextField
            id="directoryTemplate"
            label="Directory template"
            hint={tokenHint}
            value={directoryTemplate}
            onChange={setDirectoryTemplate}
            errors={errors.directoryTemplate}
            inputRef={directoryTemplateRef}
          />
          <TextField
            id="fileTemplate"
            label="File template"
            hint={`${tokenHint} The extension is added for you.`}
            value={fileTemplate}
            onChange={setFileTemplate}
            errors={errors.fileTemplate}
            inputRef={fileTemplateRef}
          />
          <div className="flex items-center gap-2">
            <input
              id="keepSourceFiles"
              type="checkbox"
              checked={keepSourceFiles}
              onChange={(event) => setKeepSourceFiles(event.target.checked)}
              className="size-4"
            />
            <label htmlFor="keepSourceFiles">Keep source files after processing</label>
          </div>
          <div className="flex items-center gap-4">
            <button
              type="submit"
              disabled={saving}
              className="rounded bg-foreground px-4 py-2 font-medium text-background disabled:opacity-60"
            >
              Save
            </button>
            <p role="status">{message === 'saved' ? 'Settings saved.' : ''}</p>
          </div>
          {message === 'failed' && (
            <p role="alert" className="flex items-center gap-2 text-destructive">
              <CircleAlert aria-hidden="true" className="size-5" />
              Settings couldn't be saved. Please try again.
            </p>
          )}
        </form>
      )}
    </>
  )
}

interface TextFieldProps {
  id: string
  label: string
  hint: string
  value: string
  onChange: (value: string) => void
  errors: string[] | undefined
  inputRef: RefObject<HTMLInputElement | null>
}

function TextField({ id, label, hint, value, onChange, errors, inputRef }: TextFieldProps) {
  const hasErrors = !!errors?.length
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  return (
    <div>
      <label htmlFor={id} className="font-medium">
        {label}
      </label>
      <p id={hintId} className="text-sm text-muted">
        {hint}
      </p>
      <input
        id={id}
        ref={inputRef}
        type="text"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={hasErrors || undefined}
        aria-describedby={hasErrors ? `${hintId} ${errorId}` : hintId}
        spellCheck={false}
        autoComplete="off"
        className={inputClass}
      />
      {hasErrors && (
        <ul id={errorId} className="mt-1 text-sm text-destructive">
          {errors.map((error) => (
            <li key={error} className="flex items-center gap-1">
              <CircleAlert aria-hidden="true" className="size-4 shrink-0" />
              {error}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
