# Tilemap stamping (штамповка)

Код: `Assets/Scripts/Generation/Tilemap/TileStampService.cs`

## Ответственность

`TileStampService` обеспечивает:
- конвертацию world ↔ grid cell,
- чтение prefab Tilemap’ов для извлечения shape,
- штамповку floors/walls модулей в глобальные Tilemap,
- опциональную очистку/скрытие рендеринга префабов (`DisableRenderers`).

Примечание:
- уничтожение инстансов после штамповки делает placement workflow (например `PlacementState.DestroyPlacedInstances`), а не `TileStampService`.

Предполагается строгое дискретное выравнивание: сокеты и root‑клетки модулей должны совпадать с центрами клеток grid.
