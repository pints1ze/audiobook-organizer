# Slice 70: Docker deploy docs

**Phase:** 9, Hardening & release
**Goal:** a developer can stand the app up on the home server from one document, `docs/DEPLOY.md`, plus a `.env.example` for the three host folders.

## Done when
- [x] `docs/DEPLOY.md` covers, in order: prerequisites; setting the host folders in `.env` (from `.env.example`); build and start; first-run settings in the app; checking it's up; logs; updating; backing up; running the backend suite in the image
- [x] `.env.example` sets `DATA_DIR`, `INCOMING_DIR` and `LIBRARY_DIR` to example host paths with a one-line comment each, and `docker compose --env-file .env.example config` shows them mounted at `/data`, `/incoming` and `/library`
- [x] every command, port, path, route and setting name in `DEPLOY.md` is checked against the repo (`docker-compose.yml`, `Dockerfile`, `appsettings.json`, `HealthController`, `SettingsService`), not written from memory
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docker-compose.yml` (port 8080, the three `${..._DIR:-./docker/...}` mounts, `restart: unless-stopped`) and `Dockerfile` (stages; the `test` target; `Database__Path=/data/audiobooks.db`; ffmpeg in the runtime image)
- `src/AudiobookOrganizer.Api/appsettings.json` (Serilog writes to the console only) and `Controllers/HealthController.cs` (`GET /api/health`, 503 when the database fails)
- `src/AudiobookOrganizer.Api/Services/SettingsService.cs` (~39–100: incoming and library must exist, mustn't overlap, library must be writable; templates required)
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` (schema created and upgraded at startup, so an update needs no migration step)
- `docs/PRD.md` (deployment and file-safety lines) and `AGENTS.md` Environment section
- `.gitignore` (`.env` is ignored; `.env.example` must not be)

## Tasks
- [x] Add `.env.example` at the repo root with `DATA_DIR`, `INCOMING_DIR`, `LIBRARY_DIR` (Linux example paths such as `/srv/audiobooks/data`, each with a comment), and confirm `git check-ignore .env.example` reports nothing. Verify with `docker compose --env-file .env.example config`.
- [x] Write `docs/DEPLOY.md` with the sections in "Done when". Include: copy `.env.example` to `.env` and edit it; incoming and library may be on different disks; `docker compose up -d --build`; open `http://<server>:8080`, then on Settings set incoming `/incoming` and library `/library` (container paths, not host paths) and the keep-source toggle; `GET /api/health` returns `{"status":"ok",...}`; `docker compose logs -f`; updating is `git pull` then `docker compose up -d --build` (the schema upgrades at startup); back up by stopping the container and copying `audiobooks.db` from `DATA_DIR`; `docker build --target test .` (with `--no-cache-filter test` to force a rerun).
- [x] Add a "File safety and permissions" section: scans only read; with keep-source off, processing deletes sources and their emptied folders, so the incoming mount must be writable; the library mount must be writable; the container's user owns the files it writes (find which user with `docker compose exec audiobook-organizer id` once it runs, and say so rather than guess); a restart during processing is handled at startup (slices 65, 69).
- [x] Check every command and name in the doc against the files listed in "Read first", then run all four checks.
- Data: none
- Auth: none (the doc states the app has no login and belongs on a trusted LAN only)
- Accessibility: none

## Tests to add
- None: docs and config only (AGENTS.md section 5, trivial config). `docker compose --env-file .env.example config` is the check for `.env.example`.

## Manual checks
- On the home server (developer): follow `DEPLOY.md` from a clean checkout; the app opens on port 8080, Settings accepts `/incoming` and `/library`, a scan finds the books, and one processed book lands in the library with its source removed (keep-source off). This is PLAN.md phase 9's last done-when line.
- Note the container user `id` reports and whether library files are owned as expected; update the doc's permissions line if it differs.

## Out of scope
- Changing the Dockerfile (running as a non-root user, `PUID`/`PGID`, a `HEALTHCHECK`) or the volume layout: proposals for the developer, not part of this slice.
- Log files under `/data/logs` (AGENTS.md mentions them; logging is console only and adding a file sink is a new package).
- Replacing the root `README.md` (it describes the slice workflow); `DEPLOY.md` stands alone.
- Building or running the image here (AGENTS.md: `docker compose build` only when the developer asks).

## Notes
- Conflict check: matches AGENTS.md's deploy target (Linux container, Docker Compose, `/incoming`, `/library`, `/data`, port 8080, no auth) and PLAN.md phase 9's "Docker image documented". It doesn't change the Dockerfile or the volume layout.
- Mismatch to raise at review (not fixed here): AGENTS.md says logs live under `/data`, but Serilog writes only to the console.
- Files touched: `docs/DEPLOY.md`, `.env.example`, this slice file, PLAN.md (4).
- After this slice, phase 9's remaining done-when is the developer's home-server run (gate 5), plus the carried-in polish items in PLAN.md if the developer wants them in this phase.
