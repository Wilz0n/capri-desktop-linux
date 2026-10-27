#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
START_SCRIPT="$SCRIPT_DIR/start-capri.sh"
SOURCE_ICON="$SCRIPT_DIR/assets/capri.webp"
APPLICATIONS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
DESKTOP_ENTRY="$APPLICATIONS_DIR/capri.desktop"
ICON_FILE="$ICON_DIR/capri.png"

for required_file in "$START_SCRIPT" "$SOURCE_ICON"; do
  if [[ ! -f "$required_file" ]]; then
    printf 'Error: no se encontró el archivo requerido: %s\n' "$required_file" >&2
    exit 1
  fi
done

if ! command -v magick >/dev/null 2>&1; then
  printf 'Error: ImageMagick (comando magick) es necesario para convertir el icono WebP a PNG.\n' >&2
  exit 127
fi

mkdir -p "$APPLICATIONS_DIR" "$ICON_DIR"
magick "$SOURCE_ICON" -resize 256x256 "$ICON_FILE"

cat > "$DESKTOP_ENTRY" <<EOF
[Desktop Entry]
Type=Application
Name=Capri
GenericName=Asistente de escritorio Capri
Comment=Inicia Capri Desktop
Exec=$START_SCRIPT
TryExec=$START_SCRIPT
Icon=$ICON_FILE
Terminal=false
Categories=Utility;Accessibility;
StartupNotify=true
EOF

chmod 0644 "$DESKTOP_ENTRY" "$ICON_FILE"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$DESKTOP_ENTRY"
fi

printf 'Capri quedó registrada en Omarchy:\n  Entrada: %s\n  Icono:   %s\n' "$DESKTOP_ENTRY" "$ICON_FILE"
printf 'Cierra y vuelve a abrir Super+I; busca Capri en Apps.\n'
