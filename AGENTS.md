# AGENTS.md

Working rules for any coding agent in this repo. Project-specific details are at the bottom.

## 1. How to talk to me

- Lead with the answer or the next action. No preamble, no recap, no "Hope this helps."
- One or two sentences beats five paragraphs. Expand only if I ask.
- Multi-step instructions are numbered. Lists cap at 5 items.
- Point to exact locations: `path/file.cs:42`, not "in the auth code."
- Errors are stated plainly: what failed, why, what you'll try next.
- No tangents. If you notice something unrelated, add one line under "Noticed" at the end.
- End every reply with:
  - **Done:** what changed (files touched)
  - **Not checked / risks:** anything skipped or assumed
  - **Next:** one concrete step

## 2. Before writing code

Size the task first.

- **Small** (one file, obvious fix): restate the goal in one sentence and do it.
- **Medium** (a few files, clear goal): post a short plan in chat (goal, done when, tasks) and wait for "go."
- **Feature work on a larger build:** use the slice workflow (section 3).

If the requirements are unclear at any size, ask (max 3 questions at a time) before writing code.

## 3. Plan and slices

Larger builds go top-down: a goal, then phases, then slices.

- **`docs/PLAN.md`** is the overarching plan: the goal, what "finished" means, and the phases (milestones) that get there. Keep it short. Detail belongs in slices, written when they're next, not up front.
- **Planning** comes first: `/itc-plan-project` brainstorms with me, then writes `docs/PRD.md` (what and why), `PLAN.md` and `CURRENT-STATE.md`. Rerun it to add a large feature.
- **A slice** is one verifiable step toward a phase: one agent session, one `feat:` commit. Each lives in `docs/slices/NN-name.md` and goes through four skills, in order:
  1. **Write:** `/itc-write-next-slice`
  2. **Implement:** `/itc-implement-slice NN` (tests first, ticks the checklist, commits the code)
  3. **Test:** `/itc-run-slice-tests NN` adds `## Test results`
  4. **Review:** `/itc-review-completed-slice NN` adds `## Outcome`, updates `PLAN.md` and `CURRENT-STATE.md`, commits the docs and pushes
- **`/itc-next`** runs those four for the next slice and stops at a gate (below). `/itc-next phase` keeps going until the phase ends; `/itc-next 3` runs up to three slices.
- **`docs/CURRENT-STATE.md`** is the one-page "you are here": branch, last slice, next slice, open questions, decisions.
- Templates for every doc above live in `docs/templates/`. Skills copy from there; don't invent a layout.

**Slice size:** at most 5 tasks and about 8 files touched. Bigger gets split, and the writer keeps only the first piece.

**Gates.** Stop and ask me at any of these. Never guess past one, and never start the next step:
1. A `[NEEDS CLARIFICATION]` inside a slice's scope.
2. A failing test I haven't listed as accepted in `CURRENT-STATE.md`.
3. A "Done when" item that can't be met, or a slice that turns out wrong or too big mid-way.
4. A deviation classified **needs decision**, a new dependency, or a change to AGENTS.md or to PLAN.md's phases.
5. The end of a phase. I verify the phase and merge its PR before the next one starts.

