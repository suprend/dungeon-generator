# Repro bundle (шаблон для багов/перфа)

Цель: минимальный пакет данных, с которым ИИ может **воспроизвести логику на бумаге** и указать точку в коде.

## Минимум для любого бага

1) **Что запускаешь**
- Entry: `GraphMapBuilder` / `GraphGenRunner` / `TrySolveAndPlaceWithLayout` (см. `docs/ai/ENTRYPOINTS.md`)

2) **Данные входа**
- `MapGraphAsset` имя/путь (или скрин инспектора).
- Размер: `nodes=N, edges=M`.
- Есть ли циклы (faces) или дерево.

3) **Seed и настройки**
- `randomSeed` (важно: `0` = недетерминизм).
- Layout settings (если используешь): `MaxLayoutsPerChain`, `TemperatureSteps`, `InnerIterations`, `Cooling`, лимиты кандидатов.
- Включены ли: verbose logs, verbose CS logs.

4) **Релевантные логи**
- строка таймингов `[MapGraphLevelSolver] Timings (s): ...`
- первые warnings/errors из:
  - `[ConfigSpace] ...`
  - `[LayoutGenerator] ...`
  - `LastError: ...` (placement/layout)

5) **Ожидаемое поведение**
- “должно находиться решение” / “должно быть N проходов” / “не должно быть дырок” и т.п.

## Для проблем “Empty offsets / CS пустой”

Добавь:
- Названия проблемной пары префабов `A -> B` из лога.
- Скрины двух префабов (room + connector):
  - где находятся `DoorSocket` (Side),
  - где `floor`/`wall` Tilemap,
  - какое `BiteDepth` на сокетах коннектора.

## Для проблем “в месте двери стена/дырка”

Добавь:
- Какой edge (пара nodeId) выглядит неправильно.
- Подозреваемый `X` (если виден в логах accept/reject).
- Скрин результата вокруг `doorCell` (5×5 клеток достаточно).

## Для перф‑проблем

Добавь:
- Тайминги и какая стадия доминирует.
- Примерный размер графа и сколько prefabs в кандидатах.
- Включи 1 раз `logLayoutProfiling`/summary (если есть) и приложи результат.

