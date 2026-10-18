# Slice 02: Client scaffold with API status

**Phase:** 1, Foundation
**Goal:** a Vite + React 19 + TypeScript + Tailwind 4 client with Vitest, whose dashboard page shows the API health status through the `/api` dev proxy, runnable through `scripts/start-client.ps1`.

## Done when
- [x] `npx tsc --noEmit` in `src/AudiobookOrganizer.Client` passes
- [x] `npm test` in `src/AudiobookOrganizer.Client` passes (at least the tests below)
- [x] `dotnet build src/AudiobookOrganizer.Api` and `dotnet test src/AudiobookOrganizer.Api.Tests` still pass, so all four checks are green
- [x] With the API and client both running, http://localhost:5173 shows "API: ok" and "Database: ok"
- [x] With the API stopped, the page shows an "API unreachable" message instead of crashing

## Read first
- `AGENTS.md`: Stack (frontend packages, the styling rules: no `tailwind.config.js` or `postcss.config.js`; state through `services/api.ts`), Layout (client folders), Commands, Ports (API 5070, client 5173)
- `src/AudiobookOrganizer.Api/Controllers/HealthController.cs` and `Models/Api/HealthResponse.cs`: the response shape (`status`, `database`; 200 or 503)
- `scripts/start-api.ps1`: the style to match for the new script

## Tasks
- [x] **Scaffold.** Create `src/AudiobookOrganizer.Client` from Vite's `react-ts` template and confirm React is 19.x. Delete the demo assets and counter. Add `tailwindcss` and `@tailwindcss/vite` (the Vite plugin, with no PostCSS config), `react-router-dom` and `lucide-react`. Set `src/index.css` to `@import "tailwindcss";` plus a `:root` block with a few colour tokens (background, foreground, muted, destructive) that the page uses. Set `index.html` to `lang="en"` with the title "Audiobook Organizer". Verify: `npm run build` succeeds.
- [x] **Test setup.** Add `vitest`, `jsdom`, `@testing-library/react`, `@testing-library/jest-dom` and `@testing-library/user-event` as dev dependencies. Configure the `test` block in `vite.config.ts` (jsdom environment, a setup file that imports jest-dom). Add `"test": "vitest run"` to `package.json`. Verify: `npm test` runs.
- [x] **API client.** Add `src/types/health.ts` (`HealthResponse { status: string; database: string }`) and `src/services/api.ts` with `getHealth(): Promise<HealthResponse>`. It fetches `/api/health` and returns the body for a 200 or 503 (both carry the JSON). It throws on network failure or any other status. Add a `server.proxy` in `vite.config.ts` from `/api` to `http://localhost:5070`, and `server.port: 5173` with `strictPort: true`. Verify: the tests below.
- [x] **Dashboard page.** Add `src/pages/DashboardPage.tsx` with an `<h1>` reading "Audiobook Organizer" and a status region (`role="status"`). The region shows "Checking API…" while loading, then "API: {status}" and "Database: {database}", or "API unreachable" with a lucide icon (`aria-hidden`) on error. `src/App.tsx` sets up `BrowserRouter` with a single route `/` → `DashboardPage`. `main.tsx` renders `App` and imports `index.css`. Verify: the tests below and the manual check.
- [x] **Run script.** Add `scripts/start-client.ps1`. It changes to the client folder, runs `npm install` if `node_modules` is missing, then `npm run dev`. Verify: run it and open http://localhost:5173.
- Data: none.
- Auth: none (no authentication by decision).
- Accessibility: one `<h1>`. Status changes are announced through `role="status"`. Icons are decorative (`aria-hidden="true"`), and the error state uses text as well as colour. Text and background tokens meet WCAG AA contrast (4.5:1).

