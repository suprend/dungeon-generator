# Geometry & Configuration Spaces (геометрия и CS)

Код:
- `Assets/Scripts/Generation/Geometry/`

## Извлечение shape (discrete geometry)

`ShapeLibrary` читает Tilemap’ы внутри префаба и строит `ModuleShape`:
- `FloorCells`: занятые клетки пола (local)
- `WallCells`: занятые клетки стен (local)
- `Sockets`: сокеты (cell offset + side)

Это дискретная замена полигональной геометрии из статей Nepožitek.

## ConfigurationSpaceLibrary (CS)

Для пары префабов `(fixed, moving)` configuration space (CS) — это множество целочисленных смещений `delta`, таких что:
- существует пара сокетов `(sFixed, sMoving)` с противоположными сторонами (opposite),
- сокеты попадают в одну и ту же world‑клетку при соответствующем root‑смещении,
- overlap‑правила выполняются:
  - floor↔floor запрещён, кроме 1‑клеточного байта (Room↔Connector),
  - wall↔floor запрещён, кроме carve‑mask допусков.

CS предвычисляется/кэшируется и активно используется в:
- генерации кандидатных позиций layout’а,
- layout‑валидации,
- placement‑валидации.

## Маска выреза (carve‑mask)

Так как коннекторы часто имеют стены по бокам “входа”, мы считаем две тангенциальные wall‑клетки рядом с байт‑клеткой “вырезанными” (ignored/removed), чтобы не ловить постоянные конфликты на входах.
