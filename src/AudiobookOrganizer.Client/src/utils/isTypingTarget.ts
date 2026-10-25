/** True when a key press on `target` types text: a form field or an editable element. Shortcuts skip these. */
export function isTypingTarget(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) return false
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement)
    return true
  // jsdom doesn't implement isContentEditable, so check the attribute as well.
  if (target.isContentEditable) return true
  const editable = target.closest('[contenteditable]')
  return editable !== null && editable.getAttribute('contenteditable') !== 'false'
}
