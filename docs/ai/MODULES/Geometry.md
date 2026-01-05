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
  - для `Room↔Connector` поддерживается **bite depth**: сокет комнаты может быть состыкован с коннектором на глубине `X` по лучу `inward` от сокета коннектора, где `0 <= X < BiteDepth`,
  - разрешённые overlap‑клетки для выбранного `X` задаются carve‑mask (см. ниже),
  - для остальных пар типов (например `Room↔Room`/`Connector↔Connector`) **в текущей реализации CS не строится**: `ConfigurationSpaceLibrary.TryGetSpace(...)` возвращает пустое пространство, т.к. валидные связи после edge‑rooms подразумеваются только как `Room↔Connector`.

CS предвычисляется/кэшируется и активно используется в:
- генерации кандидатных позиций layout’а,
- layout‑валидации,
- placement‑валидации.

## Маска выреза (carve‑mask)

Carve‑mask — это набор overlap‑клеток, которые **разрешены** в проверках (CS/layout/validation) для `Room↔Connector` при выбранной глубине `X`.

Он работает по **3 дорожкам** (в world‑клетках), построенным относительно base‑клетки сокета коннектора на глубине 0:
- floor‑ray: `base + inward*k` для `k=0..X`
- wall‑rays: `base + inward*k ± tangent` для `k=0..X`

Интуиция: коннекторы часто имеют стены по бокам “входа”, а при bite‑depth мы разрешаем “вкусить” внутрь коннектора до глубины `X`, игнорируя эти конфликты в проверках.
