package sim

import (
	"math"
	"sort"
	"strconv"

	"pogranichye/core"
	"pogranichye/dice"
)

// Глобальные координаты мира (схема «yof-tangent», версия 1).
//
// У любой точки мира — в селе, в поле между сёлами, на башне, в подвале — одно каноническое
// положение WorldPos: кадр карты (Frame) и три числа в МИЛЛИМЕТРАХ: на восток, на север и вверх.
// Целые миллиметры выбраны вместо float: точность одинакова у котла и за тысячи километров от него,
// сравнение и хеш точны, а int64 хватает на расстояния далеко за пределами планеты.
//
//   - Frame — номер касательной плоскости. Кадр 0 привязан к центру «котла»: азимутальная
//     равноудалённая проекция сферы Ёф (planet_coords.go). Она точна вокруг своей опоры и
//     расползается к антиподу, поэтому дальние материки получат собственные кадры (1, 2, …) со
//     своими опорами. Поле заведено сразу, чтобы формат не менять.
//   - E, N — метры на плоскости кадра (хранятся в мм).
//   - U — высота над уровнем моря (мм): высота земли в этой точке плюс высота тела над землёй.
//     Башня — положительная добавка, подвал и подземелье — отрицательная.
//
// Чанк — квадрат ChunkMeters×ChunkMeters на плоскости кадра; индексы знаковые, деление — с
// округлением вниз (точка −1 м лежит в чанке −1, не 0). По высоте мир режется на слои LayerMeters:
// поверхность, башни и подземелья одной клетки адресуются порознь. Чанк — единица адресации,
// потоковой подгрузки и сохранения, а не стена: пересечение границы ничего не меняет в сущности.
//
// Старые координаты никуда не делись и значат то же: ScenePosC.X/Y — метры от центра СВОЕГО села.
// Связь одна: мир = якорь села + локальная точка. Якорь выводится из SettlementC.X/Y теми же
// формулами, что и широта/долгота места (SiteC), так что три представления согласованы.

const (
	WorldScheme        = "yof-tangent"
	WorldSchemeVersion = 1
	ChunkMeters        = 256 // сторона чанка, м
	LayerMeters        = 64  // толщина слоя по высоте, м
)

// WorldPos — каноническое положение в мире. Единицы — миллиметры.
type WorldPos struct {
	Frame uint16
	E     int64
	N     int64
	U     int64
}

// WorldPosMeters собирает положение из метров (округление до миллиметра).
func WorldPosMeters(frame uint16, e, n, u float64) WorldPos {
	return WorldPos{frame, int64(math.Round(e * 1000)), int64(math.Round(n * 1000)), int64(math.Round(u * 1000))}
}

// Meters — положение в метрах.
func (p WorldPos) Meters() (e, n, u float64) {
	return float64(p.E) / 1000, float64(p.N) / 1000, float64(p.U) / 1000
}

// Chunk — индексы чанка на плоскости кадра.
func (p WorldPos) Chunk() (cx, cz int64) {
	return floorDiv(p.E, ChunkMeters*1000), floorDiv(p.N, ChunkMeters*1000)
}

// Layer — номер слоя по высоте (0 — от уровня моря до LayerMeters, −1 — первый слой под ним).
func (p WorldPos) Layer() int64 { return floorDiv(p.U, LayerMeters*1000) }

// InChunk — смещение внутри своего чанка, метры, всегда в [0, ChunkMeters).
func (p WorldPos) InChunk() (x, z float64) {
	return float64(floorMod(p.E, ChunkMeters*1000)) / 1000, float64(floorMod(p.N, ChunkMeters*1000)) / 1000
}

// Add — сдвиг на метры.
func (p WorldPos) Add(de, dn, du float64) WorldPos {
	return WorldPos{p.Frame, p.E + int64(math.Round(de*1000)), p.N + int64(math.Round(dn*1000)), p.U + int64(math.Round(du*1000))}
}

