package sim

import (
	"math"
	"sort"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// Планета, «котёл» и разные деревни (v1.14).
//
// Планета Ёф — модель всего мира: радиус, державы (около двадцати на всю планету — канон), место
// «котла». «Котёл» — стартовый край: чаша, окружённая горами, с перевалом-вратами; внутри него
// демо-деревни. Каждое поселение мира получает МЕСТО (SiteC): широту и долготу на планете, климат по
// широте, державу и собственный характер — у воды оно или на холмах, есть ли рядом лес, рудник, поля.
// От этого зависит, чем там можно заниматься: рыбу не ловят там, где нет реки, руду не добывают там,
// где нет рудника. Деревни перестают быть копиями друг друга по существу, даже пока геометрия
// сцены (Layout v2) у них общая.
//
// Истина о планете — знание разработчика (канон: мир знают только старцы). В кадр игрока попадает
// лишь место, где он стоит; сводка планеты — только в служебном /api/planet.

func init() {
	core.Register("planet", func() core.Component { return &PlanetC{} })
	core.Register("site", func() core.Component { return &SiteC{} })
}

// State — держава.
type State struct {
	Name     string  `json:"name"`
	Lat, Lon float64 `json:"-"`
	LatR     float64 `json:"lat"`
	LonR     float64 `json:"lon"`
	Kind     string  `json:"kind"` // empire|free|martial|kingdom
}

// PlanetC — планета (одна на мир).
type PlanetC struct {
	Name        string  `json:"name"`
	RadiusKm    float64 `json:"radius_km"`
	States      []State `json:"states"`
	CauldronLat float64 `json:"cauldron_lat"`
	CauldronLon float64 `json:"cauldron_lon"`
	CauldronKm  float64 `json:"cauldron_km"` // поперечник чаши
	KmPerUnit   float64 `json:"km_per_unit"` // сколько километров в единице карты мира
}

func (c *PlanetC) Kind() string { return "planet" }

// SiteC — место поселения на планете и его характер.
type SiteC struct {
	Lat          float64         `json:"lat"`
	Lon          float64         `json:"lon"`
	CoordVersion int             `json:"coord_version,omitempty"`
	Climate      string          `json:"climate"`
	State     string          `json:"state"`
	Terrain   string          `json:"terrain"`   // river|hills|forest|plain|coast|marsh
	Features  map[string]bool `json:"features"`  // river, woods, mine, garden
	Specialty string          `json:"specialty"` // чем место славится
	Cauldron  bool            `json:"cauldron"`  // внутри стартовой чаши
}

func (c *SiteC) Kind() string { return "site" }

var stateRoots = []string{"Велемир", "Острог", "Радон", "Кремень", "Ясногор", "Златояр", "Северин", "Тихомор", "Беловод", "Чернолес",
	"Вышеград", "Ладомир", "Сурож", "Каменец", "Полынь", "Заречье", "Дивногор", "Ольховец", "Студень", "Марево"}

// EnsurePlanet создаёт планету из семени мира (один раз) и возвращает её.
func EnsurePlanet(w *core.World) *PlanetC {
	var p *PlanetC
	w.Each("planet", func(e *core.Entity) { p, _ = e.Get("planet").(*PlanetC) })
	if p != nil {
		return p
	}
	g := dice.NewStream(dice.Seed64(w.Seed, "planet"))
	p = &PlanetC{Name: "Ёф", RadiusKm: 5800 + g.Uniform(0, 900), CauldronLat: 48 + g.Uniform(-6, 6),
		CauldronLon: g.Uniform(-30, 60), CauldronKm: 60, KmPerUnit: 0.25}
	kinds := []string{"empire", "free", "free", "martial"} // у котла: империя, две вольные, одна воинственная (канон)
	for i, name := range stateRoots {
		st := State{Name: name, Kind: "kingdom"}
		if i < len(kinds) { // первые четыре — соседи котла
			st.Kind = kinds[i]
			a := float64(i) * math.Pi / 2
			st.Lat, st.Lon = p.CauldronLat+math.Sin(a)*3, p.CauldronLon+math.Cos(a)*4
		} else {
			st.Lat, st.Lon = g.Uniform(-65, 70), g.Uniform(-180, 180)
		}
		st.LatR, st.LonR = math.Round(st.Lat*10)/10, math.Round(st.Lon*10)/10
		p.States = append(p.States, st)
	}
	w.Spawn(p)
	return p
}

func climateAt(lat float64) string {
	switch a := math.Abs(lat); {
	case a > 66:
		return "polar"
	case a > 52:
		return "cold"
	case a > 35:
		return "temperate"
	case a > 20:
		return "warm"
	}
	return "hot"
}

var terrainFeatures = map[string][]string{
	"river":  {"river", "garden", "woods"},
	"hills":  {"mine", "woods"},
	"forest": {"woods", "garden"},
	"plain":  {"garden", "river"},
	"coast":  {"river", "garden"},
	"marsh":  {"river", "woods"},
}
var terrainSpecialty = map[string]string{"river": "рыба и мельницы", "hills": "руда и камень", "forest": "лес и зверь",
	"plain": "хлеб и скот", "coast": "соль и рыба", "marsh": "травы и торф"}
var biomeTerrains = map[string][]string{"forest": {"forest", "river", "marsh"}, "hills": {"hills", "forest"},
	"plain": {"plain", "river"}, "coast": {"coast", "river"}, "steppe": {"plain", "hills"}}

// SiteOf — место поселения (создаётся при первом обращении, детерминированно от семени и имени).
func SiteOf(w *core.World, se *core.Entity) *SiteC {
	if se == nil || core.Settle(se) == nil {
		return nil
	}
	s := core.Settle(se)
	p := EnsurePlanet(w)
	r := core.Region(w.Get(s.Region))
	if c, _ := se.Get("site").(*SiteC); c != nil {
		// Сохранённые v0 SiteC содержат прежнюю плоскую аппроксимацию. Пересчитываем только
		// координатные поля из неизменных SettlementC.X/Y; рельеф, угодья и специализация остаются.
		if c.CoordVersion < SurfaceProjectionVersion {
			if applySiteCoordinates(c, s, p) {
				c.CoordVersion = SurfaceProjectionVersion
			}
		}
		return c
	}
	g := dice.NewStream(dice.Seed64(w.Seed, "site", s.Name, sid(se.ID)))
	c := &SiteC{Features: map[string]bool{}}
	c.Cauldron = r != nil && !r.Outer
	if applySiteCoordinates(c, s, p) {
		c.CoordVersion = SurfaceProjectionVersion
	}
	biome := "plain"
	if r != nil && r.Biome != "" {
		biome = r.Biome
	}
	opts := biomeTerrains[biome]
	if len(opts) == 0 {
		opts = []string{"plain", "river", "forest", "hills"}
	}
	c.Terrain = opts[g.Intn(len(opts))]
	if c.Cauldron { // в котле деревни заведомо разные: по порядку id — река, лес, холмы, равнина
		var ids []core.ID
		w.Each("settlement", func(o *core.Entity) {
			if or := core.Region(w.Get(core.Settle(o).Region)); or != nil && !or.Outer {
				ids = append(ids, o.ID)
			}
		})
		sort.Slice(ids, func(i, j int) bool { return ids[i] < ids[j] })
		for i, id := range ids {
			if id == se.ID {
				c.Terrain = []string{"river", "forest", "hills", "plain"}[i%4]
				if i == 0 { // первая (Починок) — учебная: в ней есть всё
					for _, f := range []string{"river", "woods", "mine", "garden"} {
						c.Features[f] = true
					}
				}
			}
		}
	}
	for _, f := range terrainFeatures[c.Terrain] {
		c.Features[f] = true
	}
	c.Specialty = terrainSpecialty[c.Terrain]
	w.Attach(se, c)
	return c
}

// applySiteCoordinates converts the legacy world-map offsets around the cauldron into a true
// spherical geodetic position. SettlementC.X/Y remain the authoritative legacy map coordinates.
func applySiteCoordinates(site *SiteC, settlement *core.SettlementC, planet *PlanetC) bool {
	if site == nil || settlement == nil || planet == nil || planet.KmPerUnit <= 0 || planet.RadiusKm <= 0 {
		return false
	}
	projection := PlanetProjection{
		Origin: SurfacePoint{LatitudeDeg: planet.CauldronLat, LongitudeDeg: planet.CauldronLon},
		RadiusMeters: planet.RadiusKm * 1000,
	}
	local := LocalMeters{
		East:  (settlement.X - 500) * planet.KmPerUnit * 1000,
		North: (settlement.Y - 500) * planet.KmPerUnit * 1000,
	}
	point, ok := projection.Inverse(local)
	if !ok {
		return false
	}
	site.Lat = math.Round(point.LatitudeDeg*100) / 100
	site.Lon = math.Round(point.LongitudeDeg*100) / 100
	site.Climate = climateAt(site.Lat)
	site.State = nearestState(planet, point)
	return true
}

// nearestState uses persisted rounded geodetic coordinates (LatR/LonR), not transient Lat/Lon
// fields that are intentionally excluded from JSON. Distance is spherical and antimeridian-safe.
func nearestState(planet *PlanetC, point SurfacePoint) string {
	if planet == nil {
		return ""
	}
	best := math.MaxFloat64
	name := ""
	for _, state := range planet.States {
		candidate := SurfacePoint{LatitudeDeg: state.LatR, LongitudeDeg: state.LonR}
		distance, ok := GreatCircleMeters(point, candidate, planet.RadiusKm*1000)
		if ok && distance < best {
			best, name = distance, state.Name
		}
	}
	return name
}
// siteLacks — нет ли в поселении игрока нужного места (реки, леса, рудника, полей). Пустая причина — есть.
func siteLacks(w *core.World, e *core.Entity, feature string) string {
	site := SiteOf(w, settlementEnt(w, e))
	if site == nil || site.Features[feature] {
		return ""
	}
	switch feature {
	case "river":
		return "здесь нет реки — рыбу ловят в других сёлах"
	case "mine":
		return "здесь нет рудника — руду добывают в холмах"
	case "woods":
		return "здесь нет строевого леса"
	}
	return "здесь нет полей и трав"
}

// SiteView — место для кадра (place.site).
func SiteView(w *core.World, se *core.Entity) map[string]any {
	c := SiteOf(w, se)
	fs := []string{}
	for f, ok := range c.Features {
		if ok {
			fs = append(fs, f)
		}
	}
	sort.Strings(fs)
	return map[string]any{"terrain": c.Terrain, "features": fs, "climate": c.Climate, "state": c.State,
		"specialty": c.Specialty, "lat": c.Lat, "lon": c.Lon, "cauldron": c.Cauldron}
}

// PlanetView — служебная сводка планеты (/api/planet): знание разработчика, игрокам не показывается.
func PlanetView(w *core.World) map[string]any {
	p := EnsurePlanet(w)
	terr := map[string]int{}
	in, out := 0, 0
	w.Each("settlement", func(se *core.Entity) {
		c := SiteOf(w, se)
		terr[c.Terrain]++
		if c.Cauldron {
			in++
		} else {
			out++
		}
	})
	return map[string]any{"planet": p, "cauldron_settlements": in, "outer_settlements": out, "terrains": terr}
}

var _ = econ.GoodOre
