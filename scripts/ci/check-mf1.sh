#!/usr/bin/env bash
# One local command, no Docker, hosted database, SMTP, weather provider or FC.
set -euo pipefail
cd "$(dirname "$0")/../.."
if [[ -x .tools/dotnet/dotnet ]]; then
  dotnet_cmd="$PWD/.tools/dotnet/dotnet"
  export DOTNET_CLI_HOME="$PWD/.tools/dotnet-home"
  export NUGET_PACKAGES="$PWD/.tools/nuget"
else
  dotnet_cmd=dotnet
fi
"$dotnet_cmd" build be/DroneOps/DroneOps.AuthChecks -c Release -warnaserror --nologo
for suite in mission-workflow-integrity mission-schedule mission-history mission-geometry; do
  "$dotnet_cmd" run --project be/DroneOps/DroneOps.AuthChecks -c Release --no-build -- "--$suite"
done
(
  cd fe
  if [[ ! -d node_modules ]]; then
    echo 'Install frontend dependencies once with: cd fe && npm ci' >&2
    exit 1
  fi
  node --test tests/mission-review.test.mjs tests/planning-api.test.mjs tests/planning-validation.test.mjs
)
echo 'MF1 offline checks passed. Browser acceptance, deployment and hosted DB are not covered.'