// ChunkOrigin — юго-западный угол чанка (метры на плоскости кадра).
func ChunkOrigin(cx, cz int64) (e, n float64) {
	return float64(cx * ChunkMeters), float64(cz * ChunkMeters)
}

// ChunkID — устойчивое имя чанка: «кадр:cx:cz».
func ChunkID(frame uint16, cx, cz int64) string {
	return strconv.Itoa(int(frame)) + ":" + strconv.FormatInt(cx, 10) + ":" + strconv.FormatInt(cz, 10)
}

// WorldDist — расстояние по земле между двумя точками одного кадра, метры (ok=false — кадры разные).
func WorldDist(a, b WorldPos) (float64, bool) {
	if a.Frame != b.Frame {
		return 0, false
	}
	return math.Hypot(float64(a.E-b.E), float64(a.N-b.N)) / 1000, true
}

func floorMod(a, b int64) int64 {
	r := a % b
	if r != 0 && ((r < 0) != (b < 0)) {
		r += b
	}
	return r
}

// --- якоря поселений ---

// Anchor — положение центра поселения на плоскости кадра 0, метры. Те же формулы, что дают
// широту и долготу места (applySiteCoordinates): карта мира → метры от центра котла.
func Anchor(w *core.World, s *core.SettlementC) (e, n float64) {
	p := EnsurePlanet(w)
	return (s.X - 500) * p.KmPerUnit * 1000, (s.Y - 500) * p.KmPerUnit * 1000
}

// --- высота земли ---

// Terrain — рельеф мира: высота земли в любой точке плоскости кадра 0. Детерминирован от семени.
// Собирается на один запрос (помнит якоря сёл); между запросами ничего не хранит.
type Terrain struct {
	seed    uint64
	rimR    float64 // радиус гребня гор вокруг котла, м
	anchors [][3]float64
}

const (
	siteFlatR  = 230.0 // на этом радиусе от центра села земля ровная (сцена села плоская)
	siteBlendR = 700.0 // к этому радиусу высота плавно переходит в природный рельеф
)

// NewTerrain готовит рельеф мира.
func NewTerrain(w *core.World) *Terrain {
	t := &Terrain{seed: dice.Seed64(w.Seed, "terrain")}
	far := 0.0
	type an struct {
		e, n  float64
		inner bool
	}
	var as []an
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		e, n := Anchor(w, s)
		in := !isOuter(w, s)
		as = append(as, an{e, n, in})
		if in {
			far = math.Max(far, math.Hypot(e, n))
		}
	})
	t.rimR = far + 9000 // гребень — в девяти километрах за самым дальним селом котла
	for _, a := range as {
		t.anchors = append(t.anchors, [3]float64{a.e, a.n, t.raw(a.e, a.n)})
	}
	return t
}

func lattice(seed uint64, x, y int64) float64 {
	h := seed ^ (uint64(x) * 0x9E3779B97F4A7C15) ^ (uint64(y) * 0xC2B2AE3D27D4EB4F)
	h ^= h >> 33
	h *= 0xFF51AFD7ED558CCD
	h ^= h >> 33
	h *= 0xC4CEB9FE1A85EC53
	h ^= h >> 33
	return float64(h>>11) / float64(1<<53) // [0,1)
}

// noise — гладкий шум: значения на решётке с шагом cell метров, между узлами — плавная интерполяция.
func noise(seed uint64, x, y, cell float64) float64 {
	fx, fy := x/cell, y/cell
	x0, y0 := math.Floor(fx), math.Floor(fy)
	tx, ty := fx-x0, fy-y0
	tx, ty = tx*tx*(3-2*tx), ty*ty*(3-2*ty)
	ix, iy := int64(x0), int64(y0)
	a, b := lattice(seed, ix, iy), lattice(seed, ix+1, iy)
	c, d := lattice(seed, ix, iy+1), lattice(seed, ix+1, iy+1)
	return (a+(b-a)*tx)*(1-ty) + (c+(d-c)*tx)*ty
}

