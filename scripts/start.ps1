<#
.SYNOPSIS
  Start the API, the Vite client, or both.

.EXAMPLE
  .\scripts\start.ps1 api
  .\scripts\start.ps1 client
  .\scripts\start.ps1 both
  .\scripts\start.ps1          # same as both
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('api', 'client', 'both')]
    [string]$Target = 'both'
)

$ProjectName = 'AudiobookOrganizer'
$ApiPort = 5070
$ClientPort = 5173
$ApiDir = Join-Path $PSScriptRoot "..\src\$ProjectName.Api"
$ClientDir = Join-Path $PSScriptRoot "..\src\$ProjectName.Client"

function Clear-Port {
    param([int]$TargetPort)
    $connections = Get-NetTCPConnection -LocalPort $TargetPort -State Listen -ErrorAction SilentlyContinue
    if ($connections) {
        $pids = $connections | Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($procId in $pids) {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Host "Port $TargetPort in use by PID $procId ($($proc.ProcessName)). Killing..." -ForegroundColor Yellow
                Stop-Process -Id $procId -Force
                Start-Sleep -Milliseconds 500
            }
        }
        Write-Host "Port $TargetPort cleared." -ForegroundColor Green
    }
    else {
        Write-Host "Port $TargetPort is free." -ForegroundColor Green
    }
}

function Start-Api {
    Clear-Port -TargetPort $ApiPort
    Write-Host "Starting $ProjectName API on http://localhost:$ApiPort ..." -ForegroundColor Cyan
    Push-Location $ApiDir
    try {
        dotnet run
    }
    finally {
        Pop-Location
    }
}

function Start-Client {
    Clear-Port -TargetPort $ClientPort
    Write-Host "Starting $ProjectName Client on http://localhost:$ClientPort ..." -ForegroundColor Cyan
    Push-Location $ClientDir
    try {
        npm run dev
    }
    finally {
        Pop-Location
    }
}

function Start-InNewWindow {
    param([ValidateSet('api', 'client')][string]$ChildTarget)
    $shell = (Get-Process -Id $PID).Path
    Start-Process -FilePath $shell -ArgumentList @(
        '-NoExit',
        '-File', $PSCommandPath,
        $ChildTarget
    )
}

Clear-Host

switch ($Target) {
    'api' {
        Start-Api
    }
    'client' {
        Start-Client
    }
    'both' {
        Write-Host "Opening client in a new window; API stays here." -ForegroundColor Cyan
        Start-InNewWindow -ChildTarget 'client'
        Start-Api
    }
}
