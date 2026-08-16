# Changelog — EVE-O Preview (fork NekoLi3)

Cambios del fork privado sobre la base `Proopai/eve-o-preview` (rama `unified-source-build`).
Versiones: `8.0.2.<patch>-custom.<n>` — patch sigue la numeración del upstream, `custom` cuenta releases del fork. A partir de 8.0.3.0 el minor sube con features grandes.

## [8.0.3.0-custom.2] — 2026-08-16

### Agregado
- **Sistema solar actual en el thumbnail** (checkbox "Show system location" en tab General, off por defecto):
  - Nuevo servicio `GamelogMonitor` que vigila el `gamelog.txt` de EVE (default `%TMP%\EVE Online\gamelog.txt`, configurable con `GamelogPath`) y parsea sesiones por personaje (`Session Started/Ended`, entradas `System`/`Station`).
  - Cada thumbnail muestra el sistema de su personaje debajo del nombre (segundo label en el overlay), actualizado en el ciclo de refresh (~500ms).
  - Lectura incremental del archivo (solo líneas nuevas), tolerante a rotación/truncado y a archivo inexistente (reintento cada 5s). Todo en threadpool, sin tocar UI.
- **Perfiles de layout** (tab "Profiles" nuevo):
  - Guardar/cargar/eliminar configuraciones completas a `profiles/<nombre>.json` (snapshot del config, mismo formato que el config principal).
  - Cargar aplica el perfil al vuelo (re-aplica opciones, hotkeys de cliente y ciclo, tamaños/posiciones de thumbnails) sin reiniciar.
  - Nombres sanitizados (sin path traversal, sin caracteres inválidos ni nombres reservados de Windows).

### Archivos clave
- `Services/Implementation/GamelogMonitor.cs` (+ `IGamelogMonitor`) — watcher + parser del gamelog
- `Services/Implementation/ProfileManager.cs` (+ `IProfileManager`) — persistencia de perfiles
- `View/Implementation/ThumbnailOverlay.cs` — segundo label (sistema) en el overlay
- `View/Implementation/MainForm.cs` + `MainForm.Designer.cs` — checkbox "Show system location" + tab "Profiles"
- `Presenters/Implementation/MainFormPresenter.cs` — sync de las opciones nuevas + re-aplicación de perfiles
- `Mediator/.../ThumbnailClientHotkeysUpdated.cs` (+ handler) — re-registro de hotkeys por cliente al cargar perfil

### Notas
- Build: 0 errores (4 warnings pre-existentes del upstream). Binario framework-dependent (~2.3MB).
- `ShowSystemInThumbnail` default `false`: el overlay no cambia visualmente hasta activar la opción.

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
