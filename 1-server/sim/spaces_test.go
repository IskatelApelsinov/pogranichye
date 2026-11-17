package sim

import (
	"encoding/json"
	"math"
	"strings"
	"testing"

	"pogranichye/core"
)

func chunkOfM(v float64) int64 { return int64(math.Floor(v / ChunkMeters)) }

func spaceByID(v map[string]any, id string) map[string]any {
	for _, sp := range v["spaces"].([]map[string]any) {
		if sp["id"] == id {
			return sp
		}
	}
	return nil
}

// checkSpace — общие законы любого пространства: имена ярусов сходятся, точки конечны и лежат в
// охвате, помещения не вылезают за охват, лестница начинается на своём ярусе и кончается на чужом.
func checkSpace(t *testing.T, sp map[string]any) {
	t.Helper()
	id := sp["id"].(string)
	o, b := sp["origin"].(map[string]float64), sp["bounds"].(map[string]float64)
	elev := map[string]float64{}
	for _, f := range sp["floors"].([]map[string]any) {
		if _, dup := elev[f["id"].(string)]; dup {
			t.Fatalf("%s: ярус %v повторён", id, f["id"])
		}
		elev[f["id"].(string)] = f["elevation_m"].(float64)
	}
	ids := map[string]bool{}
	inside := func(p map[string]float64, what string) {
		e, n, u := o["e"]+p["x"], o["n"]+p["z"], o["u"]+p["y"]
		for _, v := range []float64{e, n, u} {
			if math.IsNaN(v) || math.IsInf(v, 0) {
				t.Fatalf("%s: %s — нечисло", id, what)
			}
		}
		const eps = 0.002
		if e < b["min_e"]-eps || e > b["max_e"]+eps || n < b["min_n"]-eps || n > b["max_n"]+eps || u < b["min_u"]-eps || u > b["max_u"]+eps {
			t.Fatalf("%s: %s вне охвата: %v %v %v / %v", id, what, e, n, u, b)
		}
	}
	for _, s := range sp["surfaces"].([]map[string]any) {
		sid := s["id"].(string)
		if ids[sid] {
			t.Fatalf("%s: поверхность %s повторена", id, sid)
		}
		ids[sid] = true
		if _, ok := elev[s["floor_id"].(string)]; !ok {
			t.Fatalf("%s: поверхность %s ссылается на неизвестный ярус %v", id, sid, s["floor_id"])
		}
		pts := s["points"].([]map[string]float64)
		if len(pts) < 3 {
			t.Fatalf("%s: у поверхности %s меньше трёх точек", id, sid)
		}
		for _, p := range pts {
			inside(p, sid)
		}
	}
	for _, r := range sp["rooms"].([]map[string]any) {
		rb := r["bounds"].(map[string]float64)
		if rb["min_e"] < b["min_e"]-0.002 || rb["max_e"] > b["max_e"]+0.002 || rb["min_n"] < b["min_n"]-0.002 || rb["max_n"] > b["max_n"]+0.002 ||
			rb["min_u"] < b["min_u"]-0.002 || rb["max_u"] > b["max_u"]+0.002 || rb["min_e"] >= rb["max_e"] || rb["min_n"] >= rb["max_n"] || rb["min_u"] >= rb["max_u"] {
			t.Fatalf("%s: помещение %v вне охвата или пустое: %v / %v", id, r["id"], rb, b)
		}
		if r["kind"] == "" {
			t.Fatalf("%s: у помещения %v нет вида", id, r["id"])
		}
	}
	for _, st := range sp["stairs"].([]map[string]any) {
		from, ok1 := elev[st["from_floor"].(string)]
		to, ok2 := elev[st["to_floor"].(string)]
		if !ok1 || !ok2 || from == to {
			t.Fatalf("%s: лестница %v связывает не те ярусы", id, st["id"])
		}
		pts := st["points"].([]map[string]float64)
		first, last := pts[0], pts[len(pts)-1]
		if math.Abs(o["u"]+first["y"]-from) > 0.01 || math.Abs(o["u"]+last["y"]-to) > 0.01 {
			t.Fatalf("%s: лестница %v не доходит от яруса до яруса: %v→%v при %v→%v", id, st["id"], o["u"]+first["y"], o["u"]+last["y"], from, to)
		}
		for i, p := range pts {
			inside(p, st["id"].(string))
			if i > 0 { // непрерывна и проходима: ни отвесного, ни слишком крутого куска
				run := math.Hypot(p["x"]-pts[i-1]["x"], p["z"]-pts[i-1]["z"])
				rise := math.Abs(p["y"] - pts[i-1]["y"])
				if run < 0.05 || rise/run > 1.2 {
					t.Fatalf("%s: лестница %v, кусок %d: подъём %.2f на %.2f м пути", id, st["id"], i, rise, run)
				}
			}
		}
	}
	for _, p := range sp["portals"].([]map[string]any) {
		if p["e"].(float64) < b["min_e"]-0.002 || p["e"].(float64) > b["max_e"]+0.002 || p["n"].(float64) < b["min_n"]-0.002 || p["n"].(float64) > b["max_n"]+0.002 {
			t.Fatalf("%s: проём %v вне охвата", id, p["id"])
		}
	}
}

