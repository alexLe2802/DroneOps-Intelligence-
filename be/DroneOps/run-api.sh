#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd "$project_dir/../.." && pwd)"

if command -v dotnet >/dev/null 2>&1; then
  dotnet_command="$(command -v dotnet)"
elif [[ -x "$repo_dir/.tools/dotnet/dotnet" ]]; then
  dotnet_command="$repo_dir/.tools/dotnet/dotnet"
  export DOTNET_CLI_HOME="$repo_dir/.tools/dotnet-home"
  export NUGET_PACKAGES="$repo_dir/.tools/nuget"
else
  echo "Install the .NET 8 SDK to run DroneOps.API." >&2
  exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec "$dotnet_command" run --project "$project_dir/DroneOps.API/DroneOps.API.csproj" --launch-profile http "$@"
