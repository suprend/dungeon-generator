# Solve / Placement workflow (оркестрация)

Код: `Assets/Scripts/Generation/Solve/`

## Ответственность

`MapGraphLevelSolver` оркестрирует стадии:
1) расширение графа (edge‑rooms),
2) precompute геометрии,
3) assignment (опционально; может быть direct‑from‑graph ради скорости),
4) layout generation,
5) placement validation + stamping.

Основной entry:
- `MapGraphLevelSolver.TrySolveAndPlaceWithLayout(...)`

## PlacementState

Placement берёт готовый layout и:
- инстанцирует префабы,
- выравнивает их по grid cell,
- применяет carve‑mask на входах коннекторов,
- проверяет overlap по occupancy state,
- штампует floors/walls в глобальные Tilemap.

Placement “строгий”: правила bite/carve должны совпадать с теми, что используются в CS и layout‑валидации.
