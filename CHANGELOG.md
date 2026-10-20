# Changelog — EVE-O Preview (fork NekoLi3)

Cambios del fork privado sobre la base `Proopai/eve-o-preview` (rama `unified-source-build`).
Versiones: `8.0.2.<patch>-custom.<n>` — patch sigue la numeración del upstream, `custom` cuenta releases del fork. A partir de 8.0.3.0 el minor sube con features grandes.

## [8.0.4.5] — 2026-10-20

### Corregido
- **Ciclo de clientes excluidos en orden de adición**: los clientes excluidos con Shift+Left-Click ahora se ciclan en el orden en que se agregaron al grupo, no por handle de ventana.
  - Al excluir un cliente se agrega al final del orden; al volver a incluirlo se quita. Si se vuelve a excluir, se agrega nuevamente al final.
  - El ciclo hacia atrás recorre el orden inverso. El wrap-around y el comportamiento cuando el cliente activo no está excluido no cambian.
  - El orden es solo de sesión, igual que las exclusiones: no se persiste.

### Archivos clave
- `Services/Implementation/ThumbnailManager.cs` — `_excludedClientOrder`, `GetExcludedClientOrder()`
- `Services/Implementation/ThumbnailManager.ViewEvents.cs` — alta/baja del orden en `ThumbnailToggleCycleGroup()`
- `Services/Implementation/ThumbnailManager.Processes.cs` — limpieza al cerrar el cliente
- `src/Eve-O-Preview.Tests/ExcludedCycleTests.cs` — cobertura del orden (11 tests)
- `README.md` — documentación del orden de adición

### Notas
- Build: 0 errores (4 warnings preexistentes).
- Tests: 11/11 en `src/Eve-O-Preview.Tests`.

## [8.0.4.4] — 2026-10-20

### Agregado
- **Ciclo por hotkey de los clientes excluidos de los cycle groups**: dos listas nuevas config-only (`CycleExcludedForwardHotkeys` y `CycleExcludedBackwardHotkeys`, vacías por defecto) que ciclan únicamente entre los clientes marcados con Shift+Left-Click (`IsExcludedFromCycleGroup`), con wrap-around en ambos sentidos.
  - Comparten el flag `CycleHotkeysEnabled`: el checkbox "Enable cycle hotkeys" y `ToggleCycleHotkeysHotkey` (`Control+F23`) las habilitan/deshabilitan junto con las hotkeys de grupo.
  - Si no hay clientes excluidos, la hotkey no hace nada.
  - Las exclusiones siguen siendo solo de sesión: no se persisten y se pierden al reiniciar o cerrar el cliente.

### Archivos clave
- `Configuration/.../IThumbnailConfiguration.cs` (+ `ThumbnailConfiguration.cs`) — `CycleExcludedForwardHotkeys`, `CycleExcludedBackwardHotkeys`
- `Services/Implementation/ThumbnailManager.cs` — `CycleNextExcludedClient()`, `RegisterExcludedCycleClientHotkey()` y registro en `RegisterCycleHotkeys()`

### Notas
- Build: 0 errores (4 warnings preexistentes).
- Tests: 7/7 en `src/Eve-O-Preview.Tests` (xUnit).

## [8.0.4.3] — 2026-08-16

### Agregado
- **Hotkey de toggle para "Enable cycle hotkeys"**: nueva hotkey global configurable (`ToggleCycleHotkeysHotkey`, default `Control+F23`, lista estilo `MinimizeAllClientsHotkeys`) que invierte `CycleHotkeysEnabled` al vuelo sin abrir la UI:
  - Si las cycle hotkeys están activas → se desregistran (la tecla queda libre en Windows).
  - Si están apagadas → se vuelven a registrar.
  - El checkbox "Enable cycle hotkeys" en tab General se sincroniza inmediatamente y el nuevo estado persiste en la config (`EVE-O-Preview.json`).
  - Flujo: ThumbnailManager (hotkey) → `CycleHotkeysToggleRequested` → handler → flip de config + `UpdateCycleHotkeys()` + sync de vista + save. Un solo re-registro por toggle, sin loops.
  - Config-only: no hay campo UI (igual que las hotkeys de "Minimize all clients"), se configura vía JSON.