## Tests to add
- `src/services/api.test.ts`: `getHealth` returns the parsed body for a 200 response (mock `fetch`).
- `src/services/api.test.ts`: `getHealth` returns the body for a 503 response and throws when `fetch` rejects.
- `src/pages/DashboardPage.test.tsx`: renders "API: ok" and "Database: ok" when `getHealth` resolves (mock `services/api`).
- `src/pages/DashboardPage.test.tsx`: renders "API unreachable" when `getHealth` rejects.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`, then open http://localhost:5173. The page shows "API: ok" and "Database: ok".
- Stop the API and reload. The page shows "API unreachable".
- Tab through the page and check that nothing is focus-trapped and no keyboard-only controls are missing (there are no controls yet).

## Out of scope
- shadcn/ui components and `components/ui/`: added when a screen needs one.
- Layout shell, navigation, dark mode.
- Serving the built client from the API's `wwwroot` and the Docker setup: slice 03.

## Notes
- **Conflict check:** consistent with AGENTS.md (stack, ports, no Tailwind or PostCSS config files, `services/api.ts` as the only backend caller) and the no-auth decision. `jsdom` and `@testing-library/user-event` count as part of "Vitest + Testing Library" in the stack, not as new dependencies. `user-event` isn't strictly needed yet. If it goes unused, leave it out.
- **Size:** 5 tasks. About 14 files, mostly scaffold (`package.json`, `tsconfig*.json`, `index.html`, `vite.config.ts`). There are 7 hand-written files: `api.ts`, `health.ts`, `DashboardPage.tsx`, `App.tsx`, `main.tsx`, `index.css` and the setup file, plus two test files and the script.
- **Commit:** `chore: slice 02 client-scaffold` (Foundation scaffold).
- **Still in phase 1:** slice 03 covers the Docker multi-stage build (client into `wwwroot`, API serves the static files with an SPA fallback), Compose with the `/data`, `/incoming` and `/library` volumes on port 8080, a default `Database:Path` that can't collide with `Data/` (slice 01 bug), and a test for the health 503 path.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | `dotnet build src/AudiobookOrganizer.Api`: 0 warnings, 0 errors |
| Typecheck | Passed, but ineffective | `npx tsc --noEmit` exits 0 yet covers 0 files: the root `tsconfig.json` has `files: []` plus project references. `npx tsc -b --noEmit` also exits 0 and does check the app and node configs. |
| Backend tests | 3 passed, 0 failed | |
| Frontend tests | 6 passed, 0 failed | `npm test` (2 files) |
| New tests | 4 of 4 present, passing | plus 1 extra: `getHealth` throws on 500 |
| Manual: page with API running | Passed | Playwright: h1 "Audiobook Organizer", status shows "API: ok" / "Database: ok"; `/api/health` via 5173 proxy returns 200 |
| Manual: page with API stopped | Passed | Status shows "API unreachable"; 2 console errors (the failed proxied fetch), no crash |
| Manual: keyboard tab-through | Not run | No focusable controls yet |

**Data:** none.

## Outcome (2026-10-18, commit 08d522b)

**Status:** Implemented
**Summary:** This slice adds the Vite 8 + React 19 + TypeScript 6 + Tailwind 4 client, with Vitest and Testing Library on jsdom. `services/api.ts` has `getHealth()` and there's a dashboard page that shows the API and database status, or "API unreachable". It also adds the `/api` → 5070 dev proxy on port 5173 (strict) and `scripts/start-client.ps1`. All four checks pass and both manual checks pass.
**Changed:** 19 code/config files plus 2 docs. Key files: `src/services/api.ts`, `src/pages/DashboardPage.tsx`, `src/setupTests.ts`, `vite.config.ts`, `src/index.css`, `scripts/start-client.ps1`.
**Deviations:**
- The AGENTS.md typecheck command `npx tsc --noEmit` checked 0 files (Vite root tsconfig is references only). *Resolved:* developer approved changing the command to `npx tsc -b --noEmit`; AGENTS.md updated.
- The template's oxlint script and config, the demo assets and the template README were removed. *Fine.* oxlint isn't in the stack.
- `@testing-library/user-event` was left out. *Fine.* It isn't used yet, as the slice Notes allowed.
- `setupTests.ts` calls `cleanup()` explicitly. *Fine.* Vitest globals are off.
- There's one extra test (`getHealth` throws on a 500). *Improvement.*
- Colour tokens are defined in `:root` and exposed through `@theme inline`, all at 6.5:1 contrast or better on white. *Fine.*
**Follow-ups:**
- Slice 03: Docker, the default database path fix, and the health 503 test (carried over from slice 01).
