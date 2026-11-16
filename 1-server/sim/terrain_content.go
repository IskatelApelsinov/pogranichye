package sim

import (
	"math"
	"strconv"

	"pogranichye/core"
)

// comms 0235 (Grok) / 0254 §«Этап A» (ChatGPT), task 010 — компактные ДЕТЕРМИНИРОВАННЫЕ
// descriptor-поля местности МЕЖДУ поселениями для /api/terrain: без них клиент честно рисует
// «зелёную/бурую плоскость до горизонта» между редкими anchors — это пустой descriptor, не баг
// рендера (0235). Всё здесь — чистая функция (seed, chunk) → descriptors, НЕ authoritative game
// state и НЕ новые серверные Entity на каждый куст (0235 п.6): cover/resources — только подсказка
// для визуального scatter клиента; единственное исключение — "wild", который лишь witness'ит уже
// существующие WildSiteC (сервер и так ими владеет через /api/world.wildsites — здесь не выдаём
// developer manifest за карту знаний персонажа, тот же набор полей, что уже публичен).
//
// water (0254 Этап A, продолжение task 010): НЕ изобретаем отдельную глобальную гидрологию —
// единственная модель реки, что уже есть у сервера, это локальная река поселения (Layout.HasRiver/
// RiverY0/RiverY1/BridgeX0/BridgeX1, layout_gen.go), которую клиент и так рисует в сцене села.
// chunkWater проецирует ЭТУ ЖЕ полосу в мировые координаты (якорь + локальная точка, как и
// остальные дескрипторы здесь) и подрезает её границей чанка — так видно воду у берегового/речного
// села и за пределами его сцены, честно НЕ утверждая непрерывную реку между сёлами, которой в
// модели пока нет (это следующий отдельный шаг, если понадобится).
//
// Не в этом PR (сознательно): kind-различение road|trail|bridge|pass (0254) — сейчас все перегоны
// RoadGraph помечены просто "road". Отдельный маленький PR после обкатки этого слоя (см. comms-ответ).

const terrainContentGen = 1 // версия формулы content-дескрипторов — bump при смене алгоритма

// biomeProfile — базовые плотности покрова {forest, grass, rock, wet} по биому региона (RegionC.Biome,
// core/components.go) — те же пять биомов, что уже использует worldgen/wildsites.
var biomeProfile = map[string][4]float64{
	"forest": {0.55, 0.30, 0.05, 0.10},
	"plain":  {0.10, 0.75, 0.05, 0.10},
	"hills":  {0.25, 0.40, 0.30, 0.05},
	"coast":  {0.15, 0.35, 0.10, 0.40},
	"steppe": {0.02, 0.85, 0.08, 0.05},
}

func coverProfile(biome string) [4]float64 {
	if p, ok := biomeProfile[biome]; ok {
		return p
	}
	return [4]float64{0.20, 0.60, 0.10, 0.10}
}

// chunkCover — плотности покрова для чанка: биомный профиль + мягкий детерминированный перекос
// лес/трава от шума того же Terrain.seed, так что соседние чанки одного биома не идентичны
// побайтово, но меняются непрерывно (нет рваных швов на границе чанков).
func chunkCover(t *Terrain, biome string, oe, on float64) map[string]float64 {
	base := coverProfile(biome)
	cx, cy := oe+ChunkMeters/2, on+ChunkMeters/2
	shift := (noise(t.seed+41, cx, cy, 900) - 0.5) * 0.3
	forest := clampf(base[0]+shift, 0, 1)
	grass := clampf(base[1]-shift, 0, 1)
	return map[string]float64{
		"forest": math.Round(forest*100) / 100,
		"grass":  math.Round(grass*100) / 100,
		"rock":   math.Round(base[2]*100) / 100,
		"wet":    math.Round(base[3]*100) / 100,
	}
}

// resourceKindsByBiome — какие ресурсные дескрипторы правдоподобны для биома: ТОЛЬКО визуальная
// подсказка, где клиенту правдоподобно нарисовать заготовки/скопления — не меняет серверный
// Store/harvest-проверку (та остаётся отдельной, как и раньше: GatherWood/рубка и т.п.).
var resourceKindsByBiome = map[string][]string{
	"forest": {"wood", "wood", "berries"},
	"hills":  {"stone", "wood"},
	"plain":  {"berries", "wood"},
	"coast":  {"berries"},
	"steppe": {"stone"},
}

// chunkResources — 0..2 редкие точки-дескриптора на чанк, из решётки lattice() по (seed, chunk,
// slot) — детерминировано, не создаёт Entity (0235 п.6).
func chunkResources(t *Terrain, biome string, cx, cz int64) []map[string]any {
	kinds := resourceKindsByBiome[biome]
	if len(kinds) == 0 {
		kinds = []string{"wood"}
	}
	out := []map[string]any{}
	for slot := 0; slot < 2; slot++ {
		gx, gy := cx*2+int64(slot), cz*2+int64(slot)*3+1
		roll := lattice(t.seed+71, gx, gy)
		if roll < 0.45 { // большинство слотов пусты — не засоряем каждый чанк
			continue
		}
		hx := lattice(t.seed+72, gx, gy)
		hz := lattice(t.seed+73, gx, gy)
		kindRoll := lattice(t.seed+74, gx, gy)
		kind := kinds[int(kindRoll*float64(len(kinds)))%len(kinds)]
		out = append(out, map[string]any{
			"id":   "res-" + i64(cx) + "-" + i64(cz) + "-" + i64(int64(slot)),
			"kind": kind, "x": r3(hx * ChunkMeters), "z": r3(hz * ChunkMeters),
			"count": 1 + int(roll*3)%3,
		})
	}
	return out
}

