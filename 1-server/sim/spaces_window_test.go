package sim

import (
	"encoding/json"
	"math"
	"testing"

	"pogranichye/core"
)

// Оконные проёмы корчмы (письмо Kate 0301, v1.35). Окна — часть серверно-авторитетной геометрии
// /api/spaces: детерминированные, на фасадах без двери, с подоконником/перемычкой/простенками, без
// новых порталов и без права прохода (остекление collidable+transparent). Движение внутрь/наружу
// по-прежнему решает дверь и серверные правила — геометрия пространства прав не даёт.

func windowSurfaces(sp map[string]any) []map[string]any {
	var out []map[string]any
	for _, s := range sp["surfaces"].([]map[string]any) {
		if s["kind"] == "window" {
			out = append(out, s)
		}
	}
	return out
}

// Остекление честное: collidable (не дыра в коллизии), помечено прозрачным для клиента, непроходимо.
func checkGlazing(t *testing.T, sp map[string]any) {
	t.Helper()
	wins := windowSurfaces(sp)
	if len(wins) == 0 {
		t.Fatalf("%v: ждали оконные проёмы, не нашли ни одного", sp["id"])
	}
	for _, s := range wins {
		if s["collidable"] != true {
			t.Fatalf("окно %v должно быть collidable: сквозь окно нельзя пройти", s["id"])
		}
		if s["transparent"] != true {
			t.Fatalf("окно %v должно иметь transparent=true (видимый световой проём)", s["id"])
		}
		if s["walkable"] != false {
			t.Fatalf("окно %v не должно быть walkable", s["id"])
		}
		pts := s["points"].([]map[string]float64)
		if len(pts) != 4 {
			t.Fatalf("окно %v: ждали прямоугольник из 4 точек, получили %d", s["id"], len(pts))
		}
	}
}

// Корчма каждого села получает окна первого яруса: прозрачные, непроходимые, в пределах охвата, и
// порталов по-прежнему ровно один (дверь) — окна не создают переходов и прав прохода.
func TestTavernHasWindowApertures(t *testing.T) {
	w, _ := Generate("spaces", 12)
	tr := NewTerrain(w)
	n := 0
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		l := LayoutOf(w, se.ID)
		var tav *Rect
		for i := range l.Buildings {
			if l.Buildings[i].BuildingID == "tavern" {
				tav = &l.Buildings[i]
			}
		}
		if tav == nil || tav.Door == nil {
			return
		}
		n++
		ae, an := Anchor(w, s)
		sp := spaceByID(SpacesChunk(w, tr, chunkOfM(ae+tav.cx()), chunkOfM(an+tav.cy())), TavernSpaceID(se.ID))
		if sp == nil {
			t.Fatalf("%s: корчмы нет в своём чанке", s.Name)
		}
		checkSpace(t, sp) // точки в охвате, уникальные id, ссылки на ярусы
		checkGlazing(t, sp)
		if len(sp["portals"].([]map[string]any)) != 1 {
			t.Fatalf("%s: окна не должны добавлять порталов — ждали только дверь, получили %d",
				s.Name, len(sp["portals"].([]map[string]any)))
		}
		// окна только у первого яруса (ground): свет в зал корчмы
		for _, s2 := range windowSurfaces(sp) {
			if fid := s2["floor_id"].(string); fid != TavernSpaceID(se.ID)+":floor:ground" {
				t.Fatalf("%s: окно %v на ярусе %v, ждали только ground", s.Name, s2["id"], fid)
			}
		}
	})
	if n == 0 {
		t.Fatal("ни в одном селе нет корчмы с дверью")
	}
}

