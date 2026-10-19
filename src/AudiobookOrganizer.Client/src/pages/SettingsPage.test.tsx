import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getSettings, saveSettings } from '../services/api'
import SettingsPage from './SettingsPage'

vi.mock('../services/api', () => ({ getSettings: vi.fn(), saveSettings: vi.fn() }))

const getSettingsMock = vi.mocked(getSettings)
const saveSettingsMock = vi.mocked(saveSettings)

const saved = { incomingPath: '/incoming', libraryPath: '/library', keepSourceFiles: true }
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
  })

  it('shows null paths as empty fields', async () => {
    getSettingsMock.mockResolvedValue({ incomingPath: null, libraryPath: null, keepSourceFiles: false })

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
      settings: { incomingPath: '/in2', libraryPath: '/library', keepSourceFiles: false },
    })
    render(<SettingsPage />)
    const incoming = await screen.findByLabelText('Incoming directory')

    fireEvent.change(incoming, { target: { value: '/in2' } })
    fireEvent.click(screen.getByLabelText('Keep source files after processing'))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Settings saved.')).toHaveAttribute('role', 'status')
    expect(saveSettingsMock).toHaveBeenCalledWith({ incomingPath: '/in2', libraryPath: '/library', keepSourceFiles: false })
    expect(incoming).toHaveValue('/in2')
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
