$Port = 5173
$ProjectName = 'AudiobookOrganizer'
$ProjectDir = Join-Path $PSScriptRoot "..\src\$ProjectName.Client"

function Clear-Port {
    param([int]$TargetPort)
    $connections = Get-NetTCPConnection -LocalPort $TargetPort -State Listen -ErrorAction SilentlyContinue
    if ($connections) {
        $pids = $connections | Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($pid in $pids) {
            $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Host "Port $TargetPort in use by PID $pid ($($proc.ProcessName)). Killing..." -ForegroundColor Yellow
                Stop-Process -Id $pid -Force
                Start-Sleep -Milliseconds 500
            }
        }
        Write-Host "Port $TargetPort cleared." -ForegroundColor Green
    }
    else {
        Write-Host "Port $TargetPort is free." -ForegroundColor Green
    }
}

Clear-Host
Clear-Port -TargetPort $Port
Write-Host "Starting $ProjectName Client on http://localhost:$Port ..." -ForegroundColor Cyan
Push-Location $ProjectDir
try {
    npm run dev
}
finally {
    Pop-Location
}