// Прямой тест buildBox по всем четырём сторонам света: окна встают на трёх фасадах без двери,
// ориентированы по facing, представляют собой пояс в пределах [sill..head] (а не дыру во всю высоту),
// и не попадают на фасад с дверью.
func TestWindowApertureFacings(t *testing.T) {
	const eps = 1e-3
	for _, facing := range []int{270, 90, 180, 0} {
		sb := buildBox(boxSpec{
			id: "tbox", name: "короб", roomKind: "test", titles: []string{"низ", "верх"},
			oe: 0, on: 0, g: 100, W: 14, D: 12, facing: facing,
			doorA: 7, doorW: 1.4, t: wallThickness, storeys: 2, storeyH: 3.0, cellar: 2.0, windows: true, wallKind: "wall",
		})
		sp := sb.build("building", "короб")
		checkSpace(t, sp)
		wins := windowSurfaces(sp)
		if len(wins) == 0 {
			t.Fatalf("facing=%d: нет окон", facing)
		}
		o := sp["origin"].(map[string]float64)

		// дверь — её фасад (сторона света) окон получать не должен
		b := sp["bounds"].(map[string]float64)
		ce, cn := (b["min_e"]+b["max_e"])/2, (b["min_n"]+b["max_n"])/2
		door := sp["portals"].([]map[string]any)[0]
		doorSide := side("e", door["e"].(float64), ce)
		if facing == 270 || facing == 90 {
			doorSide = side("n", door["n"].(float64), cn)
		}

		// каждое окно прижато к одной внешней стене; классифицируем по стороне света (у фасада два
		// слоя — наружная оболочка и внутреннее кольцо, — поэтому плоскостей 6, а сторон 3)
		sides := map[string]bool{}
		for _, s := range wins {
			pts := s["points"].([]map[string]float64)
			for _, p := range pts {
				if p["y"] < winSillH-eps || p["y"] > winHeadH+eps {
					t.Fatalf("facing=%d: окно %v выходит за пояс [sill..head]: y=%.3f", facing, s["id"], p["y"])
				}
			}
			es, ns := map[float64]bool{}, map[float64]bool{}
			for _, p := range pts {
				es[round3(o["e"]+p["x"])] = true
				ns[round3(o["n"]+p["z"])] = true
			}
			var sd string
			switch {
			case len(es) == 1:
				sd = side("e", o["e"]+pts[0]["x"], ce)
			case len(ns) == 1:
				sd = side("n", o["n"]+pts[0]["z"], cn)
			default:
				t.Fatalf("facing=%d: окно %v не прижато к стене (es=%d ns=%d)", facing, s["id"], len(es), len(ns))
			}
			if sd == doorSide {
				t.Fatalf("facing=%d: окно %v на фасаде двери (%s)", facing, s["id"], sd)
			}
			sides[sd] = true
		}
		// три фасада без двери — три разные стороны окон (левый, правый, задний)
		if len(sides) != 3 {
			t.Fatalf("facing=%d: ждали окна на 3 фасадах, получили стороны %v", facing, sides)
		}
	}
}

// Отпечаток чанка реагирует на оконную геометрию: короб с окнами и без описаны по-разному.
func TestWindowsChangeRevision(t *testing.T) {
	base := boxSpec{id: "rb", name: "к", roomKind: "test", titles: []string{"низ", "верх"},
		oe: 0, on: 0, g: 10, W: 14, D: 12, facing: 270, doorA: 7, doorW: 1.4,
		t: wallThickness, storeys: 2, storeyH: 3.0, cellar: 2.0, wallKind: "wall"}
	without := buildBox(base).build("building", "к")
	withWin := base
	withWin.windows = true
	with := buildBox(withWin).build("building", "к")

	a, _ := json.Marshal(without)
	b, _ := json.Marshal(with)
	if string(a) == string(b) {
		t.Fatal("окна не изменили описание пространства")
	}
	if len(with["surfaces"].([]map[string]any)) <= len(without["surfaces"].([]map[string]any)) {
		t.Fatal("окна должны добавлять поверхности (рама + остекление)")
	}
}

func round3(v float64) float64 { return math.Round(v*1000) / 1000 }

// side — к какой стороне света (оси и знаку от центра) прижата внешняя стена: "e-","e+","n-","n+".
func side(axis string, v, center float64) string {
	if v < center {
		return axis + "-"
	}
	return axis + "+"
}
