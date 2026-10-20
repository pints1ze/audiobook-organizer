import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getSettings, saveSettings } from '../services/api'
import SettingsPage from './SettingsPage'

vi.mock('../services/api', () => ({ getSettings: vi.fn(), saveSettings: vi.fn() }))

const getSettingsMock = vi.mocked(getSettings)
const saveSettingsMock = vi.mocked(saveSettings)

const saved = {
  incomingPath: '/incoming',
  libraryPath: '/library',
  directoryTemplate: '%artist%/%album%',
  fileTemplate: '%track:3% %title%',
  keepSourceFiles: true,
}
const overlapError = "The library directory can't be inside the incoming directory."

describe('SettingsPage', () => {
  beforeEach(() => {
    getSettingsMock.mockReset()
    saveSettingsMock.mockReset()
  })

  it('loads and shows the saved values', async () => {
    getSettingsMock.mockResolvedValue(saved)

    render(<SettingsPage />)

    expect(screen.getByText('Loading settings…')).toBeInTheDocument()
    expect(await screen.findByLabelText('Incoming directory')).toHaveValue('/incoming')
    expect(screen.getByLabelText('Library directory')).toHaveValue('/library')
    expect(screen.getByLabelText('Keep source files after processing')).toBeChecked()
    expect(screen.getByLabelText('Directory template')).toHaveValue('%artist%/%album%')
    expect(screen.getByLabelText('File template')).toHaveValue('%track:3% %title%')
    expect(screen.getByLabelText('File template')).toHaveAccessibleDescription(
      expect.stringContaining('The extension is added for you.'),
    )
    expect(screen.getByLabelText('Directory template')).toHaveAccessibleDescription(
      expect.stringContaining('%series-part%'),
    )
  })

  it('shows null paths as empty fields', async () => {
    getSettingsMock.mockResolvedValue({
      incomingPath: null,
      libraryPath: null,
      directoryTemplate: null,
      fileTemplate: null,
      keepSourceFiles: false,
    })

    render(<SettingsPage />)

    expect(await screen.findByLabelText('Incoming directory')).toHaveValue('')
    expect(screen.getByLabelText('Keep source files after processing')).not.toBeChecked()
  })

  it('shows an alert when loading fails', async () => {
    getSettingsMock.mockRejectedValue(new Error('boom'))

    render(<SettingsPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent("Settings couldn't be loaded.")
  })

  it('saves the edited values and shows "Settings saved."', async () => {
    getSettingsMock.mockResolvedValue(saved)
    saveSettingsMock.mockResolvedValue({
      ok: true,
      settings: { ...saved, incomingPath: '/in2', fileTemplate: '%title%', keepSourceFiles: false },
    })
    render(<SettingsPage />)
    const incoming = await screen.findByLabelText('Incoming directory')

    fireEvent.change(incoming, { target: { value: '/in2' } })
    fireEvent.change(screen.getByLabelText('File template'), { target: { value: '  %title%  ' } })
    fireEvent.click(screen.getByLabelText('Keep source files after processing'))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Settings saved.')).toHaveAttribute('role', 'status')
    expect(saveSettingsMock).toHaveBeenCalledWith({
      incomingPath: '/in2',
      libraryPath: '/library',
      directoryTemplate: '%artist%/%album%',
      fileTemplate: '  %title%  ',
      keepSourceFiles: false,
    })
    expect(incoming).toHaveValue('/in2')
    expect(screen.getByLabelText('File template')).toHaveValue('%title%')
  })

  it('shows the library error under its field, marks it invalid and focuses it', async () => {
    getSettingsMock.mockResolvedValue(saved)
    saveSettingsMock.mockResolvedValue({ ok: false, errors: { libraryPath: [overlapError] } })
    render(<SettingsPage />)
    const library = await screen.findByLabelText('Library directory')

    fireEvent.change(library, { target: { value: '/incoming/lib' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(overlapError)).toBeInTheDocument()
    expect(library).toHaveAttribute('aria-invalid', 'true')
    expect(library).toHaveAccessibleDescription(expect.stringContaining(overlapError))
    expect(library).toHaveFocus()
    expect(screen.getByLabelText('Incoming directory')).not.toHaveAttribute('aria-invalid')
    expect(library).toHaveValue('/incoming/lib')
  })

  it('shows the file template error under its field and focuses it', async () => {
    const folderError = "The file template can't contain folders; put them in the directory template."
    getSettingsMock.mockResolvedValue(saved)
    saveSettingsMock.mockResolvedValue({ ok: false, errors: { fileTemplate: [folderError] } })
    render(<SettingsPage />)
    const file = await screen.findByLabelText('File template')

    fireEvent.change(file, { target: { value: '%artist%/%album%' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(folderError)).toBeInTheDocument()
    expect(file).toHaveAttribute('aria-invalid', 'true')
    expect(file).toHaveAccessibleDescription(expect.stringContaining(folderError))
    expect(file).toHaveFocus()
    expect(screen.getByLabelText('Directory template')).not.toHaveAttribute('aria-invalid')
  })

  it('focuses the directory template before the file template', async () => {
    getSettingsMock.mockResolvedValue(saved)
    saveSettingsMock.mockResolvedValue({
      ok: false,
      errors: { directoryTemplate: ['Unknown token %author%.'], fileTemplate: ['Enter the file template.'] },
    })
    render(<SettingsPage />)
    const directory = await screen.findByLabelText('Directory template')

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Unknown token %author%.')).toBeInTheDocument()
    expect(screen.getByText('Enter the file template.')).toBeInTheDocument()
    expect(directory).toHaveFocus()
  })

  it('keeps the input and shows an alert when saving fails unexpectedly', async () => {
    getSettingsMock.mockResolvedValue(saved)
    saveSettingsMock.mockRejectedValue(new Error('500'))
    render(<SettingsPage />)
    const incoming = await screen.findByLabelText('Incoming directory')

    fireEvent.change(incoming, { target: { value: '/typed' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("Settings couldn't be saved.")
    expect(incoming).toHaveValue('/typed')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })
})
