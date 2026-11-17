package sim

import (
	"math"
	"strings"
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

// Находки: есть в поле, детерминированы, не у самого села и не на тропе; одна и та же находка в
// соседних чанках не повторяется; взять можно только вплотную; запас убывает и отрастает за сутки.
func TestGatherNodesInFieldsAndPickUp(t *testing.T) {
	w, e, se := starterPlayer(t)
	SetSeamless(w, true)
	tr := terrainFor(w)
	ae, an := Anchor(w, core.Settle(se))
	nodes := GatherNodesNear(w, tr, ae+600, an, 300)
	if len(nodes) < 5 {
		t.Fatalf("в поле в 600 м от села почти нет находок: %d", len(nodes))
	}
	kinds := map[string]bool{}
	for _, n := range nodes {
		kinds[n.Kind.ID] = true
		if nearestAnchorDist(tr, n.E, n.N) < gatherClearR {
			t.Fatalf("находка %s у самого села", n.ID)
		}
		if again, ok := GatherNodeByID(w, tr, n.ID); !ok || again.E != n.E || again.Kind != n.Kind {
			t.Fatalf("находка %s не повторяется по id", n.ID)
		}
	}
	if len(kinds) < 3 {
		t.Fatalf("находки однообразны: %v", kinds)
	}
	// чанки: каждая находка ровно в одном
	seen := map[string]int{}
	for cx := int64(-2); cx <= 2; cx++ {
		for cz := int64(-2); cz <= 2; cz++ {
			oe, on := ChunkOrigin(int64(math.Floor((ae+600)/ChunkMeters))+cx, int64(math.Floor(an/ChunkMeters))+cz)
			for _, g := range chunkGather(w, tr, oe, on) {
				seen[g["id"].(string)]++
				if g["x"].(float64) < 0 || g["x"].(float64) >= ChunkMeters || g["z"].(float64) < 0 || g["z"].(float64) >= ChunkMeters {
					t.Fatalf("находка вне чанка: %v", g)
				}
			}
		}
	}
	for id, c := range seen {
		if c != 1 {
			t.Fatalf("находка %s в %d чанках", id, c)
		}
	}

	// сбор
	var n GatherNode
	for _, x := range nodes {
		if x.Kind.ID != "hive" {
			n = x
			break
		}
	}
	p := ScenePos(e)
	p.X, p.Y = n.E-ae+10, n.N-an
	if ok, why := PickUp(w, e, n.ID, "r1"); ok || !strings.Contains(why, "далеко") {
		t.Fatalf("издалека взять нельзя: %v %q", ok, why)
	}
	p.X, p.Y = n.E-ae+1, n.N-an
	before := econ.Stash(e)
	had := 0.0
	if before != nil {
		had = before.Goods[n.Kind.Good]
	}
	if ok, why := PickUp(w, e, n.ID, "r2"); !ok {
		t.Fatalf("сбор вплотную не удался: %q", why)
	}
	PickUp(w, e, n.ID, "r2") // повтор того же запроса — не берёт второй раз
	got := econ.Stash(e).Goods[n.Kind.Good] - had
	if got < 1 || got > 2 {
		t.Fatalf("в поклаже прибавилось %.0f", got)
	}
	for i := 3; i < 10; i++ {
		PickUp(w, e, n.ID, "r"+string(rune('0'+i)))
	}
	if ok, why := PickUp(w, e, n.ID, "rX"); ok || !strings.Contains(why, "отрастёт") {
		t.Fatalf("выбранная находка должна быть пуста: %v %q", ok, why)
	}
	wildUse(w).Today += 1
	if ok, _ := PickUp(w, e, n.ID, "rY"); !ok {
		t.Fatal("за сутки находка должна отрасти")
	}
	v := GatherView(w, tr, e)
	if len(v) == 0 || v[0]["id"] != n.ID || v[0]["here"] != true {
		t.Fatalf("place.gather: ближайшая — та, у которой стоишь: %v", v[0])
	}
}

// Дикий улей без огня в руке жалит, с факелом — нет. Самоход доводит вплотную к находке.
func TestHiveStingsWithoutFireAndWalkToNode(t *testing.T) {
	w, e, se := starterPlayer(t)
	SetSeamless(w, true)
	tr := terrainFor(w)
	ae, an := Anchor(w, core.Settle(se))
	var hive GatherNode
	for r := 400.0; hive.ID == "" && r < 4000; r += 400 {
		for _, n := range GatherNodesNear(w, tr, ae+r, an, 400) {
			if n.Kind.ID == "hive" {
				hive = n
				break
			}
		}
	}
	if hive.ID == "" {
		t.Skip("нет дикого улья поблизости")
	}
	p := ScenePos(e)
	p.X, p.Y = hive.E-ae-60, hive.N-an
	if ok, why := WalkToSite(w, e, hive.ID); !ok {
		t.Fatalf("к улью не пойти: %q", why)
	}
	ctl := func(id core.ID) bool { return id == e.ID }
	for i := 0; i < 600 && e.Get("autowalk") != nil; i++ {
		StepAutoWalk(w, 0.1, ctl)
		StepLands(w, ctl)
	}
	x, y, _ := bodyWorldXY(w, e)
	if d := math.Hypot(hive.E-x, hive.N-y); d > gatherReach {
		t.Fatalf("самоход не довёл до находки: %.1f м", d)
	}
	core.Vitals(e).Health = 100
	if ok, _ := PickUp(w, e, hive.ID, "h1"); !ok {
		t.Fatal("мёд не взять")
	}
	if core.Vitals(e).Health >= 100 {
		t.Fatal("без огня пчёлы должны жалить")
	}
	if econ.Stash(e).Goods[econ.GoodHoney] < 1 {
		t.Fatal("мёда нет в поклаже")
	}
}