// raw — природная высота без площадок под сёлами: холмы трёх масштабов и горный гребень котла.
func (t *Terrain) raw(e, n float64) float64 {
	h := 120.0 // дно котла — над уровнем моря
	h += (noise(t.seed, e, n, 9000) - 0.5) * 160
	h += (noise(t.seed+1, e, n, 2200) - 0.5) * 46
	h += (noise(t.seed+2, e, n, 480) - 0.5) * 9
	// гребень: кольцо гор вокруг котла с неровной кромкой
	d := (math.Hypot(e, n) - t.rimR) / 4200
	ridge := math.Exp(-d * d)
	h += ridge * (850 + (noise(t.seed+3, e, n, 3000)-0.5)*500)
	return h
}

// Height — высота земли в точке (метры над уровнем моря). Под каждым селом — ровная площадка на
// высоте его центра; дальше земля плавно возвращается к природному рельефу.
func (t *Terrain) Height(e, n float64) float64 {
	h := t.raw(e, n)
	best, bh := math.MaxFloat64, 0.0
	for _, a := range t.anchors {
		if d := math.Hypot(e-a[0], n-a[1]); d < best {
			best, bh = d, a[2]
		}
	}
	if best >= siteBlendR {
		return h
	}
	if best <= siteFlatR {
		return bh
	}
	k := (best - siteFlatR) / (siteBlendR - siteFlatR)
	k = k * k * (3 - 2*k)
	return bh + (h-bh)*k
}

// --- положение сущности ---

