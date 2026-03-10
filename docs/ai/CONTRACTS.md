# Contracts (инварианты и “что считается багом”)

Цель: дать ИИ и людям **однозначный контракт** между документацией и кодом. Если поведение не соответствует пунктам ниже — это либо баг, либо сознательное изменение контракта (и тогда правим этот документ и `docs/ai/SEMANTIC_AUDIT.md`).

Каноника алгоритма (объяснение “почему так”): `Assets/readme.md`.

## 1) Граф и edge‑rooms (graph expansion)

**Контракт**
- Вход: `MapGraphAsset` (узлы/рёбра, типы).
- Перед layout/placement граф расширяется: **каждое исходное ребро** превращается в отдельную corridor‑вершину (edge‑room) и два ребра `room↔corridor`.
- После расширения валидны только связи **`RoomMeta ↔ ConnectorMeta`** (не допускаем `Room–Room` и `Connector–Connector`).

**Код‑якоря**
- Expansion: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.GraphExpansion.cs` (`BuildCorridorGraph`).
- Same‑type edges:
  - CS: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs` (`TryGetSpace` возвращает `ConfigurationSpace.Empty`).
  - Layout: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs` (`TryGetBiteAllowance` возвращает `false`).
  - Placement: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs` (валидация в placement workflow).

## 2) Сокеты и типы модулей (prefab authoring)

**Контракт**
- Room prefab: на корне `RoomMeta : ModuleMetaBase`.
- Connector prefab: на корне `ConnectorMeta : ModuleMetaBase`.
- Сокеты: `DoorSocket` (сторона `DoorSide` + позиция в центре клетки Grid).
- `DoorSocket.Width` сейчас фактически **не поддерживается** (всегда 1).
- `DoorSocket.SpanId` — authoring/diagnostics; solver **не** накладывает автоматическое ограничение “один сокет на span”.

**Код‑якоря**
- `Assets/Scripts/Modules/DoorSocket.cs`
- `Assets/Scripts/Modules/RoomMeta.cs`, `Assets/Scripts/Modules/ConnectorMeta.cs`
- `Assets/Scripts/Generation/Geometry/ShapeLibrary.cs` (как socket cell трактуется как floor‑capable)

## 3) Bite depth (стыковка на глубине `X`)

**Определение**
- У сокета коннектора есть `DoorSocket.BiteDepth >= 1`.
- Для ребра `Room ↔ Connector` выбирается целое `X`, где `0 <= X < BiteDepth`.
- `roomSocket` должен лежать **точно** на inward‑луче сокета коннектора:
  - `worldCell(roomSocket) = baseCell(connectorSocket) + inward(connectorSide) * X`.

**Важно**
- Bite depth — это “геометрия стыка” (какая клетка является дверью), а не “разрешение на произвольный overlap”.
- Для room‑сокетов значение `BiteDepth` не используется как смысловая настройка (глубину задаёт сокет коннектора).

**Код‑якоря**
- CS: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Compute.cs` (перебор `depthX` и проверка overlap с carve‑исключениями).
- Layout: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs` (поиск bite‑глубины и вычисление разрешённых клеток).
- Placement: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs` (`TryComputeBiteDepth` / план carve).

## 4) Carve‑mask (3 дорожки) и разрешённые пересечения

**Определение**
Для выбранной глубины `X` (bite depth) carve‑mask задаёт **разрешённые** пересечения в world‑клетках:
- floor‑ray: `base + inward*k` для `k=0..X`
- wall‑rays: `base + inward*k ± tangent` для `k=0..X`

**Контракт валидности (в проверках)**
- `Floor↔Floor` запрещено **везде**, кроме `floor‑ray` на удовлетворённом `Room↔Connector`.
- `Wall(connector)↔Floor(room)` разрешено только на `wall‑rays`.
- `Wall(room)↔Floor(connector)` разрешено **только** в `doorCell = base + inward*X` (потому что сокеты комнат часто авторятся в стене, но при использовании ребра там должен быть проход).

**Код‑якоря**
- Маска как компактная структура: `Assets/Scripts/Generation/Layout/AllowedWorldCells.cs`.
- Использование в layout/validation/energy: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.*`.
- Использование в CS: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Compute.cs`.

## 5) Placement carve (реальная “вырезка” геометрии)

**Контракт**
- Placement не только проверяет carve‑mask, но и **реально вырезает** конфликтующие клетки у коннектора, и “пробивает дверь” в комнате.
- Практическая семантика carve для коннектора (до глубины `X`):
  - стены на центральной и боковых дорожках удаляются,
  - пол на центральной дорожке удаляется для `k < X`, но сохраняется в `doorCell`,
  - пол/стены на боковых клетках (`±tangent`) удаляются.
- Для комнаты `doorCell` принудительно делается проходом (wall→floor) только если ребро действительно используется.

**Код‑якоря**
- `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs` (`CarveConnectorBiteRays`, `CarveRoomDoorCell`).

## 6) Детерминизм и seed

**Контракт**
- `randomSeed != 0`: ожидание детерминизма в рамках алгоритма (при прочих равных и без внешних источников рандома).
- `randomSeed == 0`: seed выбирается случайно; для фейлов допускаются несколько попыток с разными seed’ами (если включено).

**Код‑якоря**
- `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs` (`TrySolveAndPlaceWithLayout`).

## 7) Non‑features (важно не “придумывать”)

- Door width > 1 не поддерживается.
- Автоматическое ограничение “один сокет на span” не поддерживается (SpanId — метка).
- Room↔Room и Connector↔Connector связи после expansion не поддерживаются (по дизайну).

