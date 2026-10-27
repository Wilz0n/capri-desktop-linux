#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/cross/Capri.Cross.csproj"
PUBLISHED_APP="$SCRIPT_DIR/out/Capri/Capri"

if [[ -x "$PUBLISHED_APP" ]]; then
  exec "$PUBLISHED_APP" "$@"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  printf 'Error: no se encontró dotnet. Instala el SDK .NET 10 o publica Capri en out/Capri.\n' >&2
  exit 127
fi

exec dotnet run --project "$PROJECT" -- "$@"
