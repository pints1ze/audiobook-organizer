# Regenerates the audio test fixtures in docs/fixtures/audio/ (chapters.m4b, chapters.mp3).
# Dev tool only: needs ffmpeg and ffprobe on PATH. The app and the tests never call ffmpeg;
# the tests read the committed fixture files.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$out = 'docs/fixtures/audio'
New-Item -ItemType Directory -Force $out | Out-Null

# 3 chapters: Opening 0-10s, Middle 10-20s, Ending 20-30s (TIMEBASE in milliseconds).
$meta = Join-Path ([IO.Path]::GetTempPath()) 'audiobook-organizer-chapters.ffmeta'
@'
;FFMETADATA1
title=Fixture Book
artist=Fixture Author

[CHAPTER]
TIMEBASE=1/1000
START=0
END=10000
title=Opening

[CHAPTER]
TIMEBASE=1/1000
START=10000
END=20000
title=Middle

[CHAPTER]
TIMEBASE=1/1000
START=20000
END=30000
title=Ending
'@ | Set-Content -Encoding ascii $meta

# 30 seconds of mono silence.
$src = @('-f', 'lavfi', '-t', '30', '-i', 'anullsrc=channel_layout=mono:sample_rate=22050')

ffmpeg -y -loglevel error @src -i $meta -map 0:a -map_metadata 1 -map_chapters 1 `
    -c:a aac -b:a 32k -f mp4 "$out/chapters.m4b"
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed for chapters.m4b' }

ffmpeg -y -loglevel error @src -i $meta -map 0:a -map_metadata 1 -map_chapters 1 `
    -c:a libmp3lame -b:a 32k -id3v2_version 3 -write_id3v1 0 "$out/chapters.mp3"
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed for chapters.mp3' }

Remove-Item $meta

foreach ($f in 'chapters.m4b', 'chapters.mp3') {
    Write-Host "== $f ($((Get-Item "$out/$f").Length) bytes)"
    ffprobe -v error -show_chapters -of compact "$out/$f"
}
