# Deploying AudiobookOrganizer

AudiobookOrganizer runs as one Linux container in Docker Desktop on a Windows machine, started with Docker Compose. The image holds the API, the built web client and ffmpeg/ffprobe. It's built from the repo and run from its own folder, `C:\docker\audiobook-organizer\`.

- `/data` (the SQLite database) is a Docker named volume.
- `/incoming` and `/library` are CIFS volumes on an SMB share (for example `\\cookserv\d`).

The app has no login. Run it only on a trusted LAN, and never expose port 8080 to the internet.

All commands are PowerShell.

## 1. Prerequisites

- Docker Desktop with the WSL2 backend. In Docker Desktop's settings, turn on **Start Docker Desktop when you sign in**, so the app comes back after a reboot.
- Git, to clone and update the repo.
- The SMB server reachable from Windows (for example `Test-NetConnection cookserv -Port 445`), and an SMB account that can read and write both share folders:
  - **incoming:** where new MP3/M4B audiobooks land.
  - **library:** where the app writes organized, tagged books.

  The library must not be inside incoming, and incoming must not be inside the library: Settings refuses that.
- Port 8080 free on the Windows machine.

## 2. Build the image

In the repo:

```powershell
docker compose build
```

This builds the image (client, API, then the runtime with ffmpeg) and tags it `audiobook-organizer:latest`, which the deploy folder uses.

The repo's `docker-compose.yml` is for trying the image locally with folders under `.\docker\`. Don't run `docker compose up` in the repo while the deployed app runs: both use the Compose project name `audiobook-organizer`, so it would replace the deployed container.

## 3. Set up `C:\docker\audiobook-organizer`

1. Create the folder and copy the two sample files from the repo's `docs\deploy\`:

   ```powershell
   New-Item -ItemType Directory -Force C:\docker\audiobook-organizer
   Copy-Item docs\deploy\compose.yaml C:\docker\audiobook-organizer\compose.yaml
   Copy-Item docs\deploy\.env.example C:\docker\audiobook-organizer\.env
   ```

2. Edit `C:\docker\audiobook-organizer\.env`:

   | Variable | Meaning |
   |---|---|
   | `SMB_SERVER` | The SMB server's name, or its IP address if the name doesn't resolve from Docker |
   | `SMB_USERNAME`, `SMB_PASSWORD` | The SMB account. A comma in the password breaks the mount options. |
   | `INCOMING_SHARE` | Share folder mounted at `/incoming`, for example `//cookserv/d/audiobooks_incoming` |
   | `LIBRARY_SHARE` | Share folder mounted at `/library`, for example `//cookserv/d/audiobooks` |

   Use the `//server/share/folder` form with forward slashes. A mapped drive letter like `Y:` can't be mounted. The credentials live only in this `.env`; never copy them into the repo.

3. Check the result. `docker compose config` shows the three volumes with targets `/data`, `/incoming` and `/library`:

   ```powershell
   Set-Location C:\docker\audiobook-organizer
   docker compose config
   ```

4. Start it:

   ```powershell
   docker compose up -d
   ```

   It restarts on its own after a crash or a reboot (`restart: unless-stopped`, once Docker Desktop is running) until you stop it with `docker compose down` or `docker compose stop`.

### Changing a share or the credentials

CIFS volume options are fixed when Docker creates the volume, so editing `.env` alone changes nothing. In `C:\docker\audiobook-organizer`:

```powershell
docker compose down
docker volume rm audiobook-organizer_incoming audiobook-organizer_library
docker compose up -d
```

Never remove `audiobook-organizer_audiobook-data`: it holds the database.

## 4. First-run settings

1. Open `http://localhost:8080` (or `http://<windows-pc>:8080` from another LAN device, see section 6).
2. Go to **Settings** (`/settings`).
3. Set **Incoming directory** to `/incoming` and **Library directory** to `/library`. These are the paths inside the container, not the share paths.
4. Leave **Directory template** and **File template** at their defaults or change them.
5. Choose **Keep source files after processing**. It's off by default: after a book is copied, tagged and verified, its source files are deleted.
6. Save. Settings checks that both folders exist and can be read, that they don't overlap, and that the library can be written to.

Then use **Scan incoming** on the dashboard to find books.

## 5. Check it's up

