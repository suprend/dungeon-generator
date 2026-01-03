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
