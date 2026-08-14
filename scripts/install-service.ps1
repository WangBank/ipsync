param(
    [string]$ServiceName = "IpSync",
    [string]$DisplayName = "IP Sync",
    [string]$PublishDirectory = (Join-Path $PSScriptRoot "..\publish"),
    [switch]$RunAsCurrentUser,
    [System.Management.Automation.PSCredential]$Credential
)

$ErrorActionPreference = "Stop"

$publishDirectory = Convert-Path -LiteralPath $PublishDirectory
$exePath = Join-Path -Path $publishDirectory -ChildPath "IpSync.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "IpSync.exe was not found in $publishDirectory. Run scripts\publish.ps1 first."
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

function Grant-LogonAsServiceRight {
    param(
        [string]$AccountName
    )

    $normalizedAccountName = if ($AccountName.StartsWith(".\", [StringComparison]::Ordinal)) {
        "$env:COMPUTERNAME\$($AccountName.Substring(2))"
    } else {
        $AccountName
    }

    if (-not ([System.Management.Automation.PSTypeName]"IpSync.ServiceLogonRight").Type) {
        Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace IpSync
{
    public static class ServiceLogonRight
    {
        private const uint POLICY_CREATE_ACCOUNT = 0x00000010;
        private const uint POLICY_LOOKUP_NAMES = 0x00000800;

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_OBJECT_ATTRIBUTES
        {
            public uint Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LSA_UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport("advapi32.dll", PreserveSig = true)]
        private static extern uint LsaOpenPolicy(
            IntPtr systemName,
            ref LSA_OBJECT_ATTRIBUTES objectAttributes,
            uint desiredAccess,
            out IntPtr policyHandle);

        [DllImport("advapi32.dll", PreserveSig = true)]
        private static extern uint LsaAddAccountRights(
            IntPtr policyHandle,
            byte[] accountSid,
            LSA_UNICODE_STRING[] userRights,
            uint countOfRights);

        [DllImport("advapi32.dll")]
        private static extern uint LsaClose(IntPtr policyHandle);

        [DllImport("advapi32.dll")]
        private static extern uint LsaNtStatusToWinError(uint status);

        public static void Grant(string accountName)
        {
            var sid = (SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
            var sidBytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(sidBytes, 0);

            var attributes = new LSA_OBJECT_ATTRIBUTES();
            IntPtr policyHandle;
            var status = LsaOpenPolicy(
                IntPtr.Zero,
                ref attributes,
                POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES,
                out policyHandle);

            ThrowIfFailed(status);

            try
            {
                var rights = new[] { CreateLsaString("SeServiceLogonRight") };
                status = LsaAddAccountRights(policyHandle, sidBytes, rights, 1);
                ThrowIfFailed(status);
            }
            finally
            {
                LsaClose(policyHandle);
            }
        }

        private static LSA_UNICODE_STRING CreateLsaString(string value)
        {
            return new LSA_UNICODE_STRING
            {
                Length = (ushort)(value.Length * 2),
                MaximumLength = (ushort)((value.Length + 1) * 2),
                Buffer = Marshal.StringToHGlobalUni(value)
            };
        }

        private static void ThrowIfFailed(uint status)
        {
            if (status == 0)
            {
                return;
            }

            throw new Win32Exception((int)LsaNtStatusToWinError(status));
        }
    }
}
"@
    }

    Write-Host "Granting 'Log on as a service' to $normalizedAccountName."
    [IpSync.ServiceLogonRight]::Grant($normalizedAccountName)
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

if ($Credential) {
    Grant-LogonAsServiceRight -AccountName $Credential.UserName
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
