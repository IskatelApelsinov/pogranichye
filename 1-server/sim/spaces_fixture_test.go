package sim

import (
	"encoding/json"
	"math"
	"os"
	"testing"

	"pogranichye/core"
)

// Реальные образцы /api/spaces для башни и пещеры (comms 0277). В main лежал только образец корчмы
// (spaces_v1.example.json); клиентский WorldSpaceDescriptorValidator проверял лишь его. Эти образцы —
// НЕ руками написанный JSON, а дословный ответ SpacesChunk для детерминированного мира с одной башней
// и одной пещерой. Обновить: UPDATE_FIXTURES=1 go test ./sim -run TestSpacesTowerCaveFixtures.
//
// Границы (0277): это упрочнение образцов и тестов, не S4. Контракт (кадр v:1, поля) не меняется,
// локального/серверного перехода между ярусами здесь нет, визуальные лестницы проходимостью не
// объявляются.

const (
	towerFixturePath = "../../3-dev/contract/fixtures/spaces_tower_v1.example.json"
	caveFixturePath  = "../../3-dev/contract/fixtures/spaces_cave_v1.example.json"
)

// towerCaveWorld — тот же детерминированный расклад, что и в TestSpacesChunkStableAcrossChunks:
// многоярусная башня с погребом и кровлей в чанке (-3,-5) и нисходящая пещера в чанке (-9,-3).
func towerCaveWorld(t *testing.T) (*core.World, *Terrain, core.ID, core.ID) {
	t.Helper()
	w, _ := Generate("spaces", 12)
	tr := NewTerrain(w)
	p := EnsurePlanet(w)
	at := func(e, n float64) (float64, float64) { return e/(p.KmPerUnit*1000) + 500, n/(p.KmPerUnit*1000) + 500 }
	tx, ty := at(-3*ChunkMeters, -5*ChunkMeters)
	tower := w.Spawn(&WildSiteC{Name: "Старый дозор", Type: "tower", X: tx, Y: ty, Z: 24})
	cx, cy := at(-9*ChunkMeters+1, -2*ChunkMeters-3)
	cave := w.Spawn(&WildSiteC{Name: "Тёмная нора", Type: "cave", X: cx, Y: cy, Z: -18})
	return w, tr, tower.ID, cave.ID
}

func TestSpacesTowerCaveFixtures(t *testing.T) {
	w, tr, towerID, caveID := towerCaveWorld(t)

	type fx struct {
		path, id, kind string
		cx, cz         int64
	}
	cases := []fx{
		{towerFixturePath, "w:" + eidStr(towerID) + ":tower", "building", -3, -5},
		{caveFixturePath, "w:" + eidStr(caveID) + ":cave", "cave", -9, -3},
	}

	for _, c := range cases {
		v := SpacesChunk(w, tr, c.cx, c.cz)
		spaces := v["spaces"].([]map[string]any)
		// чанк выбран так, что в нём ровно одно дикое пространство — образец остаётся чистым и стабильным
		if len(spaces) != 1 || spaces[0]["id"] != c.id {
			t.Fatalf("%s: ждали ровно одно пространство %s, получили %d: %v", c.path, c.id, len(spaces), spaceIDs(spaces))
		}
		sp := spaces[0]
		if sp["kind"] != c.kind {
			t.Fatalf("%s: вид пространства %v, ждали %q", c.path, sp["kind"], c.kind)
		}
		checkSpace(t, sp)       // общие законы любого пространства (как в spaces_test.go)
		checkTightGeometry(t, sp) // плюс требования 0277: ненулевые площади/сегменты, рампа со step_count=0

		if os.Getenv("UPDATE_FIXTURES") == "1" {
			raw, _ := json.MarshalIndent(v, "", "  ")
			if err := os.WriteFile(c.path, append(raw, '\n'), 0o644); err != nil {
				t.Fatal(err)
			}
		}
		raw, err := os.ReadFile(c.path)
		if err != nil {
			t.Fatalf("нет образца %s (создать: UPDATE_FIXTURES=1 go test ./sim -run TestSpacesTowerCaveFixtures): %v", c.path, err)
		}
		live, _ := json.MarshalIndent(v, "", "  ")
		if string(append(live, '\n')) != string(raw) {
			t.Fatalf("%s разошёлся с живым ответом SpacesChunk — обнови образец (UPDATE_FIXTURES=1)", c.path)
		}
	}
}

// Башня: подземелье, несколько ярусов, обходная (плоская) кровля, лестницы между ярусами.
func TestTowerFixtureShape(t *testing.T) {
	w, tr, towerID, _ := towerCaveWorld(t)
	sp := spaceByID(SpacesChunk(w, tr, -3, -5), "w:"+eidStr(towerID)+":tower")
	if sp == nil {
		t.Fatal("башни нет в своём чанке")
	}
	floors := sp["floors"].([]map[string]any)
	if len(floors) < 5 {
		t.Fatalf("у башни ждали погреб, ярусы и кровлю: %d ярусов", len(floors))
	}
	if first := floors[0]; first["kind"] != "cellar" {
		t.Fatalf("нижний ярус башни должен быть погребом: %v", first["kind"])
	}
	if roof := floors[len(floors)-1]; roof["kind"] != "roof" {
		t.Fatalf("верхний ярус башни должен быть кровлей: %v", roof["kind"])
	}
	if len(sp["stairs"].([]map[string]any)) < 4 {
		t.Fatalf("между ярусами башни ждали несколько лестниц: %d", len(sp["stairs"].([]map[string]any)))
	}
	// все лестницы башни — ступенчатые (не рампы): положительный step_count
	for _, st := range sp["stairs"].([]map[string]any) {
		if st["kind"] == "ramp" {
			t.Fatalf("в башне не ждали рампы: %v", st["id"])
		}
		if st["step_count"].(int) <= 0 {
			t.Fatalf("у ступенчатой лестницы %v должен быть положительный step_count", st["id"])
		}
	}
}

