# IpSync Usage

IpSync is a .NET Worker Service that can run as a Windows Service. It captures local IPv4/IPv6 addresses, external IPv4/IPv6 addresses, writes `README.md`, commits the change, and pushes it to the configured Git remote.

## Configuration

Edit `appsettings.json`:

- `IpSync:RepositoryPath`: local Git repository path.
- `IpSync:ReadmeFileName`: README file to update.
- `IpSync:StateFileName`: local state file used to remember the last local IP snapshot; default is `.ipsync-state.json`.
- `IpSync:IntervalMinutes`: repeat interval; default is 10.
- `IpSync:Git:Enabled`: set to `false` to only write the README without Git commit/push.
- `IpSync:Git:RemoteName`: Git remote; default is `origin`.
- `IpSync:Git:Branch`: optional branch name. Leave empty to use the current branch/upstream.

## Connect To GitHub

If this folder is not already a cloned GitHub repository, create an empty GitHub repository first, then run:

```powershell
.\scripts\setup-git.ps1 -RemoteUrl https://github.com/<owner>/ipsync.git -Branch main
```

After the first push, the service can use the configured upstream for later README updates.

## Run Locally

```powershell
dotnet run
```

The development config disables Git publishing. `README.md` is updated only when local network IP addresses change.

To run one sync and exit:

```powershell
dotnet run -- --once
```

## Publish

```powershell
.\scripts\publish.ps1
```

The default output folder is `.\publish`.

## Install As Windows Service

Run PowerShell as Administrator:

```powershell
.\scripts\install-service.ps1
```

The service name is `IpSync`.

For GitHub push over HTTPS, install the service with the same Windows account that has your GitHub credential:

```powershell
.\scripts\install-service.ps1 -RunAsCurrentUser
```

The install script configures:

- Delayed automatic startup after Windows boots.
- Service recovery that restarts the service after failures.
- Immediate start after installation.

The Windows Service account must have permission to:

- Read and write `C:\Users\12243\Documents\GitHub\ipsync`.
- Run `git`.
- Use the GitHub credentials needed by `git push`.

## Uninstall

Run PowerShell as Administrator:

```powershell
.\scripts\uninstall-service.ps1
```
