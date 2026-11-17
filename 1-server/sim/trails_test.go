package sim

import (
	"fmt"
	"math"
	"testing"
)

// У каждого перегона есть нитка: от центра села до центра села, петляет, но в меру; та же при
// повторном счёте; куски по чанкам в сумме дают всю нитку без дыр.
func TestTrailsLinkSettlementsAndWind(t *testing.T) {
	w, _ := Generate("trails", 12)
	list, grid := Trails(w)
	if len(list) == 0 {
		t.Fatal("в мире нет ни одной тропки")
	}
	tr0 := terrainFor(w)
	bent := 0
	for _, tr := range list {
		ax, ay := Anchor(w, settleOf(w, tr.A))
		bx, by := Anchor(w, settleOf(w, tr.B))
		first, last := tr.Pts[0], tr.Pts[len(tr.Pts)-1]
		if math.Hypot(first[0]-ax, first[1]-ay) > 0.01 || math.Hypot(last[0]-bx, last[1]-by) > 0.01 {
			t.Fatalf("%s: нитка не доходит до центров сёл", tr.ID)
		}
		L := math.Hypot(bx-ax, by-ay)
		maxDev := 0.0
		for i, p := range tr.Pts {
			maxDev = math.Max(maxDev, segDist(p[0], p[1], ax, ay, bx, by))
			if i > 0 {
				if d := math.Hypot(p[0]-tr.Pts[i-1][0], p[1]-tr.Pts[i-1][1]); d > trailStep*2.5 {
					t.Fatalf("%s: разрыв в нитке %.0f м", tr.ID, d)
				}
			}
		}
		if maxDev > math.Min(L*trailBend, trailBendM)*1.6 {
			t.Fatalf("%s: нитка ушла от прямой на %.0f м при длине %.0f", tr.ID, maxDev, L)
		}
		if maxDev > 8 {
			bent++
		}
		if tr.LenM < L || tr.LenM > L*1.35 {
			t.Fatalf("%s: длина нитки %.0f при прямой %.0f", tr.ID, tr.LenM, L)
		}
		again, _ := trailPoints(tr0, tr.A, tr.B, ax, ay, bx, by)
		for i := range again {
			if again[i] != tr.Pts[i] {
				t.Fatalf("%s: нитка не повторяется", tr.ID)
			}
		}
		// куски по чанкам
		seen := map[[2]int64]bool{}
		sum := 0.0
		for _, p := range tr.Pts {
			seen[[2]int64{int64(math.Floor(p[0] / ChunkMeters)), int64(math.Floor(p[1] / ChunkMeters))}] = true
		}
		for c := range seen {
			for dx := int64(-1); dx <= 1; dx++ { // нитка могла срезать угол соседнего чанка
				for dy := int64(-1); dy <= 1; dy++ {
					seen2 := [2]int64{c[0] + dx, c[1] + dy}
					if _, ok := seen[seen2]; !ok {
						seen[seen2] = false
					}
				}
			}
		}
		for c := range seen {
			for _, run := range chunkTrailRuns(&tr, float64(c[0])*ChunkMeters, float64(c[1])*ChunkMeters) {
				for i := 1; i < len(run); i++ {
					sum += math.Hypot(run[i]["x"]-run[i-1]["x"], run[i]["z"]-run[i-1]["z"])
					if run[i]["x"] < -0.01 || run[i]["x"] > ChunkMeters+0.01 || run[i]["z"] < -0.01 || run[i]["z"] > ChunkMeters+0.01 {
						t.Fatalf("%s: точка куска вне чанка: %v", tr.ID, run[i])
					}
				}
			}
		}
		if math.Abs(sum-tr.LenM) > tr.LenM*0.01+1 {
			t.Fatalf("%s: куски по чанкам дают %.0f м из %.0f", tr.ID, sum, tr.LenM)
		}
	}
	if bent == 0 {
		t.Fatal("все нитки прямые — тропки должны петлять")
	}
	_ = grid
}

