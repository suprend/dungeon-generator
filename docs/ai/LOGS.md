# Logs (что означают ключевые сообщения)

Цель: чтобы по 10–20 строкам логов можно было быстро понять “что сломалось” и “где копать”.

## Тайминги пайплайна

- `[MapGraphLevelSolver] Timings (s): precompute=... solve=... layout=... place=... stamp=... total=...`
  - Ищи узкое место по компонентам: geometry/CS, layout, placement.
  - Источник: `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.LayoutWorkflow.cs`

## Configuration Space (CS)

- `[ConfigSpace] Empty offsets for A -> B`
  - Для пары префабов нет ни одного допустимого смещения (обычно: сокеты несовместимы, неверный `DoorSide`, конфликт floor/wall без carve‑исключений, `BiteDepth` слишком мал).
  - Источник: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs`

- `[ConfigSpace][dbg] ... => reject ...` / `accept depthX=...`
  - Подробная причина reject/accept для пары сокетов и конкретного `delta`.
  - Включается настройками verbose для CS.
  - Источник: `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Debug.cs`, `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Compute.cs`

## Layout generator

- `[LayoutGenerator] No position candidates for node ...`
  - Для узла не найдено позиций даже по fallback‑стратегии.
  - Часто следствие пустых/бедных CS или слишком жёстких ограничений.

- `[LayoutGenerator] AddChain produced 0 layouts for chain [...]`
  - SA не смог дать ни одного валидного результата для chain (проверь бюджеты SA и debug‑диагностику).

Источники: `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.*`

## Runtime wrappers

- `[GraphMapBuilder] Generation failed: ...`
  - Конечная ошибка полного пайплайна из сцены.
  - Источник: `Assets/Scripts/Generation/Runtime/GraphMapBuilder.cs`

- `[GraphGenRunner] Solve failed: ...`
  - Ошибка на уровне assignment solver (без layout).
  - Источник: `Assets/Scripts/Generation/Runtime/GraphGenRunner.cs`

## Что приложить к репорту (чтобы ИИ реально помог)

Смотри шаблон: `docs/ai/REPRO.md`.

