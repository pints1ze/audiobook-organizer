# Slice 76: shadcn setup and app shell

**Phase:** 10, Review workspace
**Goal:** shadcn's packages, theme tokens and first Radix component are in place, and the top bar becomes the new shell: a wide layout with the workflow pages up front and People, Genres and Settings under a "More" menu.

## Done when
- [x] `radix-ui`, `class-variance-authority`, `clsx` and `tailwind-merge` are dependencies of the client (approved by the developer, 2026-10-25); no other package is added.
- [x] `src/utils/cn.ts` exports shadcn's `cn` (clsx + tailwind-merge), with a test.
- [x] `src/index.css` has shadcn's neutral theme tokens (background, foreground, card, popover, primary, secondary, accent, border, input, ring, muted-foreground, destructive and their foregrounds) mapped in `@theme inline`; the existing `--muted` text token keeps its meaning so the ~40 `text-muted` uses don't change.
- [x] `src/components/ui/dropdown-menu.tsx` is shadcn's dropdown menu, with its imports pointing at `../../utils/cn` and any `bg-muted`/`text-muted` classes changed to the tokens above.
- [x] The top bar is full width: app name on the left; nav **Dashboard** (`/`), **Incoming**, **Preview**, **Jobs**; a **More** button opening a menu with People, Genres and Settings links. The active page is marked (`aria-current="page"` from `NavLink`), including "More" when on one of its pages. `<main>` widens to `max-w-7xl`.
- [x] All four checks pass.

## Read first
- `AGENTS.md` (Stack: shadcn rules, "plain HTML for buttons"), `docs/UIUX.md`, `docs/CURRENT-STATE.md`
- `src/AudiobookOrganizer.Client/src/components/layout/AppLayout.tsx` and `AppLayout.test.tsx`
- `src/AudiobookOrganizer.Client/src/index.css`, `src/setupTests.ts`, `vite.config.ts`
- shadcn's dropdown-menu source (new-york style, `radix-ui` package imports: `import { DropdownMenu as DropdownMenuPrimitive } from "radix-ui"`)

## Tasks
- [x] Install the four packages with `npm install` in `src/AudiobookOrganizer.Client`. Files: `package.json`, `package-lock.json`.
- [x] Add `utils/cn.ts` and `utils/cn.test.ts`. No `@/` path alias: shadcn files import with relative paths (no change to `tsconfig*.json` or `vite.config.ts`).
- [x] Add the theme tokens to `index.css` (shadcn neutral palette, light only) and copy `components/ui/dropdown-menu.tsx`, keeping only the parts used (`DropdownMenu`, `Trigger`, `Content`, `Item`; delete the rest so there's no dead code).
- [x] Rework `AppLayout.tsx`: full-width header, the four nav links, and the More menu with `DropdownMenuItem asChild` wrapping each `NavLink`. The skip link stays. Files: `AppLayout.tsx`, `AppLayout.test.tsx`; if Radix needs jsdom stubs (`hasPointerCapture`, `scrollIntoView`, `ResizeObserver`), add them to `setupTests.ts`.
- Data: none
- Auth: none
- Accessibility: the More trigger is a native `<button>` with `aria-haspopup`/`aria-expanded` from Radix, opened by Enter, Space or ArrowDown; arrow keys move between items; Escape closes and returns focus to the trigger. Focus ring and contrast stay at AA with the new tokens.

## Tests to add
- `utils/cn.test.ts`: `cn('px-2', false && 'hidden', 'px-4')` is `'px-4'`.
- `AppLayout.test.tsx`: the Main nav lists Dashboard, Incoming, Preview and Jobs, and not People, Genres or Settings directly.
- `AppLayout.test.tsx`: pressing Enter on "More" opens a menu with People, Genres and Settings; choosing Settings navigates to the Settings page; Escape closes the menu and focus returns to "More".
- Update existing `AppLayout` tests that expect the old flat nav.

## Manual checks
- In the running client: open More with the mouse and with the keyboard, visit each page, check the focus ring and that the header spans the window at 1440px and wraps sensibly at 768px.

## Out of scope
- The review workspace itself, the scan header, retiring Dashboard, Incoming or Preview (later phase 10 slices).
- Dark mode.
- A shadcn Button component (AGENTS.md: plain HTML for buttons).

## Notes
- Conflict check: the four packages are listed in AGENTS.md's Stack and were approved for this slice (CURRENT-STATE.md, 2026-10-25). `--muted` is kept as a text colour because existing pages use `text-muted`; shadcn's `muted` background token is not added, to avoid two meanings.
- Remaining phase 10 slices, roughly: the review list API with stages (77); the workspace list with covers and stage filter at `/` (78); the detail pane's match search and accept (79); cover, changed metadata, destination and Approve & next (80); keyboard shortcuts and help popover (81); the scan header and retiring Dashboard/Incoming/Preview from the nav (82).