// WorldPosOf — каноническое положение тела: якорь его села + место в сцене; в пути — точка на
// отрезке между сёлами по доле пройденного. ok=false — у сущности нет места в мире.
func WorldPosOf(w *core.World, t *Terrain, e *core.Entity) (WorldPos, bool) {
	if e == nil {
		return WorldPos{}, false
	}
	if t == nil {
		t = NewTerrain(w)
	}
	if j := journeyOf(e); j != nil {
		a, b := settleOf(w, j.From), settleOf(w, j.To)
		if a == nil || b == nil {
			return WorldPos{}, false
		}
		ae, an := Anchor(w, a)
		be, bn := Anchor(w, b)
		k := 0.0
		if j.Total > 0 {
			k = clampf(j.Done/j.Total, 0, 1)
		}
		x, y := ae+(be-ae)*k, an+(bn-an)*k
		return WorldPosMeters(0, x, y, t.Height(x, y)), true
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 {
		return WorldPos{}, false
	}
	s := settleOf(w, pl.Settlement)
	if s == nil {
		return WorldPos{}, false
	}
	x, y := Anchor(w, s)
	z := 0.0
	if p := ScenePos(e); p != nil {
		x, y, z = x+p.X, y+p.Y, p.Z
	}
	return WorldPosMeters(0, x, y, t.Height(x, y)+z), true
}

// SurfaceOf — широта и долгота точки кадра 0 на сфере Ёф.
func SurfaceOf(w *core.World, p WorldPos) (lat, lon float64, ok bool) {
	pl := EnsurePlanet(w)
	e, n, u := p.Meters()
	sp, ok := PlanetProjection{Origin: SurfacePoint{LatitudeDeg: pl.CauldronLat, LongitudeDeg: pl.CauldronLon},
		RadiusMeters: pl.RadiusKm * 1000}.Inverse(LocalMeters{East: e, North: n})
	_ = u
	return sp.LatitudeDeg, sp.LongitudeDeg, ok
}

func r3(v float64) float64 { return math.Round(v*1000) / 1000 }

// WorldPosView — положение игрока для кадра (me.wpos). Метры с точностью до миллиметра.
func WorldPosView(w *core.World, e *core.Entity) any {
	t := NewTerrain(w)
	p, ok := WorldPosOf(w, t, e)
	if !ok {
		return nil
	}
	x, y, u := p.Meters()
	cx, cz := p.Chunk()
	ground := t.Height(x, y)
	out := map[string]any{"scheme": WorldScheme, "v": WorldSchemeVersion, "frame": p.Frame,
		"e": r3(x), "n": r3(y), "u": r3(u), "ground": r3(ground), "z": r3(u - ground),
		"chunk": []int64{cx, cz}, "layer": p.Layer()}
	if lat, lon, ok := SurfaceOf(w, p); ok {
		out["lat"], out["lon"] = math.Round(lat*1e5)/1e5, math.Round(lon*1e5)/1e5
	}
	return out
}

// AnchorView — якорь поселения для клиента (place.anchor и /api/layout): мир = якорь + me.pos.
func AnchorView(w *core.World, settlement core.ID) any {
	s := settleOf(w, settlement)
	if s == nil {
		return nil
	}
	e, n := Anchor(w, s)
	return map[string]any{"frame": 0, "e": r3(e), "n": r3(n), "u": r3(NewTerrain(w).Height(e, n))}
}

// WorldManifest — служебная карта мира (/api/world): схема координат, якоря поселений, тракты.
// Это знание разработчика и потокового клиента, а не игрока: имена сёл игрок узнаёт в мире.
func WorldManifest(w *core.World) map[string]any {
	p := EnsurePlanet(w)
	t := NewTerrain(w)
	var sets []map[string]any
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		e, n := Anchor(w, s)
		site := SiteOf(w, se)
		l := LayoutOf(w, se.ID)
		sets = append(sets, map[string]any{"id": eidStr(se.ID), "frame": 0, "e": r3(e), "n": r3(n), "u": r3(t.Height(e, n)),
			"tier": s.Tier, "terrain": site.Terrain, "plan": l.Plan, "cauldron": site.Cauldron,
			"half": l.Half, "layout": "/api/layout?settlement=" + eidStr(se.ID)})
	})
	roads := []map[string]any{}
	for _, outer := range []bool{false, true} {
		for _, r := range RoadGraph(w, outer) {
			roads = append(roads, map[string]any{"a": eidStr(r.A), "b": eidStr(r.B)})
		}
	}
	return map[string]any{"scheme": WorldScheme, "v": WorldSchemeVersion, "units": "m",
		"chunk_m": ChunkMeters, "layer_m": LayerMeters, "seamless": p.Seamless,
		"frames": []map[string]any{{"id": 0, "projection": "azimuthal-equidistant",
			"origin_lat": p.CauldronLat, "origin_lon": p.CauldronLon, "radius_m": p.RadiusKm * 1000}},
		"rim_radius_m": r3(t.rimR), "settlements": sets, "roads": roads}
}

// TerrainChunk — высоты земли одного чанка (/api/terrain): сетка (n+1)×(n+1) с шагом step метров,
// строки с юга на север, в строке — с запада на восток. Соседние чанки делят крайние отсчёты, поэтому
// швов на стыке нет. Здесь же — что стоит в чанке: центры поселений.
func TerrainChunk(w *core.World, cx, cz int64, step int) map[string]any {
	if step != 8 && step != 16 && step != 32 && step != 64 {
		step = 16
	}
	t := NewTerrain(w)
	oe, on := ChunkOrigin(cx, cz)
	n := ChunkMeters / step
	hs := make([]float64, 0, (n+1)*(n+1))
	for j := 0; j <= n; j++ {
		for i := 0; i <= n; i++ {
			hs = append(hs, math.Round(t.Height(oe+float64(i*step), on+float64(j*step))*100)/100)
		}
	}
	sets := []map[string]any{}
	w.Each("settlement", func(se *core.Entity) {
		e, nn := Anchor(w, core.Settle(se))
		if e >= oe && e < oe+ChunkMeters && nn >= on && nn < on+ChunkMeters {
			sets = append(sets, map[string]any{"id": eidStr(se.ID), "e": r3(e), "n": r3(nn)})
		}
	})
	return map[string]any{"id": ChunkID(0, cx, cz), "frame": 0, "chunk": []int64{cx, cz}, "origin": map[string]float64{"e": oe, "n": on},
		"size_m": ChunkMeters, "step_m": step, "side": n + 1, "heights": hs, "settlements": sets}
}

