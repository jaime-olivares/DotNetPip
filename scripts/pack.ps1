#!/usr/bin/env pwsh
# Build Release binaries and generate DotNetPip.*.nupkg under artifacts/.
# Package version is read from DotNetPip/DotNetPip.csproj (<Version>).
# Usage: ./scripts/pack.ps1 [-Configuration Release]
param(
    [string]$Configuration = $(if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" })
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$OutDir = Join-Path $Root "artifacts"

Set-Location $Root

dotnet pack (Join-Path $Root "DotNetPip/DotNetPip.csproj") `
    --configuration $Configuration `
    --output $OutDir `
    "-p:PackageOutputPath=$OutDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "NuGet packages written to ${OutDir}:"
Get-ChildItem -Path $OutDir -Filter "*.nupkg" | ForEach-Object { Write-Host $_.FullName }
