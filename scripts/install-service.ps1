param(
    [string]$ServiceName = "IpSync",
    [string]$DisplayName = "IP Sync",
    [string]$PublishDirectory = (Join-Path $PSScriptRoot "..\publish"),
    [switch]$RunAsCurrentUser,
    [System.Management.Automation.PSCredential]$Credential
)

$ErrorActionPreference = "Stop"

$publishDirectory = Resolve-Path $PublishDirectory
$exePath = Join-Path $publishDirectory.Path "IpSync.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "IpSync.exe was not found in $($publishDirectory.Path). Run scripts\publish.ps1 first."
}

if ($RunAsCurrentUser -and -not $Credential) {
    $defaultUser = if ($env:USERDOMAIN) { "$env:USERDOMAIN\$env:USERNAME" } else { $env:USERNAME }
    $Credential = Get-Credential -UserName $defaultUser -Message "Enter the Windows password for the account that should run IpSync."
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

function New-IpSyncService {
    param(
        [string]$Name,
        [string]$Display,
        [string]$BinaryPath,
        [System.Management.Automation.PSCredential]$ServiceCredential
    )

    $serviceArgs = @{
        Name = $Name
        DisplayName = $Display
        BinaryPathName = "`"$BinaryPath`""
        StartupType = "Automatic"
    }

    if ($ServiceCredential) {
        $serviceArgs.Credential = $ServiceCredential
    }

    New-Service @serviceArgs
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($Credential) {
        Write-Host "Service $ServiceName already exists. Recreating it with the supplied account."
        if ($existing.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName -Force
        }

        & sc.exe delete $ServiceName | Out-Host

        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 1
            if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
                break
            }
        }

        New-IpSyncService -Name $ServiceName -Display $DisplayName -BinaryPath $exePath -ServiceCredential $Credential
        Set-IpSyncServiceResilience -Name $ServiceName -BinaryPath $exePath
        Start-Service -Name $ServiceName
        Write-Host "Reinstalled and started $ServiceName with delayed automatic startup and restart-on-failure recovery."
        exit 0
    }

    Write-Host "Service $ServiceName already exists. Updating settings and restarting it."
    Set-IpSyncServiceResilience -Name $ServiceName -BinaryPath $exePath
    Restart-Service -Name $ServiceName -Force
    exit 0
}

New-IpSyncService -Name $ServiceName -Display $DisplayName -BinaryPath $exePath -ServiceCredential $Credential
Set-IpSyncServiceResilience -Name $ServiceName -BinaryPath $exePath
Start-Service -Name $ServiceName
Write-Host "Installed and started $ServiceName with delayed automatic startup and restart-on-failure recovery."
