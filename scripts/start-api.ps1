$Port = 5070
$ProjectName = 'AudiobookOrganizer'
$ProjectDir = Join-Path $PSScriptRoot "..\src\$ProjectName.Api"

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

Clear-Host
Clear-Port -TargetPort $Port
Write-Host "Starting $ProjectName API on http://localhost:$Port ..." -ForegroundColor Cyan
Push-Location $ProjectDir
try {
    dotnet run
}
finally {
    Pop-Location
}
