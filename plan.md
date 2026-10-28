# Plan: documentación y replicación de Capri en Omarchy

- [ ] Corregir `setup-omarchy.sh`: aviso de estado Voxtype sin backticks ejecutables, comprobación de .NET 10 sin Python, resumen Gemini/OpenRouter y diagnóstico final de solo lectura (binario, entrada de menú, servicio/estado Voxtype, claves solo existencia/permisos, y cambios Hyprland presentes o sugerir `--apply-hyprland`). Diagnóstico informativo y no cambia exit status.
- [ ] Corregir voz en `cross/Pet.cs`: permitir botón Wave si Gemini está conectado aunque OpenRouter no lo esté.
- [ ] Reescribir sección Linux/Omarchy del README sin encabezados duplicados: clonado de rama, setup dry-run/aplicar, flags, Brain actual, voz, Hyprland, último fix (thought signatures, grupos OpenRouter de 3, sin repetir acciones, brain-errors.log), archivos locales, migración/seguridad, síntomas 400/429/503 y ventana fuera de pantalla.
- [ ] Actualizar `AGENTS.md` para las claves Gemini/OpenRouter y la guía de instalación.
- [ ] Actualizar `HYPRLAND-SETUP.md` en cuanto a credenciales y dependencia de Brain.
- [ ] Verificar bash -n, dry-run sin ejecución accidental, diff-check, build y publish Linux x64. Reiniciar Capri y revisar clientes Hyprland.

## Restricciones

- No leer, copiar ni mostrar secretos; el diagnóstico solo verifica existencia y permisos.
- No copiar estado efímero de `$XDG_RUNTIME_DIR` entre máquinas.
- El setup automático aplica Hyprland solo con `--apply-hyprland`.
- No hacer commit ni push.
