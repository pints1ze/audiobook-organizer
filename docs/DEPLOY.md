# Deploying AudiobookOrganizer

AudiobookOrganizer runs as one Docker container on a Linux home server, started with Docker Compose. The image holds the API, the built web client and ffmpeg/ffprobe.

The app has no login. Run it only on a trusted LAN, and never expose port 8080 to the internet.

## 1. Prerequisites

- A Linux server with Docker Engine and the Docker Compose plugin (`docker compose`).
- Git, to clone and update the repo.
- Three folders on the server:
  - **data:** holds the SQLite database (`audiobooks.db`).
  - **incoming:** where new MP3/M4B audiobooks land.
  - **library:** where the app writes organized, tagged books.

  Incoming and library may be on different disks. The library must not be inside incoming, and incoming must not be inside the library: Settings refuses that.
- Port 8080 free on the server.

## 2. Set the host folders in `.env`

1. Clone the repo and change into it.
2. Copy the example file: `cp .env.example .env`
3. Edit `.env` and set the three paths to folders on the server:

   | Variable | Mounted in the container at |
   |---|---|
   | `DATA_DIR` | `/data` |
   | `INCOMING_DIR` | `/incoming` |
   | `LIBRARY_DIR` | `/library` |

4. Create the folders if they don't exist yet (for example `mkdir -p /srv/audiobooks/data /srv/audiobooks/incoming /srv/audiobooks/library`). If you leave a folder out, Docker creates it owned by root.
5. Check the result: `docker compose config` shows each folder under `volumes` with its `target`.

If `.env` is missing or a variable is unset, Compose falls back to `./docker/data`, `./docker/incoming` and `./docker/library` inside the repo.

## 3. Build and start

```
docker compose up -d --build
```

This builds the image (client, API, then the runtime with ffmpeg) and starts the `audiobook-organizer` service in the background. It restarts on its own after a crash or a reboot (`restart: unless-stopped`) until you stop it with `docker compose down` or `docker compose stop`.

## 4. First-run settings

1. Open `http://<server>:8080` in a browser on the LAN.
2. Go to **Settings** (`/settings`).
3. Set **Incoming directory** to `/incoming` and **Library directory** to `/library`. These are the paths inside the container, not the host paths from `.env`.
4. Leave **Directory template** and **File template** at their defaults or change them.
5. Choose **Keep source files after processing**. It's off by default: after a book is copied, tagged and verified, its source files are deleted.
6. Save. Settings checks that both folders exist and can be read, that they don't overlap, and that the library can be written to.

Then use **Scan incoming** on the dashboard to find books.

## 5. Check it's up

```
curl http://<server>:8080/api/health
```

A healthy app returns `{"status":"ok","database":"ok"}`. If the database can't be reached it returns HTTP 503 with `{"status":"degraded","database":"error"}`.

`docker compose ps` shows whether the container is running.

## 6. Logs

The app logs to the console only (Serilog), so read them through Docker:

```
docker compose logs -f
```

There are no log files under `/data`.

## 7. Updating

```
git pull
docker compose up -d --build
```

The database schema is created and upgraded when the app starts, so there's no migration step. The database in `DATA_DIR` and your settings are kept.

## 8. Backing up

Everything the app knows (settings, groups, metadata, covers, job history) is in `audiobooks.db` in `DATA_DIR`.

1. Stop the container so the database isn't being written: `docker compose stop`
2. Copy `audiobooks.db` from `DATA_DIR` to your backup location.
3. Start it again: `docker compose start`

Back up the library folder with your usual tools; the app only adds to it.

## 9. Running the backend suite in the image

The Dockerfile has a `test` stage that runs the backend tests on Linux with the same ffmpeg as the runtime image:

```
docker build --target test .
```

Docker caches a passing run. To force a rerun:

```
docker build --target test --no-cache-filter test .
```

The normal `docker compose up --build` doesn't run this stage.

## File safety and permissions

- **Scans only read.** A scan never changes, tags or moves files in incoming.
- **Processing copies first.** Each file is copied to the library, tagged and read back. A source file is never tagged in place, and an existing library file is never overwritten.
- **Sources are deleted only with keep-source off,** and only after the whole book verified. The emptied source folders (and parents left empty) are removed too, never the incoming folder itself. So with keep-source off, the incoming mount must be writable by the container.
- **The library mount must be writable.** Settings refuses a library it can't write to.
- **File ownership:** files the app writes belong to the user the container runs as. The Dockerfile doesn't set a user, so check it once the container runs: `docker compose exec audiobook-organizer id`. Make sure that user can write the library and incoming folders on the host, and that the owner it gives library files suits you.
- **Restarts during processing are safe.** If the container stops mid-job, the next start fails the interrupted job and removes its partial copies. The sources are untouched; use Retry on the Jobs page to approve the book again. If the job had already verified every copy and started deleting sources, startup finishes deleting the remaining unchanged sources and marks the job succeeded.
