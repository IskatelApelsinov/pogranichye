package sim

import (
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
