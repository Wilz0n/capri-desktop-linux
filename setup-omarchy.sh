#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
HYPR_DIR="${HYPR_DIR:-${XDG_CONFIG_HOME:-$HOME/.config}/hypr}"
DRY_RUN=0
SKIP_VOXTYPE=0
SKIP_BUILD=0
APPLY_HYPRLAND=0

log() { printf '\n\033[1;36m[Capri]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[Capri: aviso]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[Capri: error]\033[0m %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<'EOF'
Uso: ./setup-omarchy.sh [opciones]

Prepara Capri en Omarchy: dependencias, Voxtype, publicación Linux, comando
de rescate y entrada de Capri en el menú Super+I.

Opciones:
  --dry-run          Muestra comprobaciones y acciones, sin hacer cambios.
  --skip-voxtype     No instala ni cambia la configuración de Voxtype.
  --skip-build       No publica; requiere que out/Capri/Capri ya exista.
  --apply-hyprland   Añade idempotentemente regla de borde y bind de rescate.
  -h, --help         Muestra esta ayuda.

Sin --apply-hyprland, solo muestra las líneas de Hyprland recomendadas.
Consulta HYPRLAND-SETUP.md para detalles y verificación.
EOF
}

run() {
  printf ' +'
  printf ' %q' "$@"
  printf '\n'
  if (( ! DRY_RUN )); then "$@"; fi
}

has_command() { command -v "$1" >/dev/null 2>&1; }

install_official_packages() {
  local packages=(dotnet-sdk imagemagick playerctl wireplumber libnotify desktop-file-utils wtype wl-clipboard uwsm xdg-utils)
  log "Instalando dependencias de los repositorios oficiales de Arch"
  if has_command omarchy-pkg-add; then
    run omarchy-pkg-add "${packages[@]}"
  else
    run sudo pacman -S --needed "${packages[@]}"
  fi
}

install_voxtype_package() {
  if has_command voxtype; then
    log "Voxtype ya está instalado"
    return
  fi

  log "Instalando voxtype-bin desde AUR"
  if has_command omarchy-pkg-aur-add; then
    run omarchy-pkg-aur-add voxtype-bin
  elif has_command yay; then
    run yay -S --needed voxtype-bin
  elif has_command paru; then
    run paru -S --needed voxtype-bin
  else
    die "No encuentro omarchy-pkg-aur-add, yay ni paru para instalar voxtype-bin."
  fi
}

backup_file() {
  local file="$1"
  if [[ -e "$file" ]]; then
    local backup="${file}.bak.$(date +%Y%m%d%H%M%S)"
    if (( DRY_RUN )); then
      log "DRY-RUN: crearía respaldo $backup"
    else
      cp -a -- "$file" "$backup"
      log "Respaldo creado: $backup"
    fi
  fi
}

set_voxtype_config() {
  local config="${XDG_CONFIG_HOME:-$HOME/.config}/voxtype/config.toml"
  local config_dir
  config_dir="$(dirname -- "$config")"

  if (( DRY_RUN )); then
    log "DRY-RUN: configuraría $config; si existe, primero crearía un respaldo"
    return
  fi

  mkdir -p -- "$config_dir"
  if [[ ! -f "$config" ]]; then
    voxtype setup --no-post-install
  fi
  [[ -f "$config" ]] || die "Voxtype no creó $config."
  backup_file "$config"

  run voxtype config set engine whisper
  run voxtype config set state_file auto
  run voxtype config set hotkey.enabled false
  run voxtype config set whisper.model large-v3-turbo
  run voxtype config set whisper.language es
  run voxtype config set whisper.mode local
  run voxtype config set output.mode type
  run voxtype config set output.fallback_to_clipboard true
  chmod 600 "$config"
  log "Voxtype configurado: Whisper large-v3-turbo, idioma es, hotkey global desactivado."
}

append_hypr_setting() {
  local file="$1" setting="$2" description="$3"
  if grep -Fqx -- "$setting" "$file"; then
    log "$description ya está presente en $file"
    return
  fi
  backup_file "$file"
  if (( DRY_RUN )); then
    log "DRY-RUN: añadiría $description a $file"
  else
    mkdir -p -- "$(dirname -- "$file")"
    touch -- "$file"
    printf '\n%s\n' "$setting" >> "$file"
    log "Añadido $description a $file"
  fi
}

configure_hyprland() {
  local rule='hl.window_rule({ name = "capri-no-outline", match = { class = "^Capri$" }, border_size = 0 })'
  local rescue='o.bind("SUPER + SHIFT + C", "Bring Capri to current screen", "capri --rescue")'

  if (( ! APPLY_HYPRLAND )); then
    log "Hyprland no se modificó. Para aplicar automáticamente, usa --apply-hyprland."
    printf '\nEn %s/hyprland.lua:\n%s\n' "$HYPR_DIR" "$rule"
    printf '\nEn %s/bindings.lua:\n%s\n' "$HYPR_DIR" "$rescue"
    printf '\nDetalles: %s/HYPRLAND-SETUP.md\n' "$SCRIPT_DIR"
    return
  fi

  append_hypr_setting "$HYPR_DIR/hyprland.lua" "$rule" "regla de borde solo para Capri"
  append_hypr_setting "$HYPR_DIR/bindings.lua" "$rescue" "atajo de rescate Super+Shift+C"
  if (( ! DRY_RUN )); then
    run hyprctl reload
  else
    log "DRY-RUN: recargaría Hyprland"
  fi
}

while (($#)); do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --skip-voxtype) SKIP_VOXTYPE=1 ;;
    --skip-build) SKIP_BUILD=1 ;;
    --apply-hyprland) APPLY_HYPRLAND=1 ;;
    -h|--help) usage; exit 0 ;;
    *) die "Opción desconocida: $1 (usa --help)." ;;
  esac
  shift
