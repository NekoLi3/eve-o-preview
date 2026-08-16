# Changelog — EVE-O Preview (fork NekoLi3)

Cambios del fork privado sobre la base `Proopai/eve-o-preview` (rama `unified-source-build`).
Versiones: `8.0.2.<patch>-custom.<n>` — patch sigue la numeración del upstream, `custom` cuenta releases del fork.

## [8.0.2.19-custom.1] — 2026-08-16

### Agregado
- **Toggle de hotkeys de ciclo** (checkbox "Enable cycle hotkeys" en tab General).
  - Cuando está desactivado, las hotkeys de ciclo (CycleGroup1..5 forward/backward) se desregistran al vuelo: la tecla bindeada (ej. X) queda libre en todo Windows (las hotkeys son globales vía RegisterHotKey).
  - Al reactivarlo, se vuelven a registrar sin necesidad de reiniciar.
  - Estado persistente en la configuración (`CycleHotkeysEnabled`, default `true`).
  - El hotkey de "Minimize all clients" queda aislado y ya no se ve afectado por el toggle (fix de diseño: antes compartía la lista de handlers con las cycle hotkeys).

### Archivos clave
- `Configuration/Implementation/ThumbnailConfiguration.cs` — opción `CycleHotkeysEnabled`
- `View/Implementation/MainForm.cs` + `MainForm.Designer.cs` — checkbox en tab General
- `Services/Implementation/ThumbnailManager.cs` — `UpdateCycleHotkeys()` / register-unregister dinámico
- `Mediator/Messages/Thumbnails/ThumbnailCycleHotkeysSettingsUpdated.cs` (+ handler) — notificación config → manager

### Notas
- Build: `dotnet build src/Eve-O-Preview/Eve-O-Preview.csproj -c Release -p:EVEOTarget=Windows -p:EnableWindowsTargeting=true` (0 errores; 4 warnings pre-existentes del upstream).
- Binario: framework-dependent (~2.3MB), requiere .NET 8 Desktop Runtime.
