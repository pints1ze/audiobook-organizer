# Slice 01: API scaffold and health check

**Phase:** 1, Foundation
**Goal:** a .NET 10 API with its xUnit test project, a SQLite database file it creates on startup, and `GET /api/health` returning 200, runnable through `scripts/start-api.ps1`.

## Done when
- [x] `dotnet build src/AudiobookOrganizer.Api` succeeds with no warnings
- [x] `dotnet test src/AudiobookOrganizer.Api.Tests` passes (at least the tests below)
- [x] `GET /api/health` returns 200 with `{"status":"ok","database":"ok"}`
- [x] On startup the API creates the SQLite file at the configured `Database:Path` (and its parent folder) if missing
- [x] `scripts/start-api.ps1` runs the API on http://localhost:5070

## Read first
- `AGENTS.md`: Stack, Layout, Commands, Environment (the API runs on Windows in dev and Linux in Docker, so paths must work on both)
- `docs/PLAN.md` phase 1
- `docs/CURRENT-STATE.md` Decisions (SQLite + Dapper, no auth)

## Tasks
- [x] **Solution and API project.** Create `AudiobookOrganizer.slnx` at the repo root with `src/AudiobookOrganizer.Api` (`dotnet new webapi --use-controllers`, net10.0, nullable on, `TreatWarningsAsErrors` on). Remove the template's weather sample. Add packages Dapper, Microsoft.Data.Sqlite and Serilog.AspNetCore. Configure Serilog to write to the console from `appsettings.json`. Set `launchSettings.json` http profile to port 5070. Add a root `.gitignore` for .NET and Node (`bin/`, `obj/`, `*.db`, `logs/`, `data/`, `node_modules/`, `dist/`, `publish/`, `src/AudiobookOrganizer.Api/wwwroot/`, `.env`, `secrets.local.*`). Verify: `dotnet build` is clean.
- [x] **Database settings and initializer.** Add `Configuration/DatabaseSettings.cs` (`Path`, bound from section `Database`). In `appsettings.json` the default is `data/audiobooks.db`, resolved relative to the content root when it isn't absolute. Add `Data/DatabaseInitializer.cs`: it creates the parent directory if needed, then opens a `SqliteConnection` (which creates the file). Startup runs it once. Register a factory or service that hands out open connections for Dapper. There's no schema yet. Verify: run the API and confirm the file appears.
- [x] **Health endpoint.** Add `Controllers/HealthController.cs` with `GET /api/health`. It runs `SELECT 1` through Dapper and returns 200 `{status:"ok",database:"ok"}`. If the query throws, it returns 503 `{status:"degraded",database:"error"}` and logs the exception. Verify: browse to `/api/health`.
- [x] **Test project.** Create `src/AudiobookOrganizer.Api.Tests` (xUnit, net10.0, Microsoft.AspNetCore.Mvc.Testing) and add it to the solution. Add `AudiobookOrganizerWebApplicationFactory` that points `Database:Path` at a unique temp file and deletes it on dispose. Write the tests below. Verify: `dotnet test` is green.
- [x] **Run script.** Add `scripts/start-api.ps1`, which runs `dotnet run --project src/AudiobookOrganizer.Api --launch-profile http`. Verify: run it and hit the health endpoint.
- Data: no tables yet. The initializer only creates the database file. Later slices add schema in `DatabaseInitializer`.
- Auth: none. There's no authentication by decision (trusted LAN).
- Accessibility: none, since this slice has no UI.

## Tests to add
- `Controllers/HealthControllerTests.cs`: `GET /api/health` returns 200 and the body has `status: "ok"` and `database: "ok"`.
- `Controllers/HealthControllerTests.cs`: the database file exists at the factory's temp path after the app starts.
- `Data/DatabaseInitializerTests.cs`: a nested, non-existent directory in the configured path is created along with the file.

## Manual checks
- Run `scripts/start-api.ps1`, open http://localhost:5070/api/health and see the JSON. Confirm `data/audiobooks.db` exists under the API content root and is ignored by git (`git status` doesn't show it).

## Out of scope
- React client and `scripts/start-client.ps1`: slice 02.
- Dockerfile, `docker-compose.yml`, serving the client from `wwwroot`, the `/data` volume: slice 03.
- Any tables, settings storage or TagLibSharp: phase 2.

## Notes
- **Conflict check:** consistent with AGENTS.md (stack, layout, port 5070), PLAN phase 1 and the decisions. The frontend typecheck and test commands from "four checks" don't exist until slice 02. For this slice, done means build plus backend tests.
- **Size:** 5 tasks. About 12 files, which is over the ~8 cap only because of scaffold boilerplate (`.slnx`, two `.csproj`, `launchSettings.json`, `appsettings*.json`). There are 5 hand-written code files.
- **Split from phase 1:** 02 client scaffold (Vite, React 19, Tailwind 4, Vitest, `/api` proxy, a page showing health status, `start-client.ps1`). 03 Docker (multi-stage Dockerfile building the client into `wwwroot`, Compose with `/data`, `/incoming` and `/library` volumes, port 8080, `Database:Path=/data/audiobooks.db`).
- **Commit:** `chore: slice 01 api-scaffold-health`. AGENTS.md section 8 reserves `chore:` for the Foundation scaffold.