```powershell
Invoke-RestMethod http://localhost:8080/api/health
```

A healthy app returns `status: ok` and `database: ok`. If the database can't be reached it returns HTTP 503 with `{"status":"degraded","database":"error"}`.

`docker compose ps` in `C:\docker\audiobook-organizer` shows whether the container is running.

## 6. Reaching it from other LAN devices

Windows Firewall blocks port 8080 from other devices by default. To allow it, run once in an elevated (Run as administrator) PowerShell:

```powershell
New-NetFirewallRule -DisplayName "AudiobookOrganizer 8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow -Profile Private
```

Then open `http://<windows-pc>:8080` from the other device. Keep the network profile Private; don't open the port on public networks.

## 7. Logs

The app logs (Serilog) to the console and to daily files under `/data/logs/` in the `audiobook-data` volume, named `audiobook-organizer-YYYYMMDD.log`. A new file starts each day and only the newest 14 are kept.

Run these in `C:\docker\audiobook-organizer`:

```powershell
# Follow the console log
docker compose logs -f

# List the log files, then copy one out to read it
docker compose exec audiobook-organizer ls /data/logs
docker compose cp audiobook-organizer:/data/logs/audiobook-organizer-20261023.log .
```

Running locally, the same files go to `<repo>/data/logs/` (ignored by git).

## 8. Updating

```powershell
# in the repo
git pull
docker compose build

# then
Set-Location C:\docker\audiobook-organizer
docker compose up -d
```

`up -d` recreates the container from the new image. The database schema is created and upgraded when the app starts, so there's no migration step. The database volume and your settings are kept.

## 9. Backing up and restoring

Everything the app knows (settings, groups, metadata, covers, job history) is in `audiobooks.db` in the `audiobook-organizer_audiobook-data` volume.

Back up (here to `C:\backups\audiobook-organizer`), from `C:\docker\audiobook-organizer`:

```powershell
New-Item -ItemType Directory -Force C:\backups\audiobook-organizer
docker compose stop
docker run --rm -v audiobook-organizer_audiobook-data:/data -v C:\backups\audiobook-organizer:/backup alpine tar czf /backup/audiobook-data.tar.gz -C /data .
docker compose start
```

Stopping first means the database isn't being written while it's copied. `tar tzf` on the file (or opening it in 7-Zip) should list `./audiobooks.db`.

Restore (this replaces the current database):

```powershell
docker compose stop
docker run --rm -v audiobook-organizer_audiobook-data:/data -v C:\backups\audiobook-organizer:/backup alpine sh -c "rm -rf /data/* && tar xzf /backup/audiobook-data.tar.gz -C /data"
docker compose start
```

Back up the library folder on the server with your usual tools; the app only adds to it.

## 10. Running the backend suite in the image

The Dockerfile has a `test` stage that runs the backend tests on Linux with the same ffmpeg as the runtime image. In the repo:

```powershell
docker build --target test .
```

Docker caches a passing run. To force a rerun:

```powershell
docker build --target test --no-cache-filter test .
```

The normal `docker compose build` doesn't run this stage.

## File safety and permissions

- **Scans only read.** A scan never changes, tags or moves files in incoming.
- **Processing copies first.** Each file is copied to the library, tagged and read back. A source file is never tagged in place, and an existing library file is never overwritten.
- **Sources are deleted only with keep-source off,** and only after the whole book verified. The emptied source folders (and parents left empty) are removed too, never the incoming folder itself. So with keep-source off, the incoming share must be writable.
- **The library share must be writable.** Settings refuses a library it can't write to.
- **Share permissions come from the SMB account.** The volumes mount with `file_mode=0777,dir_mode=0777`, so inside the container every file looks writable; what the server actually allows is decided by the account in `.env`. Give it read and write on both folders (write on incoming is needed when keep-source is off). Files the app writes on the share belong to that account.
- **Restarts during processing are safe.** If the container stops mid-job, the next start fails the interrupted job and removes its partial copies. The sources are untouched; use Retry on the Jobs page to approve the book again. If the job had already verified every copy and started deleting sources, startup finishes deleting the remaining unchanged sources and marks the job succeeded.
- **Network speed.** Scans and copies go over the network, so a large incoming folder scans slower than a local disk.
