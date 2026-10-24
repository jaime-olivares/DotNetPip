#!/usr/bin/env pwsh
# Push artifacts/*.nupkg to nuget.org as jaime_olivares.
# Requires NUGET_API_KEY in the environment.
# Usage: ./scripts/push.ps1
param(
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$Source = $(if ($env:NUGET_SOURCE) { $env:NUGET_SOURCE } else { "https://api.nuget.org/v3/index.json" })
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$OutDir = Join-Path $Root "artifacts"

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "NUGET_API_KEY is not set."
}

$packages = @(Get-ChildItem -Path $OutDir -Filter "*.nupkg" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike "*.symbols.nupkg" })

if ($packages.Count -eq 0) {
    throw "No .nupkg files in ${OutDir}. Run ./scripts/pack.ps1 first."
}

foreach ($pkg in $packages) {
    Write-Host "Pushing $($pkg.FullName)"
    dotnet nuget push $pkg.FullName --api-key $ApiKey --source $Source --skip-duplicate
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
