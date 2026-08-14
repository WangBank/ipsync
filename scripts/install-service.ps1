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

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service $ServiceName already exists. Restarting it."
    Restart-Service -Name $ServiceName -Force
    exit 0
}

New-Service `
    -Name $ServiceName `
    -DisplayName $DisplayName `
    -BinaryPathName "`"$exePath`"" `
    -StartupType Automatic

Start-Service -Name $ServiceName
Write-Host "Installed and started $ServiceName."