Rules:
- Read `CURRENT-STATE.md` first in every session. (The Claude Code SessionStart hook and the Cursor rule print it for you; if you don't see it, read it.)
- Every slice belongs to a phase. New work that isn't in `PLAN.md` gets added there, with my OK, before it's sliced.
- Do only the step you were asked to do, then stop. Only `/itc-next` chains steps.
- Don't write a new slice until the previous one is reviewed.
- Only I can accept a failing test. Agents report failures; they never wave them through.
- Phase 1 sets up both test projects. A build with no automated tests is never "done."

## 4. Keep it simple

Read the code the change touches before picking a solution. Then stop at the first rung that works:

1. Does this need to exist at all?
2. Is it already in this codebase? Reuse it.
3. Does the standard library or framework do it?
4. Does the platform do it natively (browser, OS, database)?
5. Is there an already-installed dependency for it?
6. Only then: write the minimum code that works.

Rules:
- No new dependencies without asking.
- No abstractions, interfaces, or config options for a single use case.
- No speculative features ("we might need this later"). YAGNI.
- Don't refactor code outside the task unless I ask.
- If you take a deliberate shortcut, mark it: `// shortcut: <why> — revisit when <condition>`.

**Never cut corners on:** input validation at trust boundaries, error handling that prevents data loss, security, and accessibility (WCAG 2.1 AA). Simple does not mean negligent.

## 5. Tests

- Anything with logic (a branch, a loop, parsing, money, auth, permissions) gets at least one test.
- For bug fixes: write a test that reproduces the bug, watch it fail, then fix it.
- For new logic: test first when practical. Red, then green, then clean up.
- Trivial code (pass-through, markup, config) doesn't need a test.
- Never delete or weaken a failing test to make it pass. Tell me.

## 6. Definition of done

Don't say "done" until you have evidence:

- [ ] Project builds with no new warnings
- [ ] All tests pass (run them; don't assume)
- [ ] Every "Done when" item in the plan or slice is met
- [ ] You re-read the diff once for leftovers (debug code, commented-out blocks, TODOs you added)

If you can't verify something (no test runner, needs a live server, needs credentials), say so under "Not checked."

## 7. When stuck

- Same fix fails twice: stop guessing. Find the root cause (read the error, trace the data, check assumptions) before trying again.
- Still stuck after that: tell me what you tried and what you think is wrong. Don't spiral.

## 8. Git

Source control is the audit trail. Every slice leaves commits I can read, and nothing lands on `main` except through a PR I merge.

- **Branch:** one per phase, `phase/N-short-name`, off `main`. `/itc-next` creates it when a phase starts and records it under **Branch** in `CURRENT-STATE.md`. Always work on the branch named there. Never commit to `main`.
- **Commits:** two per slice, made by the skills, nothing else: `feat: slice NN <name>` (code and tests, end of implement; `fix:` for a bug-fix slice) and `docs: slice NN outcome` (test results, outcome, PLAN and CURRENT-STATE updates, end of review). Use `chore:` only for the Foundation scaffold.
- **Push** the phase branch after every review commit.
- **PR:** when a phase is done, open one from the phase branch to `main`: `tea pr create` (Gitea), or `gh pr create` on GitHub. If neither CLI is installed, print the branch name and the PR body so I can open it. Never merge; never force-push; never touch other branches.
- Never commit secrets, connection strings, `.env` files or `secrets.local.*`.

---

## Project specifics

AudiobookOrganizer (Audiobook Library Manager) is a self-hosted, single-user web app. It scans an incoming folder of MP3/M4B audiobooks, groups the files into books, looks up and normalizes their metadata, tags the files and moves them into an organized library.
Product detail lives in `docs/PRD.md`. UI rules will go in `docs/UIUX.md` once it's written. `overview.md` is the original requirements draft; where they differ, the PRD wins.

### Stack (don't deviate or add packages)

- **Backend:** .NET 10, ASP.NET Core MVC controllers, SQLite + Dapper, Serilog
- **Frontend:** Vite, React 19, TypeScript, Tailwind CSS 4, react-router-dom, lucide-react
- **UI components:** shadcn/ui, copied into `src/components/ui/` and owned by us. Allowed packages behind it: `radix-ui`, `class-variance-authority`, `clsx`, `tailwind-merge`. Add a component only when (a) a Figma Make export already uses it, or (b) it needs Radix behavior you'd otherwise hand-roll: dialog, dropdown/context menu, popover, combobox/select, tabs, tooltip. Plain HTML for buttons, inputs, cards and layout.
- **Tests:** xUnit + `WebApplicationFactory` (backend), Vitest + Testing Library (frontend)
- **State:** React built-ins plus `services/api.ts`. No Redux, Zustand, React Query, etc.
- **Styling:** CSS custom properties and utility classes in `src/index.css`, including shadcn's theme tokens. No `tailwind.config.js` or `postcss.config.js`.
- **Extra packages for this project:** TagLibSharp (tag reading for both formats; tag and cover writing for MP3, and for M4B after the ffmpeg remux). No auth packages: the app runs without login on a trusted LAN.
- **External tools:** ffmpeg/ffprobe, installed in the Docker image and required on PATH in dev and for tests. They handle the M4B remux (stream copy, `-c copy`, never re-encode) and M4B chapter reads. M4B copies are remuxed by ffmpeg with no chapters (`-map_chapters -1`, `-movflags disable_chpl`), then tagged by TagLibSharp; MP3 copies keep their chapters. Never save an M4B that still has a `chpl` atom with TagLibSharp: it corrupts it (slices 04, 60). Call the tools only through `Services/FfmpegRunner.cs`, passing an argument list rather than a shell string.
- **Metadata providers:** the Audible catalog API (`api.audible.com/1.0/catalog/products`, unofficial, keyless) is primary and Open Library is the fallback. There's one client per provider in `Services/`, behind a shared provider interface.

### Layout

```
src/AudiobookOrganizer.Api/
  Controllers/      thin; push logic into Services/
  Services/         business logic and external API clients (one client per external system)
  Data/             Dapper repositories + DatabaseInitializer (schema lives here)
  Models/Api/       DTOs returned to the client
  Models/Local/     SQLite entities
  Models/<Ext>/     external API shapes (Audible/, OpenLibrary/), matching their JSON exactly
  Configuration/    strongly typed settings classes
src/AudiobookOrganizer.Api.Tests/   mirrors the Api folders; AudiobookOrganizerWebApplicationFactory for integration tests
src/AudiobookOrganizer.Client/src/
  pages/            one per route, test file beside it
  components/       grouped by feature, plus layout/ and shared/
  components/ui/    shadcn/ui components we've pulled in (see Stack); edit freely, they're ours
  services/api.ts   the only place the client calls the backend
  types/            TS mirrors of the API models
  utils/            small pure helpers, each with a test
docs/               PRD, UI notes, API notes; docs/fixtures/ holds real JSON responses for tests
docs/fixtures/audio/   small sample MP3/M4B files (with chapters) for tag and grouping tests
docs/PLAN.md        goal, finished-when, phases (see section 3)
docs/CURRENT-STATE.md   start here: branch, last/next slice, open questions, decisions
docs/slices/        one file per slice, NN-short-name.md
docs/templates/     the blank PRD, PLAN, CURRENT-STATE and slice files the skills copy from
scripts/            start-api.ps1, start-client.ps1
Dockerfile, docker-compose.yml   production image: API serves the built client from wwwroot
reference/          read-only reference material, never import from it
```

### Commands (PowerShell, from repo root)

- **Build:** `dotnet build src/AudiobookOrganizer.Api`
- **Test (backend):** `dotnet test src/AudiobookOrganizer.Api.Tests`
- **Typecheck:** `npx tsc -b --noEmit` in `src/AudiobookOrganizer.Client` (`-b` follows the project references; plain `tsc --noEmit` checks nothing)
- **Test (frontend):** `npm test` in `src/AudiobookOrganizer.Client`
- **Run locally:** `scripts/start-api.ps1` (API) and `scripts/start-client.ps1` (Vite, proxies `/api` to the API)
- **Ports:** API 5070, client 5173, container 8080
- **Package:** `docker compose build` (only when I ask)

"Done" means all four checks pass: build, backend tests, typecheck, frontend tests.

### Environment

- **Shell:** Windows, Git Bash / PowerShell (not WSL). Use commands and paths that work there.
- **Deploy target:** a Linux Docker container via Docker Compose on a home server. Single user, no authentication, trusted LAN only. Incoming and library folders are mounted volumes (`/incoming`, `/library`) and may be on different volumes. The SQLite database and logs live under `/data`. Code must handle both Windows (dev) and Linux (container) paths. Don't change the volume layout or the Dockerfile base images without asking.
- **File safety:** scans are read-only. Source files are never tagged in place. A source is deleted only after its destination copy is written, tagged and read back. Never overwrite an existing destination file.
- **Secrets:** use user secrets or `.env` for local config. Encrypt stored credentials at rest. Never read from, write to or quote `secrets.local.*`.

### Don't touch

- Build output: `publish/`, `src/AudiobookOrganizer.Client/dist/`, `src/AudiobookOrganizer.Api/wwwroot/` (the Docker build fills it), `bin/`, `obj/`
- Local state: `*.db`, `logs/`, `data/`
- Real audiobook folders outside `docs/fixtures/`: tests use fixtures or temp directories only
- `reference/` is read-only.
