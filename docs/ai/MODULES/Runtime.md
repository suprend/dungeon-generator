# Runtime entrypoints (входы из сцены)

Код: `Assets/Scripts/Generation/Runtime/`

Типичные entry‑компоненты:
- `GraphMapBuilder` (MonoBehaviour): запускает solver/layout и штампует результат в Tilemap.
- `GeneratedLevelRuntime` (MonoBehaviour): runtime-слой поверх `GraphMapBuilder`; хранит lookup `cell -> room`, спавнит игрока и привязывает Cinemachine.
- `GraphGenRunner` / `GraphTilemapGenerator`: хелперы для экспериментов в сцене.

Подробная раскладка “что именно запускает каждый компонент” (и где лежат solver entrypoints): `docs/ai/ENTRYPOINTS.md`.

## Частые runtime‑параметры

- random seed (детерминизм)
- настройки layout generator (бюджет SA, лимиты кандидатов)
- verbose logs / config space debug

Смотри тайминги, чтобы понять узкое место:
`[MapGraphLevelSolver] Timings (s): precompute=... solve=... layout=... place=... stamp=...`
