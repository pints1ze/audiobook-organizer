# Runs the API on http://localhost:5070 (launch profile "http").
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project src/AudiobookOrganizer.Api --launch-profile http
