#!/usr/bin/env bash
# Build Release binaries and generate DotNetPip.*.nupkg under artifacts/.
# Package version is read from DotNetPip/DotNetPip.csproj (<Version>).
# Usage: ./scripts/pack.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIG="${CONFIGURATION:-Release}"
OUTDIR="${ROOT}/artifacts"

cd "$ROOT"

dotnet pack "${ROOT}/DotNetPip/DotNetPip.csproj" \
  --configuration "${CONFIG}" \
  --output "${OUTDIR}" \
  -p:PackageOutputPath="${OUTDIR}"

echo
echo "NuGet packages written to ${OUTDIR}:"
ls -1 "${OUTDIR}"/*.nupkg
