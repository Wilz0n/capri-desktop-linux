# Configuración de Hyprland para Capri

Esta guía permite configurar Capri en Omarchy sin reemplazar los archivos personales de Hyprland. Está escrita para que una persona o agente de IA pueda localizar y aplicar únicamente las dos integraciones de Capri.

## Requisitos

- Sesión Wayland con Hyprland 0.55 o posterior y configuración Lua de Omarchy.
- Capri publicada desde este checkout y `capri` disponible en `PATH` para el atajo de rescate.
- La clase de ventana predeterminada es `Capri`.

## Cambios solicitados

Los bloques entre marcadores son las únicas líneas de Capri que se deben incorporar. No copies ni sobrescribas el resto de los archivos.

### `~/.config/hypr/hyprland.lua`

Coloca la regla al final del archivo principal o en un módulo Lua que cargue:

<!-- capri:hyprland:begin -->
```lua
hl.window_rule({ name = "capri-no-outline", match = { class = "^Capri$" }, border_size = 0 })
```
<!-- capri:hyprland:end -->

La regla elimina el borde del compositor solo a Capri; no cambies el ancho ni los colores globales. Si el archivo usa módulos, la regla debe ejecutarse después de que Omarchy inicialice `hl`.

### `~/.config/hypr/bindings.lua`

Coloca el bind en el archivo de atajos personales, después de que Omarchy inicialice `o`:

<!-- capri:bindings:begin -->
```lua
o.bind("SUPER + SHIFT + C", "Bring Capri to current screen", "capri --rescue")
```
<!-- capri:bindings:end -->

El bind contacta a la instancia Capri que está ejecutándose. Mantén `~/.local/bin` en `PATH` y verifica que `capri` apunte al ejecutable publicado de este checkout (`out/Capri/Capri`). Este atajo no inicia Capri si está cerrada.

No añadas atajos globales de Voxtype. La grabación para Capri se inicia desde su botón Wave; el instalador configura Voxtype con `[hotkey] enabled = false`.

## Aplicación y verificación

Después de añadir ambos bloques:

```sh
hyprctl reload
```

Inicia Capri desde Super+I → Apps, y comprueba la clase y la regla efectiva:

```sh
hyprctl clients -j
hyprctl binds -j
```

La ventana debe tener clase `Capri` y el bind debe mostrar la descripción `Bring Capri to current screen`. Para probar el rescate, mueve Capri a una ubicación fuera de pantalla y pulsa **Super+Shift+C**; debe volver a la pantalla actual.

## Hyprland anterior a 0.55

La sintaxis anterior a 0.55 suele usar reglas en formato `hyprland.conf`; esta configuración Lua no debe pegarse literalmente en ese formato. Consulta la documentación oficial de reglas de ventana y binds correspondiente a la versión instalada antes de adaptarla. No inventes una regla ni cambies la configuración global. El bind de rescate debe invocar `capri --rescue`.

## Límites de seguridad y migración

- No pongas `border_size = 0` como regla global ni cambies colores de borde globales.
- No sobrescribas todo `~/.config/hypr` ni `~/.config/voxtype/config.toml`.
- No copies estado efímero de `$XDG_RUNTIME_DIR` (`voxtype/state`, `capri/voice.txt`) entre máquinas.
- No copies las claves Gemini/OpenRouter ni otros secretos. Configura las claves propias de cada dispositivo desde **Brain settings…** de Capri.
- Antes de editar manualmente cualquiera de los dos archivos Lua, crea una copia de respaldo y evita duplicar las líneas si ya existen.
