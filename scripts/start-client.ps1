# Runs the Vite dev server on http://localhost:5173 (proxies /api to the API on 5070).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..\src\AudiobookOrganizer.Client')
if (-not (Test-Path 'node_modules')) {
    npm install
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
npm run dev
