# Slice 03: Docker image and Compose

**Phase:** 1, Foundation
**Goal:** `docker compose up --build` serves the built client and the API on port 8080 from one Linux container, with the SQLite file on a mounted `/data` volume. The slice also fixes the slice 01 default-database-path bug and tests the health 503 path.

## Done when
- [x] All four checks pass: build, backend tests, `npx tsc -b --noEmit`, frontend tests
- [x] `docker compose up --build` starts the container. http://localhost:8080/api/health returns 200 `{"status":"ok","database":"ok"}`, and http://localhost:8080/ shows the dashboard with "API: ok"
- [x] The database file is at `/data/audiobooks.db` in the container, in the host folder mounted there, and survives `docker compose down` followed by `up`
- [x] The dev default database path no longer lands inside `src/AudiobookOrganizer.Api/Data/`
- [x] `GET /api/health` returns 503 `{"status":"degraded","database":"error"}` when the database can't be opened (automated test)

## Read first
- `AGENTS.md`: Environment (Docker, volumes `/incoming`, `/library`, `/data`, container port 8080), Commands, Don't touch (`wwwroot/` is filled by the build)
- `docs/slices/01-api-scaffold-health.md` Outcome (path bug, 503 debt)
- `src/AudiobookOrganizer.Api/Program.cs`, `appsettings.json`, `Data/SqliteConnectionFactory.cs`
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizerWebApplicationFactory.cs`, `Controllers/HealthControllerTests.cs`
- `src/AudiobookOrganizer.Client/vite.config.ts`, `package.json` (build output goes to `dist/`)

## Tasks
- [x] **Health 503 test.** In `Controllers/HealthControllerTests.cs`, add a test that uses its own factory instance (not the shared class fixture). It starts the app, then calls `SqliteConnection.ClearAllPools()`, deletes the database file and creates a *directory* at the same path. It then asserts that `GET /api/health` returns 503 with `status: "degraded"` and `database: "error"`. No production code changes. Verify: the test passes. If it passes before any change, that's expected, because the code path already exists.
- [x] **Default database path.** Change `Database:Path` in `appsettings.json` to `../../data/audiobooks.db`. Relative to the API content root, that's the repo-root `data/` folder, which AGENTS.md already lists as local state. Add `/data/` (anchored to the root, so it can't match `src/**/Data/`) to the root `.gitignore`, and update the comment there. Verify: run `scripts/start-api.ps1`, the file appears at `<repo>/data/audiobooks.db`, nothing new appears under `src/AudiobookOrganizer.Api/Data/`, and `git status` is clean.
- [x] **Serve the client from the API.** In `Program.cs`, add `UseDefaultFiles()` and `UseStaticFiles()` before `MapControllers()`, and add `MapFallbackToFile("index.html")` so client routes work. Unknown `/api/*` paths must still return 404 rather than `index.html`, for example by restricting the fallback pattern to paths not starting with `api`. Verify: the test below.
- [x] **Dockerfile and .dockerignore.** Add a multi-stage `Dockerfile` at the repo root:
  1. `node:22` builds the client (`npm ci`, `npm run build`).
  2. `mcr.microsoft.com/dotnet/sdk:10.0` publishes the API (Release).
  3. `mcr.microsoft.com/dotnet/aspnet:10.0` is the runtime, with the published API plus the client's `dist/` copied into `wwwroot/`.

  The runtime stage sets `ASPNETCORE_HTTP_PORTS=8080`, `ASPNETCORE_ENVIRONMENT=Production` and `Database__Path=/data/audiobooks.db`, exposes 8080, and creates `/data`, `/incoming` and `/library`. Add a `.dockerignore` excluding `bin/`, `obj/`, `node_modules/`, `dist/`, `data/`, `.git/` and `*.db`. Verify: `docker build .` succeeds.
- [x] **docker-compose.yml.** Add one service `audiobook-organizer`, built from `.`, with ports `8080:8080` and `restart: unless-stopped`. Volumes are `${DATA_DIR:-./docker/data}:/data`, `${INCOMING_DIR:-./docker/incoming}:/incoming` and `${LIBRARY_DIR:-./docker/library}:/library`. Add `/docker/` to `.gitignore`. Verify: the manual checks below.
- Data: none (no schema). The only change is where the database file lives in dev and in the container.
- Auth: none (no authentication by decision).
- Accessibility: none (no UI changes).

## Tests to add
- `Controllers/HealthControllerTests.cs`: health returns 503 with `degraded` / `error` when the database path can't be opened.
- `Controllers/StaticFilesTests.cs` (or similar): `GET /api/does-not-exist` returns 404, not the SPA fallback.

## Manual checks
- `docker compose up --build -d`. Then `curl http://localhost:8080/api/health` returns 200 ok/ok. A browser at http://localhost:8080/ shows the dashboard with "API: ok" and "Database: ok", and a deep link such as http://localhost:8080/anything also loads the dashboard shell.
- `./docker/data/audiobooks.db` exists on the host. After `docker compose down` and `docker compose up -d` it's still there, and health is ok.
- `docker compose down` when done. Leave `./docker/` in place (it's git-ignored) or delete it, and report which.

## Out of scope
- A published image, registry, or CI build: phase 9.
- Linux file permissions or PUID/PGID for mounted folders: revisit when processing writes to `/library` (phase 8).
- Container health check in Compose: the aspnet image has no curl. Revisit if needed.
- Using `/incoming` and `/library` in code: phase 2 onward.

## Notes
- **Conflict check:** consistent with AGENTS.md (container port 8080, `/data`, `/incoming` and `/library` volumes, `wwwroot` filled by the build, `data/` as local state) and with the slice 01 follow-ups. Base images are the official node 22 and .NET 10 images. Changing them later needs the developer's OK per AGENTS.md.
- **Size:** 5 tasks, about 8 files (`HealthControllerTests.cs`, the new static-files test, `appsettings.json`, `.gitignore`, `Program.cs`, `Dockerfile`, `.dockerignore`, `docker-compose.yml`).
- Docker Desktop (Linux engine) is available on the dev machine.
- **Commit:** `chore: slice 03 docker-compose` (Foundation scaffold). This slice should complete phase 1's Done when.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 6 passed, 0 failed | |
| Frontend tests | 6 passed, 0 failed | |
| New tests | 2 of 2 present, passing | Health 503 test; `StaticFilesTests` has 2 tests (`/api/*` 404, client route serves `index.html`) |
| Manual: `docker compose up --build -d`, health | Passed | `GET :8080/api/health` returns 200 ok/ok |
| Manual: dashboard at :8080 | Passed | Playwright: h1 plus "API: ok" / "Database: ok" |
| Manual: `/api/does-not-exist` | Passed | 404 |
| Manual: volumes | Passed | `/data`, `/incoming` and `/library` exist in the container; `/data/audiobooks.db` matches host `./docker/data/audiobooks.db` |
| Manual: persistence across down/up | Passed | file still there, health 200 after restart |
| Manual: deep link `/anything` | Passed (implementer run) | Serves the shell. The client has no matching route, so the body is empty; client routing is out of scope |

`docker compose down` run afterwards. `./docker/` left in place (git-ignored).

## Outcome (2026-10-18, commit c893295)

**Status:** Implemented
**Summary:** This slice adds a multi-stage Dockerfile (node 22 client build, .NET 10 publish, aspnet runtime with the client in `wwwroot`) and `docker-compose.yml` on 8080 with the `/data`, `/incoming` and `/library` bind mounts. The API now serves static files with an SPA fallback, and unknown `/api/*` paths return 404. The dev database moved to the repo-root `data/`, and the health 503 path is tested. All five Done when items are met.
**Changed:** 8 code/config files plus 2 docs. Key files: `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `Program.cs`, `appsettings.json`, `.gitignore`, `StaticFilesTests.cs`, `HealthControllerTests.cs`.
**Deviations:**
- `StaticFilesTests` uses a temp web root with an `index.html` and includes a positive fallback test. *Improvement.* Without it, the 404 test would pass even with an unrestricted fallback.
- The `/api` fallback is restricted through a separate `MapFallback("/api/{**path}", NotFound)` rather than a pattern constraint. *Fine.*
- `.dockerignore` uses `**/` patterns and excludes `docker/`. *Fine.*
**Follow-ups:**
- The container runs as root, and the files on the mounts are root-owned. Decide on a user or PUID/PGID before processing writes to `/library` (phase 8; already listed as out of scope here).
- Client routes beyond `/` render empty. A not-found or layout shell is needed when the next screens arrive (phase 2).
