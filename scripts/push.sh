#!/usr/bin/env bash
# Push artifacts/*.nupkg to nuget.org as jaime_olivares.
# Requires NUGET_API_KEY in the environment.
# Usage: ./scripts/push.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTDIR="${ROOT}/artifacts"
SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "NUGET_API_KEY is not set." >&2
  exit 1
fi

shopt -s nullglob
packages=("${OUTDIR}"/*.nupkg)
if [[ ${#packages[@]} -eq 0 ]]; then
  echo "No .nupkg files in ${OUTDIR}. Run ./scripts/pack.sh first." >&2
  exit 1
fi

for pkg in "${packages[@]}"; do
  case "${pkg}" in
    *.symbols.nupkg) continue ;;
  esac
  echo "Pushing ${pkg}"
  dotnet nuget push "${pkg}" --api-key "${NUGET_API_KEY}" --source "${SOURCE}" --skip-duplicate
done
