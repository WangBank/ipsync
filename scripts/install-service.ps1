param(
    [string]$ServiceName = "IpSync",
    [string]$DisplayName = "IP Sync",
    [string]$PublishDirectory = (Join-Path $PSScriptRoot "..\publish")
)

$ErrorActionPreference = "Stop"

$publishDirectory = Resolve-Path $PublishDirectory
$exePath = Join-Path $publishDirectory.Path "IpSync.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "IpSync.exe was not found in $($publishDirectory.Path). Run scripts\publish.ps1 first."
}

function Set-IpSyncServiceResilience {
    param(
        [string]$Name,
        [string]$BinaryPath
    )

    & sc.exe config $Name binPath= "`"$BinaryPath`"" start= delayed-auto | Out-Host
    & sc.exe failure $Name reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Host
    & sc.exe failureflag $Name 1 | Out-Host
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service $ServiceName already exists. Updating settings and restarting it."
    Set-IpSyncServiceResilience -Name $ServiceName -BinaryPath $exePath
    Restart-Service -Name $ServiceName -Force
    exit 0
}

New-Service `
    -Name $ServiceName `
    -DisplayName $DisplayName `
    -BinaryPathName "`"$exePath`"" `
    -StartupType Automatic

Set-IpSyncServiceResilience -Name $ServiceName -BinaryPath $exePath
Start-Service -Name $ServiceName
Write-Host "Installed and started $ServiceName with delayed automatic startup and restart-on-failure recovery."
