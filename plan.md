# Plan: fallback gratuito de OpenRouter para Capri

## Objetivo

Cuando un modelo gratuito esté temporalmente indisponible o limitado, Capri debe avanzar automáticamente por modelos gratuitos elegibles de OpenRouter. No debe enviar modelos de pago, informar claramente el límite diario y permitir reabrir Brain settings sin cerrar la app.

## Reglas de coste y elegibilidad

- Solo modelos cuyo id termina en `:free`.
- Confirmar precio cero desde el catálogo público; si falta precio/dato o no es numérico, rechazarlo.
- Requerir compatibilidad con tools, salida solo texto y que no esté expirado.
- Validar también el modelo recibido en la respuesta contra los ids del pool.
- Nunca usar fallback a un modelo de pago. Los errores 402/401/403 son definitivos.
- La garantía de la aplicación es no solicitar modelos pagos; saldo de OpenRouter en cero sigue siendo una barrera externa aconsejable, no una condición de código.

## Orden del catálogo

1. Modelo preferido guardado, si sigue en el catálogo verificado.
2. `nvidia/nemotron-3.5-lightning:free`.
3. `google/gemma-4-26b-a4b-it:free`.
4. `cohere/north-mini-code:free`.
5. Resto elegible de OpenRouter, orden determinista por contexto descendente e id.

## Pasos

- [x] Crear `cross/OpenRouterCatalog.cs`: descarga sin API key, caché local de 1 h, validación estricta, orden anterior y semilla offline.
- [x] Cambiar `cross/OpenRouterPool.cs` a catálogo dinámico, pool automático y estado de cuota/cooldown.
- [x] Mejorar `cross/OpenRouterWire.cs`: conservar `Retry-After` y `X-RateLimit-Reset`, enviar el conjunto gratuito elegible en el `models` array para que OpenRouter haga fallback, y detenerse ante cuota diaria; jamás reintentar 402.
- [x] Añadir defensa adicional de coste máximo cero en el provider object de cada llamada a chat completions.
- [x] Conectar `cross/Pet.cs` y `cross/BrainPanel.cs` al pool automático sin sobrescribir el modelo preferido con el último que respondió.
- [x] Consultar `GET /api/v1/key` para mostrar solicitudes gratuitas usadas/límite/restantes; limitar rondas de tools con poco cupo y no llamar a la API cuando se sabe que el cupo diario se agotó.
- [x] Mostrar mensajes diferenciados para cuota diaria, rate limit temporal, clave inválida y otros errores.
- [x] Corregir `ShowBrainPanel` para recrear la ventana después de cerrarla.
- [x] Añadir pruebas en `cross/Smoke.cs` para elegibilidad/orden, pool automático, cuota, defensa de precio cero y rechazo de respuestas no gratuitas.
- [x] Actualizar la sección Brain de `README.md` con fallback y modelos free-only.
- [x] Ejecutar build, smoke, publish Linux x64 y `git diff --check`; no se imprimió ni consultó la clave real del usuario.

## Límites conocidos

- OpenRouter determina la cuota diaria de cuenta y puede cambiar sus políticas. El estado de cuota es informativo y su lectura puede fallar; eso no debe permitir modelos pagos.
- El endpoint de Chat Completions documenta un array de fallbacks en `models`; implementar lotes no es necesario y podría hacer varias solicitudes/cargos para una misma pregunta. Preferir una sola petición con el array completo de modelos gratuitos elegibles.
- Los límites exactos por cuenta dependen de la política vigente de OpenRouter; no fijar el texto del límite a 50/1000 si la API devuelve los valores actuales.
- Cada ronda de herramientas puede consumir otra solicitud. Restringir trabajo adicional cuando el restante sea bajo.
