package sim

import (
	"reflect"
	"testing"

	"pogranichye/core"
)

// comms 0235/0254, task 010 — критерии приёмки: детерминизм, независимость от порядка запроса,
// непустой roads там, где он физически должен быть, высоты не регрессируют (heights остаётся).

// TestTerrainContentDeterministicAndOrderIndependent — тот же seed+chunk, запрошенный дважды (в
// т.ч. вперемешку с другим чанком, как A→B→A) даёт побайтово равный content: seed/gen/biome/cover/
// resources/wild/roads — чистая функция (seed,chunk), без скрытого состояния между вызовами.
func TestTerrainContentDeterministicAndOrderIndependent(t *testing.T) {
	w, _, _ := starterPlayer(t)

	aFirst := TerrainChunk(w, -1, 2, 16)
	_ = TerrainChunk(w, 5, -4, 16) // другой чанк между повторами — не должен задевать кэш/состояние
	aSecond := TerrainChunk(w, -1, 2, 16)

	for _, key := range []string{"seed", "gen", "biome", "cover", "resources", "wild", "roads", "water"} {
		if !reflect.DeepEqual(aFirst[key], aSecond[key]) {
			t.Fatalf("content.%s не детерминирован между вызовами: %v != %v", key, aFirst[key], aSecond[key])
		}
	}
}

// TestTerrainContentAdditiveFieldsShapeAndHeightsPreserved — новые поля аддитивны и имеют
// ожидаемую форму; существующее поле heights не регрессирует (та же длина/присутствие, что и до
// этого PR — см. TestTerrainChunkBordersAreDeterministicForSignedCoordinates).
func TestTerrainContentAdditiveFieldsShapeAndHeightsPreserved(t *testing.T) {
	w, _, _ := starterPlayer(t)
	c := TerrainChunk(w, 0, 0, 16)

	hs, ok := c["heights"].([]float64)
	if !ok || len(hs) != (ChunkMeters/16+1)*(ChunkMeters/16+1) {
		t.Fatalf("heights не должен регрессировать от content-дескрипторов: %v", c["heights"])
	}
	if _, ok := c["gen"].(int); !ok || c["gen"].(int) != terrainContentGen {
		t.Fatalf("gen должен быть версией генератора content: %v", c["gen"])
	}
	biome, ok := c["biome"].(string)
	if !ok || biome == "" {
		t.Fatalf("biome должен быть непустой строкой: %v", c["biome"])
	}
	cover, ok := c["cover"].(map[string]float64)
	if !ok {
		t.Fatalf("cover должен быть map[string]float64: %v", c["cover"])
	}
	for _, k := range []string{"forest", "grass", "rock", "wet"} {
		v, has := cover[k]
		if !has || v < 0 || v > 1 {
			t.Fatalf("cover.%s вне [0,1] или отсутствует: %v", k, cover)
		}
	}
	for _, key := range []string{"resources", "wild", "roads", "water"} {
		if _, ok := c[key].([]map[string]any); !ok {
			t.Fatalf("%s должен быть массивом (даже пустым), не null: %T", key, c[key])
		}
	}
}

// TestTerrainChunkRoadCorridorBetweenSettlements — чанк, который физически лежит на отрезке
// RoadGraph между двумя сёлами, должен получить непустой roads с точками внутри чанка; далёкий
// чанк вне сети трактов — пустой roads (0235: «Пустой массив, если тракта нет»).
func TestTerrainChunkRoadCorridorBetweenSettlements(t *testing.T) {
	w, _, _ := starterPlayer(t)
	roads := RoadGraph(w, false) // стартовый котёл — внутренние сёла
	if len(roads) == 0 {
		t.Skip("нужен хотя бы один тракт между стартовыми сёлами")
	}
	rd := roads[0]
	sa, sb := core.Settle(w.Get(rd.A)), core.Settle(w.Get(rd.B))
	ax, ay := Anchor(w, sa)
	bx, by := Anchor(w, sb)
	midE, midN := (ax+bx)/2, (ay+by)/2
	cx, cz := int64(midE)/ChunkMeters, int64(midN)/ChunkMeters
	if midE < 0 {
		cx--
	}
	if midN < 0 {
		cz--
	}

	c := TerrainChunk(w, cx, cz, 16)
	roadsOut, _ := c["roads"].([]map[string]any)
	if len(roadsOut) == 0 {
		t.Fatalf("чанк (%d,%d) на середине тракта %d-%d должен содержать road-корридор", cx, cz, rd.A, rd.B)
	}
	for _, r := range roadsOut {
		pts, ok := r["points"].([]map[string]float64)
		if !ok || len(pts) != 2 {
			t.Fatalf("road должен содержать 2 точки (отрезок), получили: %v", r["points"])
		}
		for _, p := range pts {
			if p["x"] < -1 || p["x"] > ChunkMeters+1 || p["z"] < -1 || p["z"] > ChunkMeters+1 {
				t.Fatalf("точка road вне чанка (локальные координаты): %v", p)
			}
		}
	}

	// Далёкий чанк (заведомо вне сети трактов этого мира) — пустой roads, не null.
	far := TerrainChunk(w, 100000, 100000, 16)
	farRoads, ok := far["roads"].([]map[string]any)
	if !ok || len(farRoads) != 0 {
		t.Fatalf("далёкий чанк без трактов должен иметь пустой roads: %v", far["roads"])
	}
}
