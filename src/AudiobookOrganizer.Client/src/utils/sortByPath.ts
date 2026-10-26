const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })

/** A new array ordered by relative path, digit runs by value and case ignored; ties keep their order. */
export function sortByPath<T extends { relativePath: string }>(files: readonly T[]): T[] {
  return [...files].sort((x, y) => collator.compare(x.relativePath, y.relativePath))
}
