# Internal instructions for local publishing:

The package version comes from `<Version>` in `DotNetPip/DotNetPip.csproj`.

## Publish on macOS
```bash
export NUGET_API_KEY=<nuget-api-key>
./scripts/pack.sh
./scripts/push.sh
```

## Publish on Windows
```pwsh
$env:NUGET_API_KEY = "<nuget-api-key>"
./scripts/pack.ps1
./scripts/push.ps1
```

Packages are written to `artifacts/`

## GitHub Actions (pending)

Tag a release (for example `v1.0.0`) to pack and push from GitHub Actions (secret name: `NUGET_API_KEY`). The nupkg version is still the one in `DotNetPip.csproj`.
