package sim

import (
	"testing"

	"pogranichye/core"
)

// comms 0254 §«Этап A» (продолжение task 010) — критерий приёмки: чанк, который физически лежит
// на полосе реки речного/берегового села, получает непустой water с шириной/уровнем/двумя точками
// внутри чанка; далёкий чанк без реки рядом — пустой water, не null (тот же принцип, что и у roads).
func TestTerrainChunkWaterAtRiverSettlement(t *testing.T) {
	w, _, _ := starterPlayer(t)

	var riverID core.ID
	var l *Layout
	w.Each("settlement", func(se *core.Entity) {
		if riverID != 0 {
			return
		}
		if cl := LayoutOf(w, se.ID); cl != nil && cl.HasRiver {
			riverID, l = se.ID, cl
		}
	})
	if riverID == 0 {
		t.Skip("нужно хотя бы одно речное село в стартовом мире")
	}

	s := core.Settle(w.Get(riverID))
	ax, ay := Anchor(w, s)
	midY := (l.RiverY0 + l.RiverY1) / 2
	cx, cz := int64(ax)/ChunkMeters, int64(ay+midY)/ChunkMeters
	if ax < 0 {
		cx--
	}
	if ay+midY < 0 {
		cz--
	}

	c := TerrainChunk(w, cx, cz, 16)
	water, ok := c["water"].([]map[string]any)
	if !ok || len(water) == 0 {
		t.Fatalf("чанк (%d,%d) на полосе реки села %d должен содержать water: %v", cx, cz, riverID, c["water"])
	}
	for _, wd := range water {
		width, _ := wd["width_m"].(float64)
		if width <= 0 {
			t.Fatalf("width_m должен быть положительным: %v", wd)
		}
		if _, ok := wd["water_level_m"].(float64); !ok {
			t.Fatalf("water_level_m должен присутствовать: %v", wd)
		}
		pts, ok := wd["points"].([]map[string]float64)
		if !ok || len(pts) != 2 {
			t.Fatalf("water должен содержать 2 точки (отрезок): %v", wd["points"])
		}
		for _, p := range pts {
			if p["x"] < -1 || p["x"] > ChunkMeters+1 || p["z"] < -1 || p["z"] > ChunkMeters+1 {
				t.Fatalf("точка water вне чанка (локальные координаты): %v", p)
			}
		}
	}

	far := TerrainChunk(w, 100000, 100000, 16)
	farWater, ok := far["water"].([]map[string]any)
	if !ok || len(farWater) != 0 {
		t.Fatalf("далёкий чанк без реки рядом должен иметь пустой water: %v", far["water"])
	}
}

// TestTerrainChunkWaterDeterministicAndOrderIndependent — та же проверка детерминизма, что для
// остальных content-полей (0235 п.1): water — чистая функция (seed, settlement, chunk).
func TestTerrainChunkWaterDeterministicAndOrderIndependent(t *testing.T) {
	w, _, _ := starterPlayer(t)

	var cx, cz int64 = -1, 2
	first := TerrainChunk(w, cx, cz, 16)
	_ = TerrainChunk(w, 5, -4, 16)
	second := TerrainChunk(w, cx, cz, 16)

	fw, _ := first["water"].([]map[string]any)
	sw, _ := second["water"].([]map[string]any)
	if len(fw) != len(sw) {
		t.Fatalf("water не детерминирован между вызовами: %v != %v", fw, sw)
	}
	for i := range fw {
		if fw[i]["id"] != sw[i]["id"] || fw[i]["width_m"] != sw[i]["width_m"] {
			t.Fatalf("water[%d] не детерминирован: %v != %v", i, fw[i], sw[i])
		}
	}
}
