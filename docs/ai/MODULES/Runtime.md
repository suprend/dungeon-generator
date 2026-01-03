# Runtime entrypoints (входы из сцены)

Код: `Assets/Scripts/Generation/Runtime/`

Типичные entry‑компоненты:
- `GraphMapBuilder` (MonoBehaviour): запускает solver/layout и штампует результат в Tilemap.
- `GraphGenRunner` / `GraphTilemapGenerator`: хелперы для экспериментов в сцене.

## Частые runtime‑параметры

- random seed (детерминизм)
- настройки layout generator (бюджет SA, лимиты кандидатов)
- verbose logs / config space debug

Смотри тайминги, чтобы понять узкое место:
`[MapGraphLevelSolver] Timings (s): precompute=... solve=... layout=... place=... stamp=...`
