param(
    [Parameter(Mandatory = $true)]
    [string]$RemoteUrl,

    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")

Push-Location $repo.Path
try {
    if (-not (Test-Path -LiteralPath ".git")) {
        git init
    }

    $existingRemote = git remote get-url origin 2>$null
    if ($LASTEXITCODE -eq 0 -and $existingRemote) {
        git remote set-url origin $RemoteUrl
    } else {
        git remote add origin $RemoteUrl
    }

    git branch -M $Branch
    git add .

    $status = git status --porcelain
    if ($status) {
        git commit -m "Initial IpSync project"
    } else {
        Write-Host "No local changes to commit."
    }

    git push -u origin $Branch
}
finally {
    Pop-Location
}
