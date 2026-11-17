package sim

import (
	"math"

	"pogranichye/core"
	"pogranichye/dice"
)

// Дикие места (v1.21): самостоятельные локации БЕЗ поселения, разбросанные в поле между сёлами, а
// не привязанные к какому-то одному из них — пещеры, башни, руины, урочища (места силы), родники.
// Запрос владельца: мир не должен состоять только из сёл и пустого поля между ними — между ними
// должно быть что-то своё, реалистично раскиданное, и не только на уровне земли (пещера — вниз,
// башня — вверх, см. комментарий о высоте в worldpos.go/scene.go).
//
// Своя точка на карте края — те же единицы, что у SettlementC.X/Y (не локальная сцена одного села),
// так что дикое место физически лежит МЕЖДУ сёлами в том же пространстве, где уже работает
// WorldPos/Anchor. Не растёт в бесконечность сам по себе: разбрасывается один раз на край при его
// создании (стартовый котёл и каждое новое кольцо фронтира), тем же самым детерминированным потоком
// костей, что и сами сёла этого края.

func init() {
	core.Register("wildsite", func() core.Component { return &WildSiteC{} })
}

// WildSiteC — дикое место. Type — вид (cave|tower|ruins|grove|spring), не Kind: имя поля Kind
// заняло бы имя метода core.Component.
type WildSiteC struct {
	Name   string  `json:"name"`
	Type   string  `json:"wild_type"`
	Region core.ID `json:"region"`
	X      float64 `json:"x"` // единицы карты края — как у SettlementC.X/Y, не метры
	Y      float64 `json:"y"`
	Z      float64 `json:"z"` // смещение высоты, м: пещера — вниз, башня — вверх, прочее — 0 (по рельефу)
	Mana   float64 `json:"mana"`
	Danger float64 `json:"danger"`
}

func (c *WildSiteC) Kind() string { return "wildsite" }

func WildSite(e *core.Entity) *WildSiteC {
	if e == nil {
		return nil
	}
	c, _ := e.Get("wildsite").(*WildSiteC)
	return c
}

// wildKindsByBiome — какие виды диких мест правдоподобны для биома края: пещеры и башни-руины в
// холмах, урочища (мана) в лесу, родники у воды, курганы-башни в степи/поле.
var wildKindsByBiome = map[string][]string{
	"hills":  {"cave", "cave", "ruins", "tower"},
	"forest": {"grove", "grove", "ruins", "cave"},
	"coast":  {"spring", "spring", "ruins"},
	"plain":  {"tower", "ruins", "grove"},
	"steppe": {"tower", "tower", "ruins"},
}

var wildNameNoun = map[string][]string{
	"cave":   {"Тёмная нора", "Сквозная пещера", "Медвежья яма", "Подземный ход"},
	"tower":  {"Старый дозор", "Сторожевая вышка", "Сломанная башня", "Маяк на кургане"},
	"ruins":  {"Старое городище", "Заброшенный двор", "Каменные развалины", "Пепелище"},
	"grove":  {"Заветная роща", "Поляна силы", "Старый дуб", "Урочище"},
	"spring": {"Святой родник", "Ключ-вода", "Бьющий источник", "Тихий омут"},
}

// wildKindZ — смещение высоты для вида: пещера уходит вниз, башня — вверх, прочее — по рельефу.
func wildKindZ(g *dice.Stream, kind string) float64 {
	switch kind {
	case "cave":
		return -g.Uniform(8, 25)
	case "tower":
		return g.Uniform(12, 35)
	default:
		return 0
	}
}

func wildSiteName(g *dice.Stream, kind string) string {
	names := wildNameNoun[kind]
	if len(names) == 0 {
		return "Безымянное место"
	}
	return names[g.Intn(len(names))]
}

