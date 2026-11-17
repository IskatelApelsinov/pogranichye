package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

// Граничные тесты потолка бесконечного мира (comms 0059, часть C): потолок учитывает и деревни, и
// город; отказ по лимиту не оставляет частично открытого края.

func frontierWorld(t *testing.T) (*core.World, *core.Entity, []core.ID) {
	t.Helper()
	w, e, _ := starterPlayer(t)
	var outer []core.ID
	seen := map[core.ID]bool{}
	w.Each("settlement", func(o *core.Entity) { // по одному поселению из каждого внешнего края
		s := core.Settle(o)
		if isOuter(w, s) && !seen[s.Region] {
			seen[s.Region] = true
			outer = append(outer, o.ID)
		}
	})
	if len(outer) < 3 {
		t.Skip("нужно минимум три внешних края")
	}
	return w, e, outer
}

func numSettlements(w *core.World) int {
	n := 0
	w.Each("settlement", func(*core.Entity) { n++ })
	return n
}

func countKind(w *core.World, kind string) int {
	n := 0
	w.Each(kind, func(*core.Entity) { n++ })
	return n
}

func TestFrontierOrdinaryEdge(t *testing.T) {
	w, e, outer := frontierWorld(t)
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 100, Villages: 4, CityEvery: 3, Folk: 4})
	if OpenFrontier(w, e, outer[0]) == "" || numSettlements(w) != base+4 {
		t.Fatalf("обычный край: +4 деревни, получили %d", numSettlements(w)-base)
	}
}

func TestFrontierEdgeWithCity(t *testing.T) {
	w, e, outer := frontierWorld(t)
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 100, Villages: 2, CityEvery: 1, Folk: 4})
	if OpenFrontier(w, e, outer[0]) == "" || numSettlements(w) != base+3 {
		t.Fatalf("край с городом: +2 деревни +1 город, получили %d", numSettlements(w)-base)
	}
	cities := 0
	w.Each("settlement", func(o *core.Entity) {
		re := w.Get(core.Settle(o).Region)
		if f, _ := re.Get("frontier").(*FrontierC); f != nil && f.Ring == 1 && core.Settle(o).Tier == "city" {
			cities++
		}
	})
	if cities != 1 {
		t.Fatalf("в новом краю должен быть ровно один город: %d", cities)
	}
}

func TestFrontierExactLimitAndOneShort(t *testing.T) {
	// ровно в лимит: 2 деревни + город = 3 места
	w, e, outer := frontierWorld(t)
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 3, Villages: 2, CityEvery: 1, Folk: 4})
	if OpenFrontier(w, e, outer[0]) == "" || numSettlements(w) != base+3 {
		t.Fatal("край, точно помещающийся в лимит, должен открыться целиком")
	}
	// не хватает одного места: раньше проверялись только деревни и город превышал потолок
	w2, e2, outer2 := frontierWorld(t)
	base2 := numSettlements(w2)
	regions, people := countKind(w2, "region"), countKind(w2, "ident")
	ConfigureFrontier(w2, FrontierCfgC{Enabled: true, Max: base2 + 2, Villages: 2, CityEvery: 1, Folk: 4})
	if name := OpenFrontier(w2, e2, outer2[0]); name != "" {
		t.Fatalf("край не помещается (нужно 3, есть 2) — открываться не должен: %q", name)
	}
	if numSettlements(w2) != base2 || countKind(w2, "region") != regions || countKind(w2, "ident") != people {
		t.Fatal("отказ по лимиту не должен оставлять частично открытый край")
	}
	if numSettlements(w2) > base2+2 {
		t.Fatal("потолок превышен")
	}
	// край не помечен открытым: после поднятия потолка он откроется
	ConfigureFrontier(w2, FrontierCfgC{Enabled: true, Max: base2 + 3, Villages: 2, CityEvery: 1, Folk: 4})
	if OpenFrontier(w2, e2, outer2[0]) == "" || numSettlements(w2) != base2+3 {
		t.Fatal("после поднятия потолка тот же край должен открыться")
	}
}