// Пещера: устье, ход и зал; спуск — рампа с нулевым step_count, от устья к залу в глубине.
func TestCaveFixtureShape(t *testing.T) {
	w, tr, _, caveID := towerCaveWorld(t)
	sp := spaceByID(SpacesChunk(w, tr, -9, -3), "w:"+eidStr(caveID)+":cave")
	if sp == nil {
		t.Fatal("пещеры нет в чанке её устья")
	}
	floors := sp["floors"].([]map[string]any)
	if len(floors) != 2 || floors[0]["kind"] != "entrance" || floors[1]["kind"] != "cave" {
		t.Fatalf("у пещеры ждали устье и зал: %v", floors)
	}
	stairs := sp["stairs"].([]map[string]any)
	if len(stairs) != 1 || stairs[0]["kind"] != "ramp" {
		t.Fatalf("спуск пещеры должен быть ровно одной рампой: %v", stairs)
	}
	if stairs[0]["step_count"].(int) != 0 {
		t.Fatalf("у рампы пещеры step_count должен быть 0: %v", stairs[0]["step_count"])
	}
	// зал заметно ниже устья — спускаемся в глубину
	if mouth, hall := floors[0]["elevation_m"].(float64), floors[1]["elevation_m"].(float64); mouth-hall < 4 {
		t.Fatalf("зал пещеры должен быть в глубине: устье %.1f, зал %.1f", mouth, hall)
	}
}

// checkTightGeometry — требования 0277 поверх checkSpace: у каждой поверхности ненулевая площадь и
// нет нулевых соседних рёбер; полилинии лестниц без нулевых сегментов; рампа имеет step_count=0, а
// обычная лестница — строго положительный, и концы рамп/лестниц совпадают с высотами своих ярусов.
func checkTightGeometry(t *testing.T, sp map[string]any) {
	t.Helper()
	id := sp["id"].(string)
	const eps = 1e-4

	for _, s := range sp["surfaces"].([]map[string]any) {
		pts := s["points"].([]map[string]float64)
		sid := s["id"].(string)
		// соседние рёбра (с замыканием) ненулевой длины
		for i := range pts {
			a, b := pts[i], pts[(i+1)%len(pts)]
			if edgeLen(a, b) < eps {
				t.Fatalf("%s: у поверхности %s нулевое ребро на вершине %d", id, sid, i)
			}
		}
		if polygonArea3D(pts) < 1e-3 {
			t.Fatalf("%s: у поверхности %s нулевая площадь", id, sid)
		}
	}

	o := sp["origin"].(map[string]float64)
	elev := map[string]float64{}
	for _, f := range sp["floors"].([]map[string]any) {
		elev[f["id"].(string)] = f["elevation_m"].(float64)
	}
	for _, st := range sp["stairs"].([]map[string]any) {
		stid := st["id"].(string)
		steps := st["step_count"].(int)
		switch st["kind"] {
		case "ramp":
			if steps != 0 {
				t.Fatalf("%s: у рампы %s step_count=%d, ждали 0", id, stid, steps)
			}
		case "stairs":
			if steps <= 0 {
				t.Fatalf("%s: у лестницы %s step_count=%d, ждали >0", id, stid, steps)
			}
		default:
			t.Fatalf("%s: неизвестный вид лестницы %v у %s", id, st["kind"], stid)
		}
		pts := st["points"].([]map[string]float64)
		for i := 1; i < len(pts); i++ {
			if edgeLen(pts[i-1], pts[i]) < eps {
				t.Fatalf("%s: у лестницы %s нулевой сегмент на вершине %d", id, stid, i)
			}
		}
		// концы доходят ровно до своих ярусов (дублирует checkSpace, но важно именно для рамп 0277)
		from, to := elev[st["from_floor"].(string)], elev[st["to_floor"].(string)]
		first, last := pts[0], pts[len(pts)-1]
		if math.Abs(o["u"]+first["y"]-from) > 0.01 || math.Abs(o["u"]+last["y"]-to) > 0.01 {
			t.Fatalf("%s: лестница %s не доходит от яруса до яруса", id, stid)
		}
	}
}

func edgeLen(a, b map[string]float64) float64 {
	return math.Sqrt(sq(a["x"]-b["x"]) + sq(a["y"]-b["y"]) + sq(a["z"]-b["z"]))
}

func sq(v float64) float64 { return v * v }

// polygonArea3D — площадь плоского многоугольника в 3D методом Ньюэлла (сумма векторных произведений
// рёбер). Для вертикальных стен и наклонных скатов тоже верна.
func polygonArea3D(pts []map[string]float64) float64 {
	var nx, ny, nz float64
	for i := range pts {
		a, b := pts[i], pts[(i+1)%len(pts)]
		nx += a["y"]*b["z"] - a["z"]*b["y"]
		ny += a["z"]*b["x"] - a["x"]*b["z"]
		nz += a["x"]*b["y"] - a["y"]*b["x"]
	}
	return 0.5 * math.Sqrt(nx*nx+ny*ny+nz*nz)
}

func spaceIDs(spaces []map[string]any) []string {
	out := make([]string, 0, len(spaces))
	for _, sp := range spaces {
		out = append(out, sp["id"].(string))
	}
	return out
}
