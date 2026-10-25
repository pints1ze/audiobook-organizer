# UI and UX rules: AudiobookOrganizer

The job of the UI: get each book from incoming to the library with a glance and a couple of actions. Every screen answers "what needs me next?" first.

## Components
- shadcn/ui defaults, copied into `src/components/ui/` (AGENTS.md, Stack). Theme tokens live in `src/index.css`; no `tailwind.config.js`.
- Plain HTML for buttons, inputs, cards and layout, styled with the shadcn tokens. Radix-backed components only for dialog, dropdown menu, popover, select/combobox, tabs and tooltip.
- Icons from `lucide-react`, `aria-hidden` when a text label sits beside them.

## Layout
- Full-width app shell, not a narrow centred column. Top bar: app name, **Review** (home, `/`), **Jobs**, and a **More** menu (People, Genres, Settings).
- The review workspace is list plus detail: the book list on the left (about 360px, scrolls on its own) and the selected book on the right. The selected book is in the URL (`/?book=<groupId>`), so reload and back keep it.
- Below 1024px wide the list and the detail stack: the list, then the detail of the selected book with a "Back to list" link.

## Books
- Covers everywhere: 48px thumbnails in lists, 160px in the detail pane. No cover → a neutral placeholder with the book's initials, never an empty box.
- Each book shows one **stage** chip: Needs match, Needs review, Ready, Queued, Done, Failed. A chip pairs colour with its text label; colour is never the only signal.
- Show the facts that identify a book: title, author, narrator, series and part, runtime. Grouping confidence and evidence are secondary (the Files tab).

## Actions and keyboard
- One primary action per pane state, as a filled button: Accept match when matching, **Approve & next** when Ready. Others are outline or ghost buttons.
- Shortcuts: `j`/`k` next and previous book, `Enter` accept the highlighted match, `a` approve, `/` focus search. They never fire while typing in a field, are listed in a `?` help popover, and never replace a visible button.
- Destructive or irreversible actions (merge, approve a batch) get an inline confirmation, not a modal, as on the Names pages.

## Feedback and focus
- Status messages are `role="status"`, errors `role="alert"`, beside the control that caused them.
- After an action focus moves somewhere useful: the next book after Approve & next, the status message after a save, the section heading when the pressed button disappears.
- Loading shows a skeleton of the pane's shape, not a bare "Loading…". Long lists don't block the pane: the list and the detail load separately.

## Accessibility (WCAG 2.1 AA)
- Every control keyboard-operable with a visible focus ring; the list is a single tab stop with arrow-key movement inside it (`role="listbox"` or a list of links, decided in the slice).
- Text contrast 4.5:1, large text and UI parts 3:1, in the shadcn tokens as shipped.
- Thumbnails are decorative (`alt=""`) when the title is beside them.