// chunkWild — уже существующие WildSiteC, чей центр попал в этот чанк (мировые метры из тех же
// формул, что WildSitesView использует для /api/world.wildsites) — не изобретаем новые дикие
// места здесь, только witness то, что уже публично известно серверу.
func chunkWild(w *core.World, oe, on float64) []map[string]any {
	p := EnsurePlanet(w)
	out := []map[string]any{}
	w.Each("wildsite", func(e *core.Entity) {
		ws := WildSite(e)
		if ws == nil {
			return
		}
		ex, ny := (ws.X-500)*p.KmPerUnit*1000, (ws.Y-500)*p.KmPerUnit*1000
		if ex < oe || ex >= oe+ChunkMeters || ny < on || ny >= on+ChunkMeters {
			return
		}
		out = append(out, map[string]any{
			"id": eidStr(e.ID), "kind": ws.Type, "x": r3(ex - oe), "z": r3(ny - on), "radius_m": 20.0,
		})
	})
	return out
}

// chunkRoads — отрезки RoadGraph (оба склона — внутренний котёл и внешний мир), подрезанные
// (clipSegmentToBox) границей чанка; пустой массив, если ни один тракт чанк не касается (0235).
func chunkRoads(w *core.World, oe, on float64) []map[string]any {
	out := []map[string]any{}
	for _, outer := range [2]bool{false, true} {
		for _, rd := range RoadGraph(w, outer) {
			ea, eb := w.Get(rd.A), w.Get(rd.B)
			if ea == nil || eb == nil {
				continue
			}
			sa, sb := core.Settle(ea), core.Settle(eb)
			if sa == nil || sb == nil {
				continue
			}
			ax, ay := Anchor(w, sa)
			bx, by := Anchor(w, sb)
			cx0, cy0, cx1, cy1, ok := clipSegmentToBox(ax, ay, bx, by, oe, on, ChunkMeters)
			if !ok {
				continue
			}
			out = append(out, map[string]any{
				"id": "road-" + eidStr(rd.A) + "-" + eidStr(rd.B), "kind": "road",
				"points": []map[string]float64{
					{"x": r3(cx0 - oe), "z": r3(cy0 - on)},
					{"x": r3(cx1 - oe), "z": r3(cy1 - on)},
				},
			})
		}
	}
	return out
}

// chunkWater — полоса реки поселения (Layout.HasRiver), спроецированная в мировые координаты
// (Anchor + локальная точка — та же связь, что использует WorldPosOf) и подрезанная границей
// чанка. Пустой массив, если ни одно речное село чанк не касается. water_level_m — высота земли
// в середине полосы минус небольшая глубина (сервер не хранит отдельный уровень воды, честная
// оценка от уже существующего Terrain.Height, не придуманная гидрология).
func chunkWater(w *core.World, t *Terrain, oe, on float64) []map[string]any {
	out := []map[string]any{}
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil {
			return
		}
		l := LayoutOf(w, se.ID)
		if l == nil || !l.HasRiver {
			return
		}
		ax, ay := Anchor(w, s)
		midY := (l.RiverY0 + l.RiverY1) / 2
		x0, y0 := ax-l.Half, ay+midY
		x1, y1 := ax+l.Half, ay+midY
		cx0, cy0, cx1, cy1, ok := clipSegmentToBox(x0, y0, x1, y1, oe, on, ChunkMeters)
		if !ok {
			return
		}
		width := math.Abs(l.RiverY1 - l.RiverY0)
		level := t.Height(ax, ay+midY) - 0.3
		out = append(out, map[string]any{
			"id": "water-" + eidStr(se.ID), "kind": "river",
			"width_m": r3(width), "water_level_m": r3(level),
			"points": []map[string]float64{
				{"x": r3(cx0 - oe), "z": r3(cy0 - on)},
				{"x": r3(cx1 - oe), "z": r3(cy1 - on)},
			},
		})
	})
	return out
}

// clipSegmentToBox — пересечение отрезка (x0,y0)-(x1,y1) с осепараллельным квадратом
// [bx0,bx0+size)×[by0,by0+size) (алгоритм Лианга-Барски). ok=false — отрезок чанк не касается.
func clipSegmentToBox(x0, y0, x1, y1, bx0, by0, size float64) (rx0, ry0, rx1, ry1 float64, ok bool) {
	bx1, by1 := bx0+size, by0+size
	dx, dy := x1-x0, y1-y0
	tmin, tmax := 0.0, 1.0
	clip := func(p, q float64) bool {
		if p == 0 {
			return q >= 0
		}
		r := q / p
		if p < 0 {
			if r > tmax {
				return false
			}
			if r > tmin {
				tmin = r
			}
		} else {
			if r < tmin {
				return false
			}
			if r < tmax {
				tmax = r
			}
		}
		return true
	}
	if !clip(-dx, x0-bx0) || !clip(dx, bx1-x0) || !clip(-dy, y0-by0) || !clip(dy, by1-y0) || tmin > tmax {
		return 0, 0, 0, 0, false
	}
	return x0 + dx*tmin, y0 + dy*tmin, x0 + dx*tmax, y0 + dy*tmax, true
}

func i64(v int64) string { return strconv.FormatInt(v, 10) }
