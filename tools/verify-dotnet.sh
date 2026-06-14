#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$repo_root/.dotnet}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$repo_root/.nuget/packages}"
export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-true}"
export DOTNET_NOLOGO="${DOTNET_NOLOGO:-true}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE="${DOTNET_SKIP_FIRST_TIME_EXPERIENCE:-true}"

mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"

dotnet restore BallGM.slnx
dotnet format BallGM.slnx --verify-no-changes --no-restore
dotnet build BallGM.slnx --configuration Release --no-restore -p:UseSharedCompilation=false
dotnet test BallGM.slnx --configuration Release --no-build
