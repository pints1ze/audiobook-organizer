# Slice 73: Audiobookshelf server deploy

**Phase:** 9, Hardening & release
**Goal:** the deploy docs and sample compose describe the real host: the server that runs Audiobookshelf (Windows, Docker Desktop), with the image built there from `D:\workspace\audiobook-organizer` and `/incoming`, `/library` bind-mounted from its local `D:\audiobooks_incoming` and `D:\audiobooks`.

## Done when
- [x] `docs/deploy/compose.yaml` mounts `/incoming` and `/library` as bind mounts from `.env` paths, keeps `/data` on the named `audiobook-data` volume, and has no CIFS volumes or SMB variables.
- [x] `docs/deploy/.env.example` holds only `INCOMING_DIR=D:\audiobooks_incoming` and `LIBRARY_DIR=D:\audiobooks`, with comments; no credentials.
- [x] `docs/DEPLOY.md` describes the Audiobookshelf server: repo at `D:\workspace\audiobook-organizer`, compose folder `C:\docker\audiobook-organizer\`, bind mounts, no SMB steps; sections 1–10 and "File safety and permissions" no longer mention CIFS, SMB accounts or `//cookserv/d`.
- [x] AGENTS.md's Deploy target line, the PRD's deployment line, PLAN.md's "Finished when" item and phase 9 done-when name the Audiobookshelf server and local bind mounts (developer OK, 2026-10-23).
- [x] `docker compose -f docs/deploy/compose.yaml --env-file docs/deploy/.env.example config` succeeds and shows `/data` (volume), `/incoming` and `/library` (bind) targets. Build and all tests still pass.

## Read first
- `docs/DEPLOY.md` (all of it), `docs/deploy/compose.yaml`, `docs/deploy/.env.example`
- `AGENTS.md` Environment section (Deploy target line), `docs/PRD.md:52`, `docs/PLAN.md` line 9 and phase 9
- `docs/slices/71-windows-docker-deploy.md` (the CIFS setup this replaces)
- `docs/CURRENT-STATE.md` Decisions, 2026-10-23

## Tasks
- [x] `docs/deploy/compose.yaml`: replace the `incoming` and `library` CIFS volumes with `- ${INCOMING_DIR}:/incoming` and `- ${LIBRARY_DIR}:/library`; keep `name: audiobook-organizer`, the `audiobook-data` named volume (and its SQLite comment), the port and `restart: unless-stopped`. Update the header comment (copy to `C:\docker\audiobook-organizer\` on the Audiobookshelf server). Verify with the `config` command in Done when.
- [x] `docs/deploy/.env.example`: rewrite to the two `*_DIR` variables with Windows paths, and say that the folders are local disks on the server, the same library Audiobookshelf reads.
- [x] `docs/DEPLOY.md`: rewrite the intro, 1 (prerequisites: Docker Desktop, Git, the two folders on `D:`; drop the SMB reachability check and account), 2 (clone/pull at `D:\workspace\audiobook-organizer`, `docker compose build` there), 3 (copy files, set `.env` paths; Docker Desktop must be allowed to share drive `D:`, under Settings → Resources → File sharing if it asks), delete "Changing a share or the credentials" (replace with: edit `.env`, then `docker compose up -d` recreates the container), 6 (`<server>` instead of `<windows-pc>`), 8 (paths), 9 (keep), and "File safety and permissions" (bind mounts use the Windows folder permissions of the Docker Desktop user; drop `file_mode`/SMB-account bullets and the network-speed bullet; add that Audiobookshelf sees books in `D:\audiobooks` as soon as they're renamed into place, since copies are written as `.partial` first). Keep section 7 (logs) as slice 72 left it.
- [x] `AGENTS.md` Deploy target line, `docs/PRD.md:52`, `docs/PLAN.md` line 9 and the phase 9 done-when: replace "developer's Windows machine … CIFS volumes on an SMB share" with "the Audiobookshelf server (Windows, Docker Desktop) … `/incoming` and `/library` bind-mounted from its local disks". Keep the rest of each line.
- [x] Run the four checks (nothing in code changes, but confirm).
- Data: none.
- Auth: none.
- Accessibility: none (no UI).

## Tests to add
- None: docs and a sample compose file only (AGENTS.md section 5, config). The `docker compose ... config` command in Done when is the check.

## Manual checks
- Developer, on the Audiobookshelf server: follow the new DEPLOY.md from section 2. `docker compose config` in `C:\docker\audiobook-organizer` shows the bind mounts, the app starts, Settings accepts `/incoming` and `/library`, and a processed book appears in Audiobookshelf.

## Out of scope
- The repo's own `docker-compose.yml` (local try-out with `./docker/` folders) and the Dockerfile: unchanged.
- Any code change; any change to the `/data` named volume.

## Notes
- Conflict check: AGENTS.md says not to change the volume layout without asking; the developer chose local bind mounts on 2026-10-23, and that is the OK for this change to AGENTS.md and PLAN.md (gate 4 already cleared).
- `/incoming` and `/library` are on the same `D:` disk now, but the code still copies, never renames, across them, so nothing depends on that.
- Remaining phase 9 slices after this: re-focusing the Jobs status line on a repeated announcement (59), focusing the Incoming load error when a failed save also fails the reload (26), and the cover picker's "Current cover" after a match change (37).
