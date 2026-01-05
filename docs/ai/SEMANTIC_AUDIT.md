# Semantic audit: `docs/ai` ↔ code

Цель: построчно сопоставить **термины/утверждения** из `docs/ai` с текущей реализацией в `Assets/Scripts`, отметить расхождения и (где возможно) привести документацию/комментарии к факту.

Статус: выполнено для текущих `docs/ai/*` и публичных/ключевых инвариантов пайплайна; ниже — “источник истины” по ссылкам на код.

## Pipeline (из `docs/ai/ARCHITECTURE.md`)

1) **Входной граф (`MapGraphAsset`)**
   - Код: `Assets/Scripts/Generation/Domain/MapGraphAsset.cs:12`
2) **Расширение графа в edge‑rooms (`BuildCorridorGraph`)**
   - Код: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.GraphExpansion.cs:9`
3) **Предвычисление геометрии (`ModuleShape` + CS)**
   - Shape: `Assets/Scripts/Generation/Geometry/ShapeLibrary.cs:17`
   - ModuleShape/Socket: `Assets/Scripts/Generation/Geometry/ShapeData.cs:30`
   - Precompute: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.Geometry.cs:11`
4) **Graph decomposition (faces/chains)**
   - Faces: `Assets/Scripts/Generation/Graph/MapGraphFaceBuilder.cs:50`
   - Chains: `Assets/Scripts/Generation/Graph/MapGraphChainBuilder.cs:25`
5) **Layout (SA)**
   - Entrypoint: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.cs:144`
   - Stack search: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.StackSearch.cs:4`
   - Chain add + SA loop orchestration: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.ChainPlacement.cs:9`
   - Candidates: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Candidates.cs:9`
   - Energy: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Energy.cs:13`
   - Validation: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Validation.cs:9`
6) **Placement + carve**
   - Entrypoint from solver: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs:56`
   - Placement pipeline: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:10`
7) **Stamping (Tilemap)**
   - `TileStampService`: `Assets/Scripts/Generation/Tilemap/TileStampService.cs:5`

## Invariants (из `docs/ai/README.md` / `docs/ai/ARCHITECTURE.md`)

### 1) После расширения валидные связи только `RoomMeta ↔ ConnectorMeta`

- Expansion реально создаёт “коридор‑ноды” и рёбра `room↔corridor`: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.GraphExpansion.cs:9`
- Layout валидирует “same-type edges” как ошибку: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Validation.cs:124`
- Placement валидирует “Room↔Corridor only” и кидает `InvalidOperationException` при нарушении: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:103`

### 2) Bite depth: `0 <= X < BiteDepth` по лучу `inward` от сокета коннектора

- Источник параметра: `DoorSocket.BiteDepth`: `Assets/Scripts/Modules/DoorSocket.cs:18`
- Поиск совместимой глубины в layout (room socket должен лежать на inward-ray): `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs:180`
- Та же геометрия в placement (TryComputeBiteDepth): `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:306`

### 3) Carve-mask: 3 дорожки до `X` (floor-ray + wall-rays) и исключение door-cell

- Layout/energy/validation используют компактную “маску разрешённых overlap”:
  - построение: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs:125`
  - применение в overlap penalty: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Energy.cs:130`
  - применение в строгой валидации: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Validation.cs:98`
- CS computation строит тот же carve-mask (floor-ray + wall-rays) для `Room↔Connector`:
  - `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Compute.cs:60`
- Placement реально “вырезает” клетки (carve) у коннектора и “пробивает дверь” в комнате:
  - `CarveConnectorBiteRays`: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:224`
  - `CarveRoomDoorCell`: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:251`

## Glossary → code map (из `docs/ai/GLOSSARY.md`)

- **Расширение графа / edge‑rooms** → `BuildCorridorGraph`: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.GraphExpansion.cs:9`
- **RoomMeta / ConnectorMeta** → маркеры типов модулей: `Assets/Scripts/Modules/RoomMeta.cs:3`, `Assets/Scripts/Modules/ConnectorMeta.cs:3`
- **DoorSocket** → authoring + `BiteDepth`: `Assets/Scripts/Modules/DoorSocket.cs:5`
- **DoorSocketSpan** → генератор дискретных сокетов: `Assets/Scripts/Modules/DoorSocketSpan.cs:10`
- **Door cell / base cell / inward / tangent** → layout bite-depth поиск и rays:
  - `TryFindBiteDepth`: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs:180`
  - `InwardVector/TangentVector`: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs:108`
- **Carve-mask / carve rays** → `AllowedWorldCells.Rays` + placement carve:
  - `AllowedWorldCells`: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.BiteAllowance.cs:5`
  - `CarveConnectorBiteRays`: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:224`
- **ModuleShape** → `ModuleShape`/`ShapeSocket`: `Assets/Scripts/Generation/Geometry/ShapeData.cs:30`
- **Configuration Space (CS)** → `ConfigurationSpaceLibrary.TryGetSpace`: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs:21`
- **Chain** → `MapGraphChainBuilder.Chain`: `Assets/Scripts/Generation/Graph/MapGraphChainBuilder.cs:9`
- **SA / Energy** → `ComputeEnergy` + SA-loop modules:
  - energy: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Energy.cs:13`
  - chain/SA orchestration: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.ChainPlacement.cs:9`
- **Placement / Stamping** → `PlacementState` + `TileStampService`:
  - placement: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs:10`
  - stamping: `Assets/Scripts/Generation/Tilemap/TileStampService.cs:5`

## Runtime entrypoints (из `docs/ai/MODULES/Runtime.md`)

- `GraphMapBuilder` (layout + placement + stamping): `Assets/Scripts/Generation/Runtime/GraphMapBuilder.cs:8`
- `GraphGenRunner` (только assignment solver): `Assets/Scripts/Generation/Runtime/GraphGenRunner.cs:8`
- `GraphTilemapGenerator` (простая “штамповка по позиции графа”, без layout): `Assets/Scripts/Generation/Runtime/GraphTilemapGenerator.cs:11`

## Logging (из `docs/ai/DEBUGGING.md`)

- `[MapGraphLevelSolver] Timings (s): ...`: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs:158`
- `[ConfigSpace] Empty offsets ...`: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs:54`
- `[ConfigSpace][dbg] ... => reject ...`: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Debug.cs:22`
- `[LayoutGenerator] ...` (no candidates / no layouts / profiling): `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Candidates.cs:155`, `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.cs:268`

## Known gaps / non-features (важно для “семантики”)

- `DoorSocket.SpanId` сейчас **не ограничивает** выбор “один сокет на span” автоматически (используется как authoring/diagnostics), см. `Assets/Scripts/Modules/DoorSocket.cs:14`.
- `TileStampService` не уничтожает инстансы (только `DisableRenderers`); уничтожение делает placement workflow.