done

(( EUID != 0 )) || die "No ejecutes este script como root; necesita configurar tu sesión de usuario."
has_command pacman || die "Este instalador requiere Arch Linux / Omarchy (no encuentro pacman)."
if ! has_command omarchy && [[ ! -x /usr/share/omarchy/bin/omarchy ]]; then
  warn "No se detectó el comando omarchy; intentaré usar pacman y verificaré el entorno."
fi
[[ "${XDG_SESSION_TYPE:-}" == wayland ]] || die "Ejecuta el instalador dentro de tu sesión Wayland de Omarchy."
has_command hyprctl || die "No se encuentra hyprctl; Hyprland debe estar instalado y la sesión iniciada."

if has_command Hyprland; then
  hypr_version="$(Hyprland --version 2>/dev/null | head -n 1 || true)"
  log "Compositor detectado: ${hypr_version:-Hyprland}"
  if [[ "$hypr_version" =~ ([0-9]+)\.([0-9]+) ]] && (( ${BASH_REMATCH[1]} == 0 && ${BASH_REMATCH[2]} < 55 )); then
    warn "La configuración documentada usa Hyprland 0.55+ con Lua. Verifica HYPRLAND-SETUP.md antes de aplicar reglas."
  fi
fi

if (( DRY_RUN )); then
  log "Modo dry-run: se mostrarán las acciones sin cambiar archivos ni instalar paquetes."
  if (( ! SKIP_VOXTYPE )); then
    log "DRY-RUN: prepararía servicio de usuario, modelo large-v3-turbo y config de Voxtype."
  fi
  install_official_packages
else
  install_official_packages
fi

if (( ! SKIP_VOXTYPE )); then
  if (( ! DRY_RUN )); then
    install_voxtype_package
    has_command voxtype || die "Voxtype no quedó instalado."
    if [[ ! -f "${XDG_CONFIG_HOME:-$HOME/.config}/voxtype/config.toml" ]]; then
      log "Creando la configuración inicial de Voxtype con valores por defecto"
      run voxtype setup --no-post-install
    fi
    log "Preparando servicio de Voxtype"
    voxtype setup systemd
    run systemctl --user enable --now voxtype.service
    log "Descargando el modelo Whisper large-v3-turbo (si falta)"
    run voxtype setup --download --model large-v3-turbo --activate --no-post-install
    set_voxtype_config
    if voxtype setup gpu --status >/dev/null 2>&1; then
      log "Intentando habilitar aceleración GPU compatible para Voxtype"
      if ! voxtype setup gpu --enable; then
        warn "No se pudo habilitar GPU; Voxtype puede usar CPU."
      fi
    else
      warn "Voxtype no reportó estado GPU; la transcripción CPU sigue disponible."
    fi
    run systemctl --user restart voxtype.service
  else
    install_voxtype_package
  fi
else
  log "Voxtype omitido por --skip-voxtype"
fi

if (( ! SKIP_BUILD )); then
  log "Publicando Capri para Linux x64"
  run dotnet publish "$SCRIPT_DIR/cross/Capri.Cross.csproj" -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    -o "$SCRIPT_DIR/out/Capri"
  if (( ! DRY_RUN )); then
    [[ -x "$SCRIPT_DIR/out/Capri/Capri" ]] || die "No se generó el ejecutable $SCRIPT_DIR/out/Capri/Capri."
    chmod +x "$SCRIPT_DIR/out/Capri/Capri"
  fi
else
  log "Build omitido por --skip-build"
fi

if (( ! DRY_RUN )); then
  has_command dotnet || die "dotnet no quedó instalado."
  dotnet --list-sdks | python3 -c 'import sys; raise SystemExit(0 if any(x.split(" ",1)[0].startswith("10.") for x in sys.stdin if " " in x) else 1)' || die "Se requiere .NET SDK 10.x."
  [[ -x "$SCRIPT_DIR/out/Capri/Capri" ]] || die "Falta el ejecutable. Ejecuta sin --skip-build o publica Capri primero."
  mkdir -p "$HOME/.local/bin"
  run ln -sfn "$SCRIPT_DIR/out/Capri/Capri" "$HOME/.local/bin/capri"
  case ":${PATH}:" in
    *":$HOME/.local/bin:"*) ;;
    *) warn "$HOME/.local/bin no está en PATH en esta sesión; el bind capri --rescue necesitará esa ruta en PATH." ;;
  esac
  run "$SCRIPT_DIR/install-omarchy.sh"
else
  log "DRY-RUN: enlazaría ~/.local/bin/capri al publish y registraría el menú Omarchy."
fi

configure_hyprland

if (( ! SKIP_VOXTYPE )); then
  if (( ! DRY_RUN )); then
    if [[ -n "${XDG_RUNTIME_DIR:-}" && -f "$XDG_RUNTIME_DIR/voxtype/state" ]]; then
      log "Voxtype state: $(<"$XDG_RUNTIME_DIR/voxtype/state")"
    else
      warn "No veo el state file de Voxtype todavía; comprueba `systemctl --user status voxtype.service` después de iniciar la sesión gráfica."
    fi
  fi
fi

cat <<EOF

Instalación de Capri preparada.
- Menú: cierra y vuelve a abrir Super+I; busca Capri en Apps.
- Brain: en Capri → Brain settings…, introduce la clave OpenRouter propia del dispositivo,
  elige un modelo :free y conecta. Este instalador no lee ni copia claves.
- Voz: usa el botón Wave de Capri; los atajos globales de Voxtype quedan desactivados.
- Hyprland: consulta $SCRIPT_DIR/HYPRLAND-SETUP.md.
EOF
