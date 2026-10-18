import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getHealth } from '../services/api'
import DashboardPage from './DashboardPage'

vi.mock('../services/api', () => ({ getHealth: vi.fn() }))

const getHealthMock = vi.mocked(getHealth)

describe('DashboardPage', () => {
  beforeEach(() => {
    getHealthMock.mockReset()
  })

  it('shows the API and database status when the health check succeeds', async () => {
    getHealthMock.mockResolvedValue({ status: 'ok', database: 'ok' })

    render(<DashboardPage />)

    expect(screen.getByRole('heading', { level: 1, name: 'Audiobook Organizer' })).toBeInTheDocument()
    expect(await screen.findByText('API: ok')).toBeInTheDocument()
    expect(screen.getByText('Database: ok')).toBeInTheDocument()
  })

  it('shows "API unreachable" when the health check fails', async () => {
    getHealthMock.mockRejectedValue(new TypeError('Failed to fetch'))

    render(<DashboardPage />)

    expect(await screen.findByText('API unreachable')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('API unreachable')
  })
})
