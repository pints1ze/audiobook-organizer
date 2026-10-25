import { describe, expect, it } from 'vitest'
import { cn } from './cn'

describe('cn', () => {
  it('drops falsy classes and lets the later Tailwind class win', () => {
    const hidden = false
    expect(cn('px-2', hidden && 'hidden', 'px-4')).toBe('px-4')
  })
})
