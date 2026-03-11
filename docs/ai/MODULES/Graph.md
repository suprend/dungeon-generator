# Graph decomposition (faces & chains / разложение графа)

Код:
- `Assets/Scripts/Generation/Graph/MapGraphFaceBuilder.cs`
- `Assets/Scripts/Generation/Graph/MapGraphChainBuilder.cs`

## Зачем это нужно

Layout решается инкрементально по chain’ам. Chain’ы упорядочены так, чтобы:
- циклы/face’ы (faces) обрабатывались раньше (более жёсткие ограничения → раньше отсеиваем тупики),
- оставшиеся ацикличные части добавлялись после.

## Faces (грани)

`MapGraphFaceBuilder` строит представление граней (faces) на основе планарного вложения (planar embedding).

## Chains (цепочки)

`MapGraphChainBuilder` превращает face’ы в cycle‑chains (только простые циклы / simple cycles), а затем строит path‑chains из оставшихся рёбер.
Также он добавляет singleton‑chains для изолированных вершин, чтобы генерация не “теряла” disconnected nodes.

Практически это значит:
- chain почти всегда **линейная** структура:
  - path,
  - simple cycle,
  - singleton,
- внутри одной chain обычно нет произвольного ветвления,
- поэтому основная сложность layout‑поиска чаще возникает:
  - в стыковке chain с уже размещёнными частями,
  - в замыкании cycle,
  - в topology‑aware выборе стартовых кандидатов,
  - а не в “общем CSP‑порядке” узлов внутри chain.

Как именно строятся chain’ы:
- сначала каждый простой face превращается в `cycle-chain`,
- затем оставшиеся рёбра обходятся как максимальные degree‑`<=2` path‑chain’ы,
- если после этого остаются замкнутые компоненты со степенью `2`, они тоже становятся `cycle-chain`.
