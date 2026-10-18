const formatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/** Formats an ISO-8601 UTC timestamp as a short local date and time, or "—" when there is none. */
export function formatDateTime(iso: string | null): string {
  if (!iso) return '—'
  return formatter.format(new Date(iso))
}
