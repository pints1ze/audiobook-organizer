import { Keyboard } from 'lucide-react'
import { Popover, PopoverContent, PopoverTrigger } from '../ui/popover'

const shortcuts: { keys: string[]; action: string }[] = [
  { keys: ['j'], action: 'Next book' },
  { keys: ['k'], action: 'Previous book' },
  { keys: ['a'], action: 'Approve & next (Ready books)' },
  { keys: ['/'], action: 'Search the catalog' },
  { keys: ['Enter'], action: 'Submit a form or press the focused button' },
  { keys: ['?'], action: 'Show or hide these shortcuts' },
]

/** The Review page's "Keyboard shortcuts" button and popover. `open` is controlled so `?` can toggle it. */
export default function ShortcutsHelp({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Popover open={open} onOpenChange={onOpenChange}>
      <PopoverTrigger asChild>
        <button
          type="button"
          className="flex items-center gap-2 rounded border border-border px-3 py-1 text-sm hover:bg-accent"
        >
          <Keyboard aria-hidden="true" className="size-4" />
          Keyboard shortcuts
        </button>
      </PopoverTrigger>
      <PopoverContent align="end" aria-labelledby="shortcuts-heading" className="w-80">
        <h2 id="shortcuts-heading" className="font-semibold">
          Keyboard shortcuts
        </h2>
        <p className="mt-1 text-sm text-muted">Not while typing in a field.</p>
        <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-2 text-sm">
          {shortcuts.map((s) => (
            <div key={s.keys.join()} className="contents">
              <dt>
                <kbd className="rounded border border-border bg-secondary px-1.5 py-0.5 font-mono text-xs">
                  {s.keys.join(' ')}
                </kbd>
              </dt>
              <dd>{s.action}</dd>
            </div>
          ))}
        </dl>
      </PopoverContent>
    </Popover>
  )
}
