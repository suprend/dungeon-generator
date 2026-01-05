# AI Docs (entrypoint)

Папка для:
- онбординга новых людей в проект,
- работы с ИИ/агентами (какой контекст дать, какие инварианты нельзя ломать).

Компромисс формата:
- основной текст — на русском,
- ключевые термины при первом упоминании — с английским эквивалентом в скобках,
- имена классов/файлов/логов — как в коде.

## Быстрый контекст

Проект: генератор подземелий на Unity по графу (Nepožitek-inspired), адаптированный под Tilemap + дискретные клетки + сокеты.

Ключевые правила (инварианты, must not break):
- Граф расширяется в **edge-rooms**: каждое исходное ребро становится отдельной вершиной `ConnectorMeta`.
- После расширения валидные связи **только** `RoomMeta ↔ ConnectorMeta` (без `Room–Room` и `Connector–Connector`).
- Геометрия связи — **bite depth** (глубина укуса): сокет комнаты может стыковаться с коннектором на глубине `X`, где `0 <= X < BiteDepth` (по лучу `inward` от сокета коннектора).
- Практическая “вырезка” (carve mask) для выбранной глубины `X` работает по **3 дорожкам** (в world‑клетках):
  - центральная дорожка пола: `base + inward*k` для `k=0..X`,
  - две дорожки стен коннектора: `base + inward*k ± tangent` для `k=0..X`.
- Важно про дверь в комнате: сокеты дверей обычно авторятся в стене комнаты, но когда ребро реально используется, **в точке двери должен быть проход**. Поэтому во время layout/energy/validation разрешаем ровно одно пересечение `wall(room)` на `floor(connector)` в door‑cell.

Каноническое описание алгоритма: `Assets/readme.md`.

## Где живёт код (текущая раскладка)

- Компоненты модулей (префабы): `Assets/Scripts/Modules/`
- Пайплайн генерации: `Assets/Scripts/Generation/`
  - ассеты домена: `Assets/Scripts/Generation/Domain/`
  - геометрия + CS (configuration space): `Assets/Scripts/Generation/Geometry/`
  - декомпозиция графа: `Assets/Scripts/Generation/Graph/`
  - layout (раскладка) через SA (simulated annealing): `Assets/Scripts/Generation/Layout/`
  - solver/placement workflow: `Assets/Scripts/Generation/Solve/`
  - stamping (штамповка в Tilemap): `Assets/Scripts/Generation/Tilemap/`
  - runtime entrypoints: `Assets/Scripts/Generation/Runtime/`
  - utilities: `Assets/Scripts/Generation/Utils/`
  - debug helpers: `Assets/Scripts/Generation/Debug/`
- Legacy прототип: `Assets/Scripts/Legacy/`

Unity‑заметка:
- Unity компилирует всё под `Assets/`; `.csproj` вручную не поддерживаем.

## “Как спрашивать ИИ” (шаблон запроса)

В запросе лучше сразу приложить:
1) Цель (например “ускорить layout”, “почему CS пустой”).
2) Минимальный пример графа (размер, типы) и seed.
3) Релевантные логи (`[ConfigSpace]`, `[LayoutGenerator]`, `[MapGraphLevelSolver] Timings`).
4) Подозреваемые модули: Geometry / Layout / Placement.
5) Что можно менять: качество vs скорость vs детерминизм.

## Дальше читать

- Архитектура: `docs/ai/ARCHITECTURE.md`
- Документация по модулям: `docs/ai/MODULES/`
- Дебаг: `docs/ai/DEBUGGING.md`
- Термины: `docs/ai/GLOSSARY.md`
