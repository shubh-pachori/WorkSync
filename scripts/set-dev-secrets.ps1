# Windows equivalent of set-dev-secrets.sh.
#
#   ./scripts/set-dev-secrets.ps1
#
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function New-Secret {
    $bytes = New-Object 'System.Byte[]' 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    [Convert]::ToBase64String($bytes) -replace '[/+=]', 'x'
}

$pgUser = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'aitimesheet' }
$pgHost = if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { 'localhost' }
$pgPort = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { '5432' }
$pgPassword = $env:POSTGRES_PASSWORD

if (-not $pgPassword -and (Test-Path .env)) {
    $line = Select-String -Path .env -Pattern '^POSTGRES_PASSWORD=' | Select-Object -First 1
    if ($line) { $pgPassword = $line.Line.Split('=', 2)[1] }
}

if (-not $pgPassword) {
    Write-Error "POSTGRES_PASSWORD is not set and no .env file was found. Copy .env.example to .env first."
}

$jwtKey = if ($env:JWT_KEY) { $env:JWT_KEY } else { New-Secret }
$internalKey = if ($env:INTERNAL_API_KEY) { $env:INTERNAL_API_KEY } else { New-Secret }

$identity = 'backend/AITimesheet.IdentityService'
$timesheet = 'backend/AITimesheet.TimesheetService'

Write-Host 'Setting identity service secrets...'
dotnet user-secrets --project $identity set 'ConnectionStrings:DefaultConnection' "Host=$pgHost;Port=$pgPort;Database=ai_timesheet_identity_db;Username=$pgUser;Password=$pgPassword" | Out-Null
dotnet user-secrets --project $identity set 'Jwt:Key' $jwtKey | Out-Null
dotnet user-secrets --project $identity set 'Internal:ApiKey' $internalKey | Out-Null

Write-Host 'Setting timesheet service secrets...'
dotnet user-secrets --project $timesheet set 'ConnectionStrings:DefaultConnection' "Host=$pgHost;Port=$pgPort;Database=ai_timesheet_timesheet_db;Username=$pgUser;Password=$pgPassword" | Out-Null
dotnet user-secrets --project $timesheet set 'Jwt:Key' $jwtKey | Out-Null
dotnet user-secrets --project $timesheet set 'Internal:ApiKey' $internalKey | Out-Null

Write-Host ''
Write-Host 'Done. Secrets are stored under %APPDATA%\Microsoft\UserSecrets\.'