// Корчма каждого села — дом в два яруса с погребом; дверь описания стоит там же, где дверь, через
// которую сервер пускает внутрь.
func TestTavernSpaceInEverySettlement(t *testing.T) {
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
		v := SpacesChunk(w, tr, chunkOfM(ae+tav.cx()), chunkOfM(an+tav.cy()))
		sp := spaceByID(v, TavernSpaceID(se.ID))
		if sp == nil {
			t.Fatalf("%s: корчмы нет в своём чанке", s.Name)
		}
		checkSpace(t, sp)
		if len(sp["floors"].([]map[string]any)) != 3 || len(sp["stairs"].([]map[string]any)) != 2 {
			t.Fatalf("%s: ждали погреб, зал и верх с двумя лестницами: %v / %v", s.Name, sp["floors"], sp["stairs"])
		}
		door := sp["portals"].([]map[string]any)[0]
		if math.Abs(door["e"].(float64)-(ae+tav.Door.X)) > 0.01 || math.Abs(door["n"].(float64)-(an+tav.Door.Y)) > 0.01 {
			t.Fatalf("%s: дверь описания (%v,%v) не там, где дверь села (%v,%v)", s.Name, door["e"], door["n"], ae+tav.Door.X, an+tav.Door.Y)
		}
		b := sp["bounds"].(map[string]float64)
		if b["min_e"] > ae+tav.X0 || b["max_e"] < ae+tav.X1 || b["min_n"] > an+tav.Y0 || b["max_n"] < an+tav.Y1 {
			t.Fatalf("%s: охват меньше пятна здания", s.Name)
		}
	})
	if n == 0 {
		t.Fatal("ни в одном селе нет корчмы с дверью")
	}
}

// Одно и то же: сходил в соседний чанк и вернулся — описание и отпечаток те же; здание на стыке
// чанков в каждом из них одно, с одним именем; отрицательные номера чанков работают.
func TestSpacesChunkStableAcrossChunks(t *testing.T) {
	w, _ := Generate("spaces", 12)
	tr := NewTerrain(w)
	// башня и пещера ровно на углу четырёх чанков в отрицательной четверти
	p := EnsurePlanet(w)
	at := func(e, n float64) (float64, float64) { return e/(p.KmPerUnit*1000) + 500, n/(p.KmPerUnit*1000) + 500 }
	x, y := at(-3*ChunkMeters, -5*ChunkMeters)
	tower := w.Spawn(&WildSiteC{Name: "Старый дозор", Type: "tower", X: x, Y: y, Z: 24})
	x, y = at(-9*ChunkMeters+1, -2*ChunkMeters-3)
	cave := w.Spawn(&WildSiteC{Name: "Тёмная нора", Type: "cave", X: x, Y: y, Z: -18})

	a1, _ := json.Marshal(SpacesChunk(w, tr, -3, -5))
	_ = SpacesChunk(w, tr, -4, -5)
	a2, _ := json.Marshal(SpacesChunk(w, tr, -3, -5))
	if string(a1) != string(a2) {
		t.Fatal("A→B→A: описание чанка изменилось")
	}
	tid := "w:" + eidStr(tower.ID) + ":tower"
	var ref []byte
	for _, c := range [][2]int64{{-3, -5}, {-4, -5}, {-3, -6}, {-4, -6}} {
		sp := spaceByID(SpacesChunk(w, tr, c[0], c[1]), tid)
		if sp == nil {
			t.Fatalf("башни нет в чанке %v, хотя она его задевает", c)
		}
		raw, _ := json.Marshal(sp)
		if ref == nil {
			ref = raw
			checkSpace(t, sp)
			if fl := sp["floors"].([]map[string]any); len(fl) < 5 || !strings.HasSuffix(fl[len(fl)-1]["id"].(string), ":floor:roof") {
				t.Fatalf("у башни ждали подземелье, ярусы и кровлю: %v", fl)
			}
		} else if string(raw) != string(ref) {
			t.Fatalf("башня в чанке %v описана иначе", c)
		}
	}
	if spaceByID(SpacesChunk(w, tr, -1, -5), tid) != nil {
		t.Fatal("башня попала в чужой чанк")
	}
	cid := "w:" + eidStr(cave.ID) + ":cave"
	csp := spaceByID(SpacesChunk(w, tr, -9, -3), cid)
	if csp == nil {
		t.Fatal("пещеры нет в чанке её устья")
	}
	checkSpace(t, csp)
	if csp["kind"] != "cave" || spaceByID(SpacesChunk(w, tr, -9, -2), cid) == nil {
		t.Fatal("пещера уходит на север через границу чанка и должна быть в обоих")
	}
	hall := csp["floors"].([]map[string]any)[1]["elevation_m"].(float64)
	if mouth := csp["origin"].(map[string]float64)["u"]; mouth-hall < 4 {
		t.Fatalf("зал пещеры должен быть в глубине: устье %.1f, зал %.1f", mouth, hall)
	}
}

// Игрок в зале корчмы: me.wpos называет пространство и ярус; на улице — пусто.
func TestSpaceAtNamesTavernFloor(t *testing.T) {
	w, _ := GenerateStarter("spaces-at")
	var se *core.Entity
	w.Each("settlement", func(e *core.Entity) {
		if se == nil {
			se = e
		}
	})
	l := LayoutOf(w, se.ID)
	cx, cy, _, ok := l.TavernCounter()
	if !ok {
		t.Skip("нет корчмы")
	}
	e := w.Spawn(&core.PlaceC{Settlement: se.ID}, &ScenePosC{X: cx, Y: cy})
	sp, lv := SpaceAt(w, e)
	if sp != TavernSpaceID(se.ID) || lv != TavernSpaceID(se.ID)+":floor:ground" {
		t.Fatalf("в зале корчмы: %v / %v", sp, lv)
	}
	ScenePos(e).X, ScenePos(e).Y = l.Entry()
	if sp, lv = SpaceAt(w, e); sp != nil || lv != nil {
		t.Fatalf("на улице: %v / %v", sp, lv)
	}
}
