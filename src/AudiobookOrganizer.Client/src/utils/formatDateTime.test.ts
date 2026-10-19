import { describe, expect, it } from 'vitest'
import { formatDateTime } from './formatDateTime'

describe('formatDateTime', () => {
  it('formats an ISO UTC string as a local date and time', () => {
    const iso = '2026-10-18T14:30:00Z'
    const expected = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
      new Date(iso),
    )

    const result = formatDateTime(iso)

    expect(result).not.toBe('')
    expect(result).toBe(expected)
  })

  it('returns "—" for null', () => {
    expect(formatDateTime(null)).toBe('—')
  })
})
