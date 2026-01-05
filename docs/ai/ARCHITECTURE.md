# Архитектура

Документ описывает **реальный пайплайн в коде** и зависимости между модулями.

## Пайплайн (high level)

Сквозной поток генерации:

1) Входной граф (`MapGraphAsset`)
2) Расширение графа в edge‑rooms (`BuildCorridorGraph`)
3) Предвычисление геометрии:
   - чтение floor/wall Tilemap из префабов → `ModuleShape`
   - предвычисление configuration spaces (CS) для пар префабов
4) Layout (раскладка “куда поставить”) через SA (simulated annealing):
   - разложение графа на faces/chains
   - инкрементальная укладка chain’ов + SA оптимизация
5) Placement (инстанцирование + проверка overlap + carve у входов)
6) Stamp (штамповка) в глобальные Tilemap (floor/wall)

Основной entrypoint:
- `MapGraphLevelSolver.TrySolveAndPlaceWithLayout(...)` в `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs`

## Границы ответственности

Система разделяет:
- **что** выбрать (prefab selection / types),
- **куда** поставить (layout).

Сейчас layout берёт кандидатов префабов прямо из `MapGraphAsset`/типов, поэтому стадия solver может быть “быстрой” (просто подтвердить дефолты) и даже обходиться ради скорости.

## Инварианты (валидируются)

- Рёбра валидны только как `RoomMeta ↔ ConnectorMeta`.
- Удовлетворённое ребро `Room ↔ Connector` выбирает глубину `X` вдоль луча сокета коннектора:
  - `X` — целое число,
  - `0 <= X < BiteDepth` (BiteDepth хранится в `DoorSocket.BiteDepth` у коннектора).
- Пересечения (overlap) разрешаются только как часть carve‑mask для выбранной глубины `X`:
  - `floor(connector)` ↔ `floor(room)` допускается на центральной дорожке `base + inward*k`, `k=0..X`,
  - `wall(connector)` ↔ `floor(room)` (и наоборот, в зависимости от того, кто fixed/moving) допускается на двух дорожках `base + inward*k ± tangent`, `k=0..X`,
  - `wall(room)` ↔ `floor(connector)` допускается **только** в `door cell = base + inward*X` (потому что сокет комнаты авторится в стене, но при использовании ребра должен становиться проходом).

## Зависимости модулей

Рекомендуемое направление:

`Domain` → `Modules` → `Geometry` → `Graph` → `Layout` → `Solve` → `Tilemap`

- `Domain`
  - `MapGraphAsset`, `RoomTypeAsset`, `ConnectionTypeAsset`
- `Modules`
  - `ModuleMetaBase`, `RoomMeta`, `ConnectorMeta`, `DoorSocket`, `DoorSocketSpan`
- `Geometry`
  - `ShapeLibrary` строит `ModuleShape` из префаба
  - `ConfigurationSpaceLibrary` строит CS‑offsets по shape’ам и правилам сокетов
- `Graph`
  - planar embedding faces + chain decomposition
- `Layout`
  - SA / energy / candidates, использует CS из `Geometry`
- `Solve`
  - оркестрация, placement в occupancy, stamping
- `Tilemap`
  - чтение/запись Unity Tilemaps (`TileStampService`)

## Где обычно “горит” по времени

1) Inner loop SA в layout:
   - не делать дорогую валидацию на каждом шаге
   - использовать инкрементальную энергию и кеши
2) Генерация кандидатов:
   - избегать аллокаций в tight loops
   - держать доступ к CS O(1) через кеши
3) Global validation:
   - платить за неё только когда кандидат близок к решению