// scatterWildSites — разбрасывает count диких мест вокруг (cx,cy) в единицах карты региона,
// избегая тесноты с уже стоящими сёлами (avoid) и друг с другом. Детерминировано потоком g,
// переданным вызывающим кодом (тот же, что растит само поселение/край) — одно и то же семя мира
// даёт одни и те же дикие места. Если за разумное число попыток свободного места не нашлось —
// пропускает это место, не ломая остальные (лучше меньше диких мест, чем наложить их на село).
func scatterWildSites(w *core.World, g *dice.Stream, region *core.Entity, cx, cy float64, avoid [][2]float64, count int) {
	r := core.Region(region)
	if w == nil || r == nil || region == nil || count <= 0 {
		return
	}
	kinds := wildKindsByBiome[r.Biome]
	if len(kinds) == 0 {
		kinds = []string{"ruins"}
	}
	const minDist = 35.0 // единицы карты — не теснее этого к селу или другому дикому месту
	var placed [][2]float64
	for i := 0; i < count; i++ {
		var x, y float64
		ok := false
		for attempt := 0; attempt < 20 && !ok; attempt++ {
			ang := g.Uniform(0, 2*math.Pi)
			rad := g.Uniform(25, 140) // в поле между сёлами, не на самом краю региона
			x, y = cx+math.Cos(ang)*rad, cy+math.Sin(ang)*rad
			ok = true
			for _, p := range avoid {
				if math.Hypot(x-p[0], y-p[1]) < minDist {
					ok = false
					break
				}
			}
			if ok {
				for _, p := range placed {
					if math.Hypot(x-p[0], y-p[1]) < minDist {
						ok = false
						break
					}
				}
			}
		}
		if !ok {
			continue
		}
		placed = append(placed, [2]float64{x, y})
		kind := kinds[g.Intn(len(kinds))]
		mana := r.Mana
		if kind == "grove" {
			mana *= g.Uniform(1.3, 1.9) // урочище заметно гуще фона края — настоящее место силы
		}
		w.Spawn(&WildSiteC{
			Name: wildSiteName(g, kind), Type: kind, Region: region.ID,
			X: x, Y: y, Z: wildKindZ(g, kind),
			Mana: clampf(mana, 0.1, 3.0), Danger: g.Uniform(0.2, 1.0),
		})
	}
}

// scatterWildSitesPerRegion — раскладывает дикие места по всем краям мира сразу после того, как в
// них расселены сёла (Generate/многодеревенский котёл): для каждого края — свои 2-4 диких места,
// подальше от уже стоящих в нём сёл. ids — ID всех сёл, только что созданных тем же вызовом.
func scatterWildSitesPerRegion(w *core.World, g *dice.Stream, regions []*core.Entity, ids []core.ID) {
	byRegion := map[core.ID][][2]float64{}
	for _, id := range ids {
		se := w.Get(id)
		s := core.Settle(se)
		if s == nil {
			continue
		}
		byRegion[s.Region] = append(byRegion[s.Region], [2]float64{s.X, s.Y})
	}
	for _, re := range regions {
		r := core.Region(re)
		if r == nil {
			continue
		}
		scatterWildSites(w, g, re, r.X, r.Y, byRegion[re.ID], 2+g.Intn(3)) // 2..4 на край
	}
}

// WildSitesView — дикие места края для /api/world (служебная карта, не кадр игрока — см.
// WorldManifest). Абсолютные мировые координаты считаются так же, как якорь села (Anchor), только
// без привязки к самому селу.
func WildSitesView(w *core.World) []map[string]any {
	p := EnsurePlanet(w)
	t := NewTerrain(w)
	var out []map[string]any
	w.Each("wildsite", func(e *core.Entity) {
		ws := WildSite(e)
		if ws == nil {
			return
		}
		ex, ny := (ws.X-500)*p.KmPerUnit*1000, (ws.Y-500)*p.KmPerUnit*1000
		ground := t.Height(ex, ny)
		out = append(out, map[string]any{
			"id": eidStr(e.ID), "name": ws.Name, "wild_type": ws.Type, "region": eidStr(ws.Region),
			"frame": 0, "e": r3(ex), "n": r3(ny), "u": r3(ground + ws.Z), "ground": r3(ground),
			"mana": r3(ws.Mana), "danger": r3(ws.Danger),
		})
	})
	return out
}
