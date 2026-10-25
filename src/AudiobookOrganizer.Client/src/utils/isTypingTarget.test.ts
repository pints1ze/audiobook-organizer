import { describe, expect, it } from 'vitest'
import { isTypingTarget } from './isTypingTarget'

describe('isTypingTarget', () => {
  it.each(['input', 'textarea', 'select'])('is true for a %s', (tag) => {
    expect(isTypingTarget(document.createElement(tag))).toBe(true)
  })

  it('is true for a contenteditable element', () => {
    const div = document.createElement('div')
    div.setAttribute('contenteditable', 'true')
    document.body.append(div)
    expect(isTypingTarget(div)).toBe(true)
    div.remove()
  })

  it('is false for a button, a link, the body and no target', () => {
    expect(isTypingTarget(document.createElement('button'))).toBe(false)
    expect(isTypingTarget(document.createElement('a'))).toBe(false)
    expect(isTypingTarget(document.body)).toBe(false)
    expect(isTypingTarget(null)).toBe(false)
  })
})