func TestFrontierCityCountsOnThirdOpening(t *testing.T) {
	w, e, outer := frontierWorld(t)
	base := numSettlements(w)
	// места ровно на два обычных края и третий БЕЗ города: третий (с городом) открыться не должен
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 6, Villages: 2, CityEvery: 3, Folk: 4})
	OpenFrontier(w, e, outer[0])
	OpenFrontier(w, e, outer[1])
	if numSettlements(w) != base+4 {
		t.Fatalf("два обычных края: +4, получили %d", numSettlements(w)-base)
	}
	if OpenFrontier(w, e, outer[2]) != "" || numSettlements(w) != base+4 {
		t.Fatal("третий край требует 3 места (с городом), а есть 2 — потолок должен остановить")
	}
}

func TestFrontierReopenAndDisabled(t *testing.T) {
	w, e, outer := frontierWorld(t)
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 100, Villages: 3, Folk: 4})
	OpenFrontier(w, e, outer[0])
	n := numSettlements(w)
	if OpenFrontier(w, e, outer[0]) != "" || numSettlements(w) != n {
		t.Fatal("повторное открытие того же края ничего не добавляет")
	}
	// Max=0 и Enabled=false — мир не растёт
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: 0, Villages: 3, Folk: 4})
	if OpenFrontier(w, e, outer[1]) != "" || numSettlements(w) != n {
		t.Fatal("Max=0: мир не растёт")
	}
	ConfigureFrontier(w, FrontierCfgC{Enabled: false, Max: base + 100, Villages: 3, Folk: 4})
	if OpenFrontier(w, e, outer[1]) != "" || numSettlements(w) != n {
		t.Fatal("выключенный рост: мир не растёт")
	}
}

func TestFrontierStatsReadOnlyAndAccurate(t *testing.T) {
	w, e, outer := frontierWorld(t)
	before := w.Count()
	st := FrontierStats(w)
	if w.Count() != before || peekFrontierCfg(w) != nil {
		t.Fatal("чтение статистики не должно создавать настройки в мире")
	}
	if st["settlements"].(int) != numSettlements(w) {
		t.Fatal("счётчик поселений должен совпадать с фактом")
	}
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 100, Villages: 2, CityEvery: 1, Folk: 4})
	OpenFrontier(w, e, outer[0])
	st = FrontierStats(w)
	if st["settlements"].(int) != base+3 || st["opened"].(int) != 1 || st["max"].(int) != base+100 {
		t.Fatalf("статистика после открытия: %v", st)
	}
}

