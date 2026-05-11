# Dungeon Generator

Unity-проект с генератором 2D-подземелий по дизайнерскому графу комнат.

Генератор принимает `MapGraphAsset`, где вершины описывают логические комнаты, а ребра — типы соединений между ними. На выходе он подбирает подходящие префабы комнат и коридоров, раскладывает их на дискретной Tilemap-сетке без недопустимых пересечений и штампует итоговый уровень в целевые Tilemap-слои.

## Что уже есть

- графовый редактор: `Window/Generation/Map Graph Editor`;
- ScriptableObject-типы комнат и соединений: `RoomTypeAsset`, `ConnectionTypeAsset`;
- модульные префабы комнат и коридоров с `RoomMeta`, `ConnectorMeta` и `DoorSocket`;
- layout-генератор на дискретной геометрии Tilemap;
- рантайм-сборка уровня через `GraphMapBuilder`;
- отладочные настройки для seed, retry, таймаута, профилирования layout и диагностики неудачных раскладок.

## Как устроен генератор

Основной entrypoint — `GraphMapBuilder` на сцене. Он берет `MapGraphAsset`, целевой `Grid`, `floorMap` и `wallMap`, после чего запускает `MapGraphLevelSolver`.

Внутри пайплайн выглядит так:

1. Исходный граф расширяется: каждое ребро превращается в отдельную corridor-вершину. После этого layout работает только со связями вида `RoomMeta <-> ConnectorMeta`.
2. Для всех префабов строится дискретная форма: клетки пола, стены и сокеты дверей. Этим занимается `ShapeLibrary`, читая дочерние Tilemap-слои префаба.
3. `ConfigurationSpaceLibrary` заранее считает допустимые смещения между парами модулей с учетом сокетов, bite depth и пересечений.
4. `MapGraphFaceBuilder` и `MapGraphChainBuilder` разбивают граф на цепочки, чтобы layout можно было собирать по частям.
5. `MapGraphLayoutGenerator` ищет размещение комнат и коридоров. Основной поиск использует configuration spaces, stack search и simulated annealing; для больших наборов кандидатов есть rejection sampling и conflict-driven выбор проблемных узлов.
6. `PlacementState` валидирует найденный layout, инстанцирует префабы, соединяет конкретные сокеты и готовит список сгенерированных комнат.
7. `TileStampService` переносит тайлы из размещенных модулей в общие `floorMap` и `wallMap`; временные инстансы можно уничтожить после штамповки.

## Граф и данные

`MapGraphAsset` хранит список комнат и связей. У вершины есть label, `RoomTypeAsset` и заметки для дизайнера. У ребра есть `ConnectionTypeAsset`, который задает набор возможных connector-префабов.

`RoomTypeAsset` и `ConnectionTypeAsset` содержат списки prefab-вариантов. Это позволяет одному и тому же логическому типу комнаты или перехода иметь несколько визуальных/геометрических реализаций.

## Требования к префабам

Префаб комнаты должен иметь `RoomMeta`, префаб соединения — `ConnectorMeta`. Оба наследуются от `ModuleMetaBase` и используют дочерние `DoorSocket` для точек стыковки.

Геометрия префаба читается из дочерних Tilemap-слоев:

- слой с `floor` в имени считается полом;
- слой с `wall` в имени считается стенами;
- клетки сокетов считаются проходными для layout, а фактическое прорезание двери происходит при размещении.

Для connector-сокетов важен `BiteDepth`: он задает, насколько комната может "вкусить" внутрь перехода при стыковке. Это помогает соединять комнаты и коридоры без лишних зазоров на тайловой сетке.

## Запуск в сцене

1. Создать или выбрать `MapGraphAsset`.
2. Заполнить `RoomTypeAsset` и `ConnectionTypeAsset` prefab-вариантами.
3. На объект сцены добавить `GraphMapBuilder`.
4. Указать `graph`, `targetGrid`, `floorMap` и `wallMap`.
5. Включить `runOnStart` или вызвать контекстное меню `Build From Graph`.

`randomSeed = 0` выбирает случайный seed. Ненулевой seed делает генерацию воспроизводимой. `layoutRetries` задает количество попыток layout, а `layoutTimeLimitSeconds` ограничивает время одной попытки.

## Полезные файлы

- `Assets/Scripts/Generation/Runtime/GraphMapBuilder.cs` — компонент запуска генерации из сцены.
- `Assets/Scripts/Generation/Solve/MapGraphLevelSolver.*.cs` — расширение графа, подготовка assignments, layout workflow и placement.
- `Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.*.cs` — поиск раскладки.
- `Assets/Scripts/Generation/Geometry/ShapeLibrary.cs` — извлечение дискретной формы из prefab-модулей.
- `Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.*.cs` — расчет допустимых смещений между модулями.
- `Assets/Scripts/Generation/Tilemap/TileStampService.cs` — перенос тайлов в итоговые Tilemap-слои.
- `Assets/Editor/MapGraphEditorWindow.cs` — редактор графа.

