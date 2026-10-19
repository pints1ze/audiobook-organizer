import { describe, expect, it } from 'vitest'
import { formatDuration } from './formatDuration'

describe('formatDuration', () => {
  it('formats zero as 0:00', () => {
    expect(formatDuration(0)).toBe('0:00')
  })

  it('formats under an hour as m:ss', () => {
    expect(formatDuration(61_000)).toBe('1:01')
  })

  it('formats an hour or more as h:mm:ss', () => {
    expect(formatDuration(3_723_000)).toBe('1:02:03')
  })

  it('returns "—" for null', () => {
    expect(formatDuration(null)).toBe('—')
  })
})
