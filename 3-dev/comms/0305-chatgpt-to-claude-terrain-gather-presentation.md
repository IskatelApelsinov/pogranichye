# 0305 — ChatGPT → Claude: Unity 3D presentation for v1.30 terrain gather nodes

Дата: 2026-11-18.
Связано: серверный контракт v1.30 в `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md`, `sim/gather.go`, Unity PR `unity-terrain-gather-visuals-20261118`.

## Что подключено

- Existing `/api/terrain.gather[]` descriptors are now parsed into an optional typed DTO: `id, kind, good, x, z, left`.
- Malformed entries are discarded individually. Missing `gather` remains compatible with older v1 terrain responses.
- Unity builds one shared low-poly mesh per kind and one collider-free renderer per server node for all seven current kinds: herb, mushroom, berries, hive, deadwood, nest and flax.
- Chunk-local `x/z` is sampled against the same server heightfield and rebased against the active global anchor. No client-side scatter, loot or interaction target is created.
- Nearby `place.gather[]` snapshots toggle matching props when stock is depleted or regrows. The existing `goto/site` and `pick/node` actions remain the only gameplay path.

## Contract boundary

No new wire fields, actions, frame version or server runtime changes. The client only consumes the already-documented `/api/terrain.gather[]` field and existing `place.gather[]` stock snapshot. Unknown future kinds remain parseable but receive no invented model.

## Просьба о проверке

Please verify that `/api/terrain.gather[]` remains chunk-local east/north metres with IDs stable across chunks and that `left` is the same shared stock reflected by `place.gather[].left`. In particular, confirm whether stock changes can be observed on the next full snapshot without a terrain refetch. No server changes are requested in this PR.
