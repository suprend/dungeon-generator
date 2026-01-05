# Layout (SA) модуль

Код: `Assets/Scripts/Generation/Layout/`

## Ответственность

Дано:
- expanded graph (edge‑rooms; только `Room↔Connector`),
- набор доступных префабов на узлы/рёбра (через типы),
- предвычисленные configuration spaces (CS),

Нужно получить:
- `root`‑клетку для каждой вершины (layout positions),
- выбранный prefab для каждой вершины (из допустимого списка).

## Основные части

- `MapGraphLayoutGenerator.TryGenerate(...)`
  - строит faces/chains
  - запускает инкрементальный поиск через “stack of partial layouts”
- `AddChain(...)`
  - строит начальную раскладку для chain’а
  - улучшает её через SA (simulated annealing)
- Генерация кандидатов (candidate generation)
  - пересечение CS по уже размещённым соседям
  - fallback “удовлетворить максимум соседей”
- Энергия (energy)
  - overlap penalty: запрещённые пересечения floor/wall (с исключениями bite/carve)
  - distance penalty: несостыкованные рёбра (CS mismatch) штрафуются квадратом расстояния

## Перформанс

Основные ручки:
- `MaxLayoutsPerChain`
- `TemperatureSteps`
- `InnerIterations`
- `Cooling`
- лимиты кандидатов (`MaxWiggleCandidates`, `MaxFallbackCandidates`)

Уже применённые оптимизации:
- инкрементальный energy cache на шагах perturb
- не делать дорогую полную валидацию на каждом SA‑шаге; валидировать только при сохранении результатов

## Где что лежит (по файлам)

Код в `Assets/Scripts/Generation/Layout/` разбит на модули:
- `MapGraphLayoutGenerator.cs`
  - публичный API (`TryGenerate`) + `Settings`
- `MapGraphLayoutGenerator.Prefabs.cs`
  - сбор кандидатов префабов по узлам (lookup из `MapGraphAsset`/типов)
- `MapGraphLayoutGenerator.Candidates.cs`
  - генерация кандидатных позиций (пересечение CS по соседям + fallback “удовлетворить максимум соседей”)
- `MapGraphLayoutGenerator.Profiler.cs`
  - маркеры `Profiler.BeginSample` (`PS(...)`, `S_*`)
- `MapGraphLayoutGenerator.Warmup.cs` / `MapGraphLayoutGenerator.Context.cs`
  - прогрев shape/CS, сбор lookup’ов, построение `LayoutContext`
- `MapGraphLayoutGenerator.StackSearch.cs`
  - stack‑поиск по chain’ам (инкрементальное добавление chain’ов)
- `MapGraphLayoutGenerator.ChainPlacement.cs`
  - `AddChain` / `AddChainSmall` / `GetInitialLayout` + SA‑цикл принятия/отката
- `MapGraphLayoutGenerator.SA.cs`
  - служебный “stub” файл (SA‑логика вынесена в `Annealing.cs`/`ChainPlacement.cs`)
- `MapGraphLayoutGenerator.Annealing.cs`
  - внутренняя perturb‑логика: `TryPerturbInPlace`, `WiggleCandidates`, `UpdateEnergyCacheInPlace`, undo‑структуры
- `MapGraphLayoutGenerator.EnergyCache.cs`
  - `EnergyCache` + packed pair indexing + построение/клон кэша
- `MapGraphLayoutGenerator.Energy.cs`
  - энерго‑термы: `IntersectionPenalty*`, `ComputeEdgeDistancePenalty`
- `MapGraphLayoutGenerator.Validation.cs` + `MapGraphLayoutGenerator.Validation.Overlap.cs`
  - строгая валидация layout’а и overlap‑проверки (с учётом bite/carve)
- `MapGraphLayoutGenerator.BiteAllowance.cs`
  - вычисление разрешённых overlap‑клеток для bite depth (`TryGetBiteAllowance`, `TryFindBiteDepth`)
- `MapGraphLayoutGenerator.Diagnostics.cs`
  - `DebugNoLayoutsDump` и диагностические helpers
