# Slice 72: Log files

**Phase:** 9, Hardening & release
**Goal:** the app writes daily rolling log files under the data folder (`/data/logs/` in the container), so the developer can review them later.

## Done when
- [ ] Running locally writes `<repo>/data/logs/audiobook-organizer-YYYYMMDD.log`; the container writes `/data/logs/audiobook-organizer-YYYYMMDD.log`. Console logging is unchanged.
- [ ] Files roll daily and only the newest 14 are kept.
- [x] The test suite never writes log files into the repo: the test factory points the log path at a temp folder and deletes it on dispose.
- [x] `docs/DEPLOY.md` section 7 says where the files are and how to read them.
- [x] All four checks pass (build, backend tests, typecheck, frontend tests).

## Read first
- `AGENTS.md` (Stack: Serilog; Environment: "The SQLite database and logs live under `/data`")
- `src/AudiobookOrganizer.Api/Program.cs` (the `AddSerilog` call and how `Database:Path` is resolved against the content root)
- `src/AudiobookOrganizer.Api/appsettings.json`, `Dockerfile` (runtime `ENV`)
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizerWebApplicationFactory.cs`
- `docs/DEPLOY.md` section 7

## Tasks
- [x] Config: add `"Logs": { "Path": "../../data/logs/audiobook-organizer-.log" }` to `appsettings.json` (same base as `Database:Path`), and `Logs__Path=/data/logs/audiobook-organizer-.log` to the runtime `ENV` in `Dockerfile`. Verify: `dotnet build src/AudiobookOrganizer.Api`.
- [x] `Program.cs`: in the `AddSerilog` callback, resolve `Logs:Path` against the content root (`Path.GetFullPath(path, contentRoot)`, as the database does) and add `.WriteTo.File(fullPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)`. Keep `ReadFrom.Configuration` for level and the console sink. If `Logs:Path` is empty, skip the file sink. `Serilog.Sinks.File` already comes in through `Serilog.AspNetCore`, so don't add a package reference. Serilog creates the folder.
- [x] Test factory: set `Logs:Path` to a per-factory temp folder (`audiobook-organizer-tests-logs-{guid}/app-.log`), expose it, and delete the folder in `Dispose` after the host is gone.
- [x] Test: add `src/AudiobookOrganizer.Api.Tests/LoggingTests.cs` (see below). Watch it fail before the `Program.cs` change.
- [x] Docs: rewrite `docs/DEPLOY.md` section 7. Logs go to the console (`docker compose logs -f`) and to `/data/logs/` in the named volume, rolled daily and kept 14 days. To read them: `docker compose exec audiobook-organizer ls /data/logs`, then `docker compose cp audiobook-organizer:/data/logs/<file> .` (use the service name from `docs/deploy/compose.yaml`).
- Data: no schema change. A new `logs/` folder under the data folder; `/data/` and `logs/` are already in `.gitignore`.
- Auth: none.
- Accessibility: none (no UI).

## Tests to add
- `LoggingTests`: start the factory, call `GET /api/health`, dispose the host, then assert one `app-*.log` file exists in the factory's log folder and holds at least one line (for example, the request log or the startup "Now listening"/"Application started" line). Seen failing first.
- The same test class: a factory with `Logs:Path` set to `""` starts, answers `/api/health` and creates no log folder.

## Manual checks
- Run `scripts/start.ps1 api`, open `http://localhost:5070/api/health`, stop the API: `data/logs/audiobook-organizer-<today>.log` exists and `git status` stays clean.
- After `docker compose build`, run the deployed container: `docker compose exec <service> ls /data/logs` lists today's file.

## Out of scope
- Log viewing in the UI, log levels per environment, structured (JSON) file output.
- Changing what gets logged.

## Notes
- Conflict check: AGENTS.md already says logs live under `/data`, so this slice makes the code match it. No AGENTS.md change, no new dependency (the developer accepted file logs on 2026-10-23 after hearing it needs no new package; `Serilog.Sinks.File` is a transitive dependency of `Serilog.AspNetCore` 10.0.0).
- Serilog's file sink holds the file open without sharing. Read it in the test only after the host is disposed, or open it with `FileShare.ReadWrite`.
- The rolled file name puts the date before the extension: `audiobook-organizer-.log` becomes `audiobook-organizer-20261023.log`.
- Next phase 9 slices (developer, 2026-10-23: kept in phase 9): re-focusing the Jobs status line on a repeated announcement (59), focusing the Incoming load error when a failed save also fails the reload (26), and the cover picker's "Current cover" after a match change (37).