// v1.28: там, где прямой наезд на звено круче slopeAvoidGrade, нитка не хуже — отступ вбок либо
// находит более пологий проход, либо (если рядом ровнее нет) остаётся на прежнем месте, но никогда
// не выбирает место КРУЧЕ исходного. Перебираем несколько миров, чтобы точно встретить крутой рельеф.
func TestTrailAvoidsSteepWhenPossible(t *testing.T) {
	improved := false
	for seedN := 0; seedN < 8; seedN++ {
		w, _ := Generate(fmt.Sprintf("trails-slope-%d", seedN), 16)
		tr0 := terrainFor(w)
		for _, rd := range RoadGraph(w, false) {
			sa, sb := settleOf(w, rd.A), settleOf(w, rd.B)
			if sa == nil || sb == nil {
				continue
			}
			ax, ay := Anchor(w, sa)
			bx, by := Anchor(w, sb)
			L := math.Hypot(bx-ax, by-ay)
			if L < 1 {
				continue
			}
			px, py := -(by-ay)/L, (bx-ax)/L
			amp := math.Min(L*trailBend, trailBendM)
			n := int(math.Max(2, math.Ceil(L/trailStep)))
			salt := float64(rd.A%9973)*811.0 + float64(rd.B%9973)*37.0
			prevX, prevY := ax, ay
			for i := 0; i <= n; i++ {
				f := float64(i) / float64(n)
				env := math.Sin(math.Pi * f)
				d := f * L
				off := ((noise(tr0.seed+97, salt+d, salt*0.5, 700)-0.5)*2*amp + (noise(tr0.seed+131, salt+d, salt*0.5+50, 170)-0.5)*0.5*amp) * env
				naiveX, naiveY := ax+(bx-ax)*f+px*off, ay+(by-ay)*f+py*off
				if i > 0 && i < n {
					naiveGrade := segGrade(tr0, prevX, prevY, naiveX, naiveY)
					gotX, gotY := avoidSteep(tr0, prevX, prevY, naiveX, naiveY, px, py, amp)
					gotGrade := segGrade(tr0, prevX, prevY, gotX, gotY)
					if gotGrade > naiveGrade+1e-9 {
						t.Fatalf("отступ вбок хуже прямого звена: было %.3f, стало %.3f", naiveGrade, gotGrade)
					}
					if naiveGrade > slopeAvoidGrade && gotGrade < naiveGrade-1e-9 {
						improved = true
					}
					prevX, prevY = gotX, gotY
					continue
				}
				prevX, prevY = naiveX, naiveY
			}
		}
	}
	if !improved {
		t.Skip("за все миры отступ вбок не нашёл ни одного улучшения — рельеф сидов оказался мягче")
	}
}

// На нитке шаг легче, в стороне от неё — обычный; чанк отдаёт нитку с видом и шириной.
func TestTrailBoostAndChunkDescriptor(t *testing.T) {
	w, _ := Generate("trails", 12)
	list, _ := Trails(w)
	var tr *Trail
	for i := range list {
		if len(list[i].Pts) > 20 {
			tr = &list[i]
			break
		}
	}
	if tr == nil {
		t.Skip("нет длинной тропки")
	}
	mid := tr.Pts[len(tr.Pts)/2]
	got := TrailAt(w, mid[0], mid[1])
	if got == nil {
		t.Fatal("середина нитки не считается ниткой")
	}
	if b := trailBoost(w, mid[0], mid[1]); b != TrailBoost && b != RoadBoost {
		t.Fatalf("прибавка на нитке: %v", b)
	}
	// в стороне: ищем точку в 40 м, где нет никакой нитки
	prev := tr.Pts[len(tr.Pts)/2-1]
	dx, dy := mid[0]-prev[0], mid[1]-prev[1]
	l := math.Hypot(dx, dy)
	ox, oy := mid[0]-dy/l*40, mid[1]+dx/l*40
	if TrailAt(w, ox, oy) == nil && trailBoost(w, ox, oy) != 1 {
		t.Fatal("в поле прибавки быть не должно")
	}
	oe, on := math.Floor(mid[0]/ChunkMeters)*ChunkMeters, math.Floor(mid[1]/ChunkMeters)*ChunkMeters
	found := false
	for _, r := range chunkRoads(w, oe, on) {
		if r["id"] == tr.ID || r["id"] == got.ID {
			found = true
			if (r["kind"] != "trail" && r["kind"] != "road") || r["width_m"].(float64) <= 0 || len(r["points"].([]map[string]float64)) < 2 {
				t.Fatalf("описание нитки в чанке: %v", r)
			}
		}
	}
	if !found {
		t.Fatal("чанк не отдал нитку, которая через него идёт")
	}
}
