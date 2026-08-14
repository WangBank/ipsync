param(
    [string]$Configuration = "Release",
    [string]$Output = (Join-Path $PSScriptRoot "..\publish"),
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$project = Resolve-Path (Join-Path $PSScriptRoot "..\IpSync.csproj")
$publishArgs = @(
    "publish",
    $project.Path,
    "--configuration",
    $Configuration,
    "--runtime",
    "win-x64",
    "--output",
    $Output,
    "-p:PublishSingleFile=true"
)

if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
} else {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}

dotnet @publishArgs

Write-Host "Published to $Output"