### Archivos clave
- `Configuration/.../ThumbnailConfiguration.cs` (+ interfaz) — `ToggleCycleHotkeysHotkey`
- `Services/Implementation/ThumbnailManager.Hotkeys.cs` (nuevo partial) — `RegisterCycleHotkeysToggleHotkey`
- `Mediator/.../CycleHotkeysToggleRequested.cs` (+ handler) — flujo toggle manager → presenter
- `Presenters/.../MainFormPresenter.cs` (+ `IMainFormPresenter`) — `ApplyCycleHotkeysState()` (sync checkbox)

### Notas
- Build: 0 errores. Binario framework-dependent.

## [8.0.4.2] — 2026-08-16

### Cambiado
- **"Hide caption bar on clients" ahora activado por defecto** (antes off). El default aplica a configs nuevos o que no guarden la clave explícitamente; configs existentes con `HideCaptionOnClients: false` guardado se respetan.

### Fix
- **Cursor de thumbnails vuelve a la flecha por defecto** (antes manita `Cursors.Hand`; se veía solo con overlays visibles). Commit `40a9a22`/`490710d`, previo a este release pero nunca lanzado.

## [8.0.3.1] — 2026-08-16

### Fix
- **Sistema solar en thumbnail no mostraba nada** (reportado por Rai): el `GamelogMonitor` buscaba el gamelog en `%TMP%\EVE Online\gamelog.txt` (clientes antiguos), pero el cliente moderno (launcher) escribe los logs en `Documents\EVE\logs\Gamelogs\gamelog.txt`. Ahora resuelve el path en orden: `GamelogPath` configurado → `Documents\EVE\logs\Gamelogs\gamelog.txt` (si existe el directorio) → fallback `%TMP%\EVE Online\gamelog.txt` (legacy).

## [8.0.4.1] — 2026-08-16

### Agregado
- **Pestaña "System" nueva** en el panel de opciones (entre Overlay y Active Clients): agrupa el checkbox "Show system location" (movido de General) y la configuración del label del sistema (fuente, color, posición) en un layout limpio de columna única.
- **Configuración del label del sistema** (independiente del label del personaje): `SystemLabelFont`, `SystemLabelColor` (vacío = hereda del label del personaje) y `SystemLabelPosition` (BelowName + 9 anclas). Sección en la pestaña System.
- **About actualizado:** link a los releases del fork (NekoLi3/eve-o-preview) y crédito a la base Proopai.

### Fix
- **Sistema solar en thumbnail ahora lee los CHATLOGS de Local por personaje** (antes gamelog): el cliente moderno escribe los logs en `Documents\EVE\logs\Chatlogs\Local_*.txt` (UTF-16LE con BOM, uno por personaje con "Listener:" y "Channel changed to Local : <sistema>"). Watcher + refresco periódico (3s), parseo incremental, manejo de rotación por sesión (LastWriteTime), tolerante a archivos bloqueados por EVE. Opción de config: `ChatlogsPath` (default: auto = Documents\EVE\logs\Chatlogs).
- Fix previo (8.0.3.1, no lanzado): path del gamelog para clientes modernos.

### Archivos clave
- `Services/Implementation/GamelogMonitor.cs` — fuente: chatlogs de Local (rewrite)
- `Configuration/Interface/SystemLabelPosition.cs` — enum de posiciones (nuevo)
- `View/Implementation/MainForm.Designer.cs` — pestaña System + About
- `View/Implementation/ThumbnailOverlay.cs` — posicionamiento del label por ancla
- `Presenters/Implementation/MainFormPresenter.cs` — About URL

### Notas
- Build: 0 errores (4 warnings pre-existentes). Binario framework-dependent (~2.3MB).
- `ShowSystemInThumbnail` default `false`: el overlay no cambia hasta activar la opción.

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
