# Entrypoints (как запускать пайплайн и где смотреть)

Цель: быстро ответить на вопросы “откуда это вызывается?” и “какой уровень пайплайна я сейчас запускаю?”.

## Runtime (MonoBehaviour) — из сцены

### 1) Полный пайплайн: layout + placement + stamping
- `GraphMapBuilder`: `Assets/Scripts/Generation/Runtime/GraphMapBuilder.cs`
  - Вызывает `MapGraphLevelSolver.TrySolveAndPlace(...)` (с layout‑настройками).
  - Подходит для “как в игре”: финальный результат в Tilemaps.

### 2) Только assignment solver (без layout/placement/stamp)
- `GraphGenRunner`: `Assets/Scripts/Generation/Runtime/GraphGenRunner.cs`
  - Вызывает `MapGraphLevelSolver.TrySolve(...)`.
  - Полезно для проверки типов/назначений и валидации входного графа, но **не** проверяет геометрию/стыковку.

### 3) Debug stamping “по позициям из графа” (без layout)
- `GraphTilemapGenerator`: `Assets/Scripts/Generation/Runtime/GraphTilemapGenerator.cs`
  - Использует `node.position` как координаты Grid (с `positionScale`).
  - Штампует без CS/SA layout: это отладочный инструмент, а не гарантия валидности.

## Solver API — из кода

### `TrySolveAndPlaceWithLayout` (основной “канонический” вызов)
- `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs`
  - Делает:
    1) `BuildCorridorGraph` (edge‑rooms)
    2) `PrecomputeGeometry` (shapes + CS)
    3) direct assignments из графа (быстрый режим)
    4) `MapGraphLayoutGenerator.TryGenerate` (SA layout)
    5) placement + carve + stamp
  - Логи таймингов: `[MapGraphLevelSolver] Timings (s): ...`

### `TryGenerateLayout` (только layout, без placement/stamp)
- `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs`
  - Удобно для профилирования layout и диагностики “почему layout не находится” до placement.

## Где “точка истины” по правилам

- Инварианты и контракты: `docs/ai/CONTRACTS.md`
- Каноническое объяснение алгоритма: `Assets/readme.md`
- Маппинг док→код: `docs/ai/SEMANTIC_AUDIT.md`

