# Runtime entrypoints (входы из сцены)

Код: `Assets/Scripts/Generation/Runtime/`

Типичные entry‑компоненты:
- `GraphMapBuilder` (MonoBehaviour): запускает solver/layout и штампует результат в Tilemap.
- `GeneratedLevelRuntime` (MonoBehaviour): runtime-слой поверх `GraphMapBuilder`; хранит lookup `cell -> room`, спавнит игрока и привязывает Cinemachine.
- `GeneratedLevelNavMeshRuntime` (MonoBehaviour): пересобирает 2D navmesh после generation/stamp через `NavMeshPlus` (`NavMeshSurface` + `CollectSources2d`).
- `LevelEnemyController` (MonoBehaviour): спавнит врагов по `GeneratedRoomInfo.EnemySpawns`, не использует connector'ы и активирует AI только в текущей non-connector комнате игрока.
- `EnemyAuthoring` (MonoBehaviour): живёт на enemy prefab; хранит HP/contact damage/AI-параметры и требует базовые runtime-компоненты врага.
- `GraphGenRunner` / `GraphTilemapGenerator`: хелперы для экспериментов в сцене.

Подробная раскладка “что именно запускает каждый компонент” (и где лежат solver entrypoints): `docs/ai/ENTRYPOINTS.md`.

## Частые runtime‑параметры

- random seed (детерминизм)
- настройки layout generator (бюджет SA, лимиты кандидатов)
- verbose logs / config space debug

Смотри тайминги, чтобы понять узкое место:
`[MapGraphLevelSolver] Timings (s): precompute=... solve=... layout=... place=... stamp=...`
