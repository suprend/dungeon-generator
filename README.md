# Dungeon Generator (Unity)

Генератор подземелий на Unity на основе графа связности (Nepožitek-inspired), адаптированный под дискретную Tilemap‑геометрию и `DoorSocket`.

## Документация

- Каноническое описание алгоритма и проектных правил: `Assets/readme.md`
- AI/онбординг/инварианты/дебаг: `docs/ai/README.md`
- “Док ↔ код” маппинг терминов и инвариантов: `docs/ai/SEMANTIC_AUDIT.md`

## Ключевые инварианты (must not break)

- Граф расширяется в edge‑rooms: каждое исходное ребро становится отдельной corridor‑вершиной.
- После расширения валидные связи только `RoomMeta ↔ ConnectorMeta`.
- Стыковка `Room ↔ Connector` поддерживает bite depth: `0 <= X < BiteDepth` у сокета коннектора.
- Carve‑mask (3 дорожки) используется консистентно в CS / layout / placement.

## Runtime entrypoints (из сцены)

- Полный solve+layout+place+stamp в Tilemaps: `Assets/Scripts/Generation/Runtime/GraphMapBuilder.cs`
- Только assignment solver (без layout/stamp): `Assets/Scripts/Generation/Runtime/GraphGenRunner.cs`
- Простая “штамповка по позициям графа” (без layout): `Assets/Scripts/Generation/Runtime/GraphTilemapGenerator.cs`