// TestStepLandsOpensOuterFrontier — comms 0234 (P0.7 "пустой горизонт"): до этого PR рост мира был
// завязан только на journey/врата (arrive→OpenFrontier), а бесшовный пеший переход между землями
// (StepLands) молча менял Place/ScenePos, не открывая следующий край и не отмечая новое село
// посещённым — пешком за свои земли игрок уходил в пустоту без новых якорей. Сценарий: Travel через
// врата в первый внешний край (ring0), затем пешком (StepLands, без тракта/врат) — в один из
// появившихся за ним сёл (ring1); этот шаг САМ должен открыть ring2, в точности как arrive().
func TestStepLandsOpensOuterFrontier(t *testing.T) {
	w, e, _ := starterPlayer(t)
	SetSeamless(w, true)
	var ring0 *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if ring0 == nil && isOuter(w, core.Settle(o)) {
			ring0 = o
		}
	})
	if ring0 == nil {
		t.Skip("нет внешнего мира")
	}
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 50, Villages: 4, Folk: 5})
	if ok, why := Travel(w, e, ring0.ID); !ok {
		t.Fatalf("врата в ring0: %s", why)
	}
	if numSettlements(w) != base+4 {
		t.Fatalf("Travel должен открыть ring1 (4 деревни): было %d, стало %d", base, numSettlements(w))
	}
	afterRing1 := numSettlements(w)

	// найти свежее село ring1 — соседнее с ring0, физически достижимое пешком (оба Outer)
	var ring1 *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if ring1 != nil || o.ID == ring0.ID {
			return
		}
		if re := w.Get(core.Settle(o).Region); re != nil {
			if f, _ := re.Get("frontier").(*FrontierC); f != nil && f.Ring == 1 {
				ring1 = o
			}
		}
	})
	if ring1 == nil {
		t.Fatal("ring1 должен существовать после Travel")
	}

	// пешком стоим прямо на землях ring1 (StepLands сам довершит переход и LearnVisited/OpenFrontier)
	s0, s1 := core.Settle(ring0), core.Settle(ring1)
	e0, n0 := Anchor(w, s0)
	e1, n1 := Anchor(w, s1)
	pl, p := core.Place(e), EnsureScenePos(w, e)
	pl.Settlement = ring0.ID
	p.X, p.Y = (e1-e0)+1, (n1-n0)+1 // на мгновение "видим" обе точки: де-факто уже внутри ring1
	before := numSettlements(w)

	StepLands(w, nil)

	if pl.Settlement != ring1.ID {
		t.Fatalf("StepLands должен перевести игрока на ring1 (%d), получили %d", ring1.ID, pl.Settlement)
	}
	if !knowsPlace(e, ring1.ID) {
		t.Fatal("StepLands должен вызвать LearnVisited для нового села — как arrive()")
	}
	if numSettlements(w) != afterRing1+4 {
		t.Fatalf("пеший переход в ring1 должен сам открыть ring2 (+4): было %d, стало %d", before, numSettlements(w))
	}
	// WorldPos (якорь нового села + локальная точка) должен совпасть с тем, где игрок физически
	// стоял до перехода (e1,n1 — якорь ring1, плюс тот же (1,1) локальный остаток).
	wx0, wy0 := e1+1, n1+1

	// повторный StepLands на тех же землях — без дублирования края и без лишнего перехода
	StepLands(w, nil)
	if pl.Settlement != ring1.ID || numSettlements(w) != afterRing1+4 {
		t.Fatal("повторный StepLands на тех же землях не должен открывать край снова")
	}
	e1b, n1b := Anchor(w, core.Settle(ring1))
	wx1, wy1 := e1b+p.X, n1b+p.Y
	if math.Abs(wx1-wx0) > 0.01 || math.Abs(wy1-wy0) > 0.01 {
		t.Fatalf("WorldPos должен остаться непрерывным: было (%.3f,%.3f), стало (%.3f,%.3f)", wx0, wy0, wx1, wy1)
	}
}

// TestStepLandsInnerDoesNotOpenFrontier — handover между двумя НЕ-outer (стартовыми) землями не
// должен открывать/растить мир: стартовый котёл конечен по концепции (ADR 0003/0006).
func TestStepLandsInnerDoesNotOpenFrontier(t *testing.T) {
	w, e, se := starterPlayer(t)
	SetSeamless(w, true)
	var other *core.Entity
	w.Each("settlement", func(o *core.Entity) {
		if other == nil && o.ID != se.ID && !isOuter(w, core.Settle(o)) {
			other = o
		}
	})
	if other == nil {
		t.Skip("нужно хотя бы два стартовых (не-outer) села")
	}
	base := numSettlements(w)
	ConfigureFrontier(w, FrontierCfgC{Enabled: true, Max: base + 50, Villages: 4, Folk: 5})

	s0, s1 := core.Settle(se), core.Settle(other)
	e0, n0 := Anchor(w, s0)
	e1, n1 := Anchor(w, s1)
	pl, p := core.Place(e), EnsureScenePos(w, e)
	pl.Settlement = se.ID
	p.X, p.Y = (e1-e0)+1, (n1-n0)+1

	StepLands(w, nil)

	if pl.Settlement != other.ID {
		t.Fatalf("handover между стартовыми землями должен сработать как раньше: получили %d", pl.Settlement)
	}
	if numSettlements(w) != base {
		t.Fatalf("стартовые земли не должны расти от пешего перехода: было %d, стало %d", base, numSettlements(w))
	}
}
