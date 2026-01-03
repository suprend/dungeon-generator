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
- Каждое удовлетворённое ребро должно давать ровно 1 клетку floor↔floor overlap (байт‑клетка / bite cell).
- Wall↔floor overlap запрещён, кроме:
  - байт‑клетки,
  - двух боковых wall‑клеток коннектора рядом с байт‑клеткой (carve‑mask).

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
