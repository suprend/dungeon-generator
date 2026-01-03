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