// --- бесшовный мир: земли сёл ---

// Бесшовность на сервере. Весь край поделён на ЗЕМЛИ сёл: точка принадлежит тому селу, чей центр
// ближе. Тело хранит место относительно центра своего села (ScenePosC), и когда оно уходит так
// далеко, что ближе становится центр соседа, сервер переносит его в земли соседа: меняет село в
// PlaceC и пересчитывает локальные координаты от нового центра. Мировое положение при этом не
// меняется ни на миллиметр, id, соединение и всё состояние — те же. Игрок ничего не «загружает»:
// он просто идёт. Край сцены села перестаёт быть стеной (Layout.Open).
//
// Режим включается на мир (PlanetC.Seamless) и хранится в сохранении. Пока он выключен, всё
// работает по-старому: край земли — стена, выезд на тракт — у края (StepExits).

const landHysteresis = 40.0 // м — насколько ближе должен стать сосед, чтобы сменить земли (без дребезга)

// SetSeamless включает или выключает бесшовный мир.
func SetSeamless(w *core.World, on bool) { EnsurePlanet(w).Seamless = on }

// StepLands — перенос идущих игроков в земли ближайшего села (только в бесшовном мире).
func StepLands(w *core.World, controlled func(core.ID) bool) {
	if !Seamless(w) {
		return
	}
	type an struct {
		id    core.ID
		e, n  float64
		outer bool
	}
	var as []an
	var walkers []*core.Entity
	w.Each("player", func(e *core.Entity) {
		if controlled != nil && !controlled(e.ID) {
			return
		}
		if p := ScenePos(e); p != nil && !OnRoad(e) && core.Place(e) != nil && core.Place(e).Settlement != 0 {
			// в пределах своей сцены соседей ближе не бывает — не тратим время
			sid := core.Place(e).Settlement
			if l := LayoutOf(w, sid); math.Abs(p.X) > l.Half || math.Abs(p.Y) > l.Half {
				walkers = append(walkers, e)
			} else if !knowsPlace(e, sid) {
				LearnVisited(w, e, sid) // дошёл до самого села ногами — теперь знаешь его
			}
		}
	})
	if len(walkers) == 0 {
		return
	}
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		e, n := Anchor(w, s)
		as = append(as, an{se.ID, e, n, isOuter(w, s)})
	})
	sort.Slice(as, func(i, j int) bool { return as[i].id < as[j].id })
	for _, e := range walkers {
		pl, p := core.Place(e), ScenePos(e)
		var cur *an
		for i := range as {
			if as[i].id == pl.Settlement {
				cur = &as[i]
			}
		}
		if cur == nil {
			continue
		}
		wx, wy := cur.e+p.X, cur.n+p.Y
		dc := math.Hypot(p.X, p.Y)
		best, bd := cur, dc
		for i := range as {
			if as[i].outer != cur.outer {
				continue // через горы котла ногами не ходят — только перевалом
			}
			if d := math.Hypot(wx-as[i].e, wy-as[i].n); d < bd {
				bd, best = d, &as[i]
			}
		}
		if best == cur || bd+landHysteresis > dc {
			continue
		}
		pl.Settlement = best.id
		p.X, p.Y = wx-best.e, wy-best.n
		if _, h := horseOf(w, e); h != nil && Mounted(e) {
			h.Settlement = best.id
		}
		if nav := navOf(e); nav != nil {
			nav.Route = nil
		}
		EndTalk(e)
		say(w, e, "road", "Межа позади: дальше лежат земли другого села.")
	}
}

func settleOf(w *core.World, id core.ID) *core.SettlementC {
	if se := w.Get(id); se != nil {
		return core.Settle(se)
	}
	return nil
}
