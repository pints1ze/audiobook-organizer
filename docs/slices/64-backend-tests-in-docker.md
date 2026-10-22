# Slice 64: Backend tests in Docker

**Phase:** 9, Hardening & release
**Goal:** the backend suite runs and passes inside a Linux image with the same ffmpeg as the runtime image, through one `docker build --target test .` command.

## Done when
- [x] the Dockerfile has a `test` stage that builds the API and test project, installs ffmpeg from the same distro as the runtime stage, prints `ffmpeg -version`'s first line and runs `dotnet test src/AudiobookOrganizer.Api.Tests`
- [x] `docker build --target test .` from the repo root finishes with every backend test passing, and the printed ffmpeg is 6.x (the runtime image's version)
- [x] `docker compose build` still builds only the runtime image (the test stage isn't in its path) and the runtime stage is unchanged
- [x] any test that failed only on Linux is fixed in the test or the code, with the cause recorded in the Outcome
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `Dockerfile`, `.dockerignore`, `docker-compose.yml`
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizer.Api.Tests.csproj`: fixtures are linked from `..\..\docs\fixtures\...`, so the stage needs the same `src/` and `docs/fixtures/` layout
- `docs/CURRENT-STATE.md`, Decisions on ffmpeg (slice 04, slice 56 "ffmpeg 9 exits 0 when `-n` refuses", slices 61 and 63) and the Verification note "The tests run against dev ffmpeg 9.0; the image has 6.1"
- `src/AudiobookOrganizer.Api/Services/FfmpegRunner.cs`, `Services/TagWriter.cs`: the ffmpeg arguments most likely to behave differently on 6.1
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs:559`: an existing Windows-only test, the pattern for an OS-specific guard

## Tasks
- [x] Add a `test` stage to `Dockerfile` between stage 2 (api) and stage 3 (runtime): `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test` (the base image stage 2 already uses), `apt-get install -y --no-install-recommends ffmpeg` as in stage 3, copy `src/AudiobookOrganizer.Api/`, `src/AudiobookOrganizer.Api.Tests/` and `docs/fixtures/` to the same relative paths under `/src`, restore the test project, then `RUN ffmpeg -version | head -n 1 && dotnet test src/AudiobookOrganizer.Api.Tests --no-restore`. Put a comment above the stage naming the command (`docker build --target test .`). Leave stages 1–3 as they are.
- [x] Check that `sdk:10.0` and `aspnet:10.0` resolve to the same distro release, so the test stage's ffmpeg matches the runtime's: run `cat /etc/os-release` (or `ffmpeg -version`) in both images and record the result in the slice Notes or Test results.
- [x] Run `docker build --target test --progress=plain .` and read the output. For each failing test, find the root cause (path separators, case-sensitive file system, ffmpeg 6.1 arguments or output, temp folders, file locking) and fix it in the test or the code. A failure that needs a product decision (for example an ffmpeg 6.1 feature gap) is gate 3: stop and report it, don't skip the test.
- [x] Run `docker compose build` to prove the runtime image still builds without running the tests, then run the four local checks.
- Data: none
- Auth: none
- Accessibility: none

## Tests to add
- No new test classes: the whole existing backend suite (384 tests) is the test, run on Linux with ffmpeg 6.x. Its pass is recorded in Test results with the ffmpeg version line.
- For each Linux-only failure found: the failing run is the red step; the fix makes it green on both Linux (`docker build --target test .`) and Windows (`dotnet test`). If a test genuinely can't apply on one OS, guard it the way `JobsControllerTests.cs:559` does and say why in a comment.

## Manual checks
- Run `docker build --target test .` on the home server (or another Linux Docker host) once, to confirm the suite passes outside Docker Desktop's VM as well.

## Out of scope
- Running the frontend tests in Docker (they don't touch ffmpeg or the file system).
- Changing the runtime stage, the base images, the volume layout or docker-compose.yml.
- CI wiring; there's no CI in this repo.
- Docker documentation for the home server (a later phase 9 slice).

## Notes
- Conflict check: matches PLAN.md phase 9's done-when ("including the backend suite inside the Docker image (its ffmpeg 6.1 vs dev's 9.0)"). The test stage reuses the SDK base image stage 2 already pulls, so no base image changes; AGENTS.md's "Package: `docker compose build` (only when I ask)" is met because this slice is the request for that build.
- The `.dockerignore` already drops `bin/`, `obj/`, `data/`, `docker/` and `*.db`, so local build output and state don't leak into the stage.
- Risk: the size of the Linux-only fixes is unknown until the first run. If they go past the slice cap, fix what fits, list the rest under Follow-ups and stop at gate 3.
- Files touched: `Dockerfile`, this slice file, plus whatever tests or services the Linux run shows broken (expected 0–4).
- Remaining phase 9 work for later slices: end-to-end fixture scan → process as one test, an interrupted job's partial copies after a restart (startup fails the job, but its copies and `.partial` files stay and would show as `exists` on retry), repeat scans after processing (deleted sources, empty incoming folders), Docker docs for the home server, and the carried-in items in PLAN.md.
- Distro check (task 2): `mcr.microsoft.com/dotnet/sdk:10.0` and `aspnet:10.0` both report `Ubuntu 24.04.5 LTS` (noble), so the test stage's ffmpeg is the runtime's: `ffmpeg version 6.1.1-3ubuntu5`.
- First Linux run: 384 passed, 0 failed, so no Linux-only fixes were needed. `A_source_that_cant_be_deleted_is_kept_with_an_error_and_the_job_still_succeeds` is Windows-only (returns early on Linux) and counts as a pass there.
