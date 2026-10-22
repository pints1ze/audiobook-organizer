# Slice 71: Windows Docker deploy

**Phase:** 9, Hardening & release
**Goal:** the developer can run the app in Docker Desktop on their Windows machine from `C:\docker\audiobook-organizer\compose.yaml`, using an image built from the repo, a named volume for `/data`, and incoming and library mounted from the `\\cookserv\d` SMB share.

## Done when
- [x] the repo's `docker-compose.yml` tags its build as `audiobook-organizer:latest` (`image:`), so `docker compose build` in the repo produces an image another compose file can use
- [x] `docs/deploy/compose.yaml` (a sample to copy to `C:\docker\audiobook-organizer\compose.yaml`) runs `audiobook-organizer:latest` with no `build:`, port 8080, `restart: unless-stopped`, a named volume at `/data`, and two CIFS named volumes at `/incoming` and `/library` whose share paths, server address and credentials come from `.env`
- [x] `docs/deploy/.env.example` lists those variables with example values (`//cookserv/d/...`), and `docker compose -f docs/deploy/compose.yaml --env-file docs/deploy/.env.example config` shows the three volumes mounted at `/data`, `/incoming` and `/library`
- [x] `docs/DEPLOY.md` is rewritten for Docker Desktop on Windows, with PowerShell commands throughout (sections below); nothing Linux-host-specific is left
- [x] AGENTS.md (Environment), `docs/PRD.md` (deployment line) and PLAN.md's phase 9 done-when say Docker Desktop (WSL2) on the developer's Windows machine, with incoming and library on an SMB share
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docker-compose.yml`, `Dockerfile` (runtime stage: `/data`, `/incoming`, `/library`, port 8080, `Database__Path=/data/audiobooks.db`)
- `docs/DEPLOY.md` and `.env.example` (slice 70): keep the parts that still hold (first-run settings, health, logs, the test stage, file safety), replace the Linux-host parts
- `AGENTS.md` "Environment" (the "Deploy target" line), `docs/PRD.md` (~52, "Runs in Docker (Linux container, Docker Compose)"), `docs/PLAN.md` phase 9 "Done when"
- `.gitignore` (`.env` is ignored; `docs/deploy/.env.example` must not be)
- `docs/CURRENT-STATE.md`, Decisions dated 2026-10-22

## Tasks
- [x] Add `image: audiobook-organizer:latest` to the service in `docker-compose.yml` (keep `build: .` and the `./docker/...` fallback mounts; the repo compose stays the local build-and-try file). Write `docs/deploy/compose.yaml`: service `audiobook-organizer`, `image: audiobook-organizer:latest`, `ports: "8080:8080"`, `restart: unless-stopped`, volumes `audiobook-data:/data`, `incoming:/incoming`, `library:/library`; the CIFS volumes use `driver: local` with `driver_opts` `type: cifs`, `device: ${INCOMING_SHARE}` / `${LIBRARY_SHARE}`, and `o: addr=${SMB_SERVER},username=${SMB_USERNAME},password=${SMB_PASSWORD},vers=3.0,file_mode=0777,dir_mode=0777`.
- [x] Write `docs/deploy/.env.example` with `SMB_SERVER=cookserv`, `SMB_USERNAME=`, `SMB_PASSWORD=`, `INCOMING_SHARE=//cookserv/d/audiobooks_incoming`, `LIBRARY_SHARE=//cookserv/d/audiobooks`, a comment on each (credentials stay in the untracked `.env` only; a password containing a comma breaks the `o` option). Confirm `git check-ignore docs/deploy/.env.example` reports nothing and run the `config` command from "Done when".
- [x] Rewrite `docs/DEPLOY.md` for Windows: prerequisites (Docker Desktop with the WSL2 backend, "Start Docker Desktop when you sign in", the server reachable from Windows); build the image in the repo (`docker compose build`); set up `C:\docker\audiobook-organizer\` (copy `compose.yaml` and `.env.example` → `.env`, fill in credentials, `docker compose up -d` from that folder); first-run settings (`/incoming`, `/library`, keep-source); check it's up (`Invoke-RestMethod http://localhost:8080/api/health`); a Windows Firewall inbound rule for TCP 8080 if other LAN devices use it (`New-NetFirewallRule`, run as admin); logs; updating (`git pull` and `docker compose build` in the repo, then `docker compose up -d` in `C:\docker\audiobook-organizer`); backup and restore of the named volume (stop, then a throwaway container that tars `/data` to a Windows folder; restore the reverse); the test stage. Notes: CIFS volume options are fixed when the volume is created, so after changing a share or credentials run `docker compose down` and `docker volume rm` the two share volumes (never the data volume); if `cookserv` doesn't resolve from Docker, use its IP in `SMB_SERVER`; a mapped drive letter like `Y:` can't be mounted. Keep the slice 70 file-safety section, replacing the ownership bullet: the share's permissions come from the SMB account, so it needs read and write on both folders (write on incoming when keep-source is off).
- [x] Update the wording in AGENTS.md (Environment: deploy target is Docker Desktop (WSL2) on the developer's Windows machine running the Linux image; `/data` is a named volume; `/incoming` and `/library` are CIFS volumes on an SMB share; keep "Code must handle both Windows (dev) and Linux (container) paths"), `docs/PRD.md` (~52) and PLAN.md's phase 9 done-when ("running on the developer's Windows machine in Docker Desktop with incoming and library on the SMB share"). Delete the root `.env.example` (its `DATA_DIR` bind mount is replaced by the named volume; the repo compose's `./docker/...` fallbacks need no `.env`) and drop its mention from DEPLOY.md. Run all four checks.
- Data: none (the database moves to a named volume on deploy; nothing in the app changes)
- Auth: none (the app still has no login; the doc keeps "trusted LAN only"). SMB credentials live only in the untracked `C:\docker\audiobook-organizer\.env`.
- Accessibility: none

## Tests to add
- None: config and docs only (AGENTS.md section 5). The `docker compose ... config` command checks the sample compose and `.env.example`.

## Manual checks
- Developer, on the Windows machine: `docker compose build` in the repo; set up `C:\docker\audiobook-organizer` from DEPLOY.md with real credentials; `docker compose up -d`; the app opens on `http://localhost:8080`, Settings accepts `/incoming` and `/library`, a scan finds the books, and one processed book lands in the library with its source removed (keep-source off). Consider pointing `LIBRARY_SHARE` at `//cookserv/d/audiobooks_test` for the first run.
- Developer: back up the data volume with the DEPLOY.md command and check the tar contains `audiobooks.db`.
- Developer: if another device on the LAN should reach it, add the firewall rule and open `http://<windows-pc>:8080` from that device.

## Out of scope
- Changing the Dockerfile, the container paths or the app's code.
- Docker secrets or encrypting the SMB password (it's in an untracked `.env` on the developer's machine, like the n8n setup); a proposal if the developer wants it.
- Log files (the AGENTS.md logs question in CURRENT-STATE.md stays open).
- Building or running the image here (AGENTS.md: only when the developer asks).

## Notes
- Developer decisions (2026-10-22): host on Windows in Docker Desktop with compose files under `C:\docker\<app>\`; incoming and library are on `\\cookserv\d` (mapped as `Y:`); `/data` is a named volume (SQLite locking over Windows bind mounts isn't reliable); the image is built from the repo and referenced by tag from `C:\docker`.
- Gate 4: this slice edits AGENTS.md and PLAN.md's phase 9 done-when. It needs the developer's OK before implementation.
- Assumption: incoming is `audiobooks_incoming` and the library is `audiobooks` on the share (example values in `.env.example` only; the developer sets the real ones).
- Assumption: the CIFS mount uses SMB 3.0 and the `0777` modes, so the container (root) can read, write and delete; the server's share permissions still apply.
- Risk: scans and copies go over the network; a large incoming folder scans slower than local disk. Nothing in the app depends on local-disk speed.
- Files touched: `docker-compose.yml`, `docs/deploy/compose.yaml`, `docs/deploy/.env.example`, `docs/DEPLOY.md`, `.env.example` (deleted), `AGENTS.md`, `docs/PRD.md`, PLAN.md, this slice file (9; just over the cap, all docs/config).
