package sim

import (
	"pogranichye/core"
	"pogranichye/econ"
)

// Занятия-помощь селу (ROADMAP P0: «помощь в поле/кузнице» как реальные действия, не только
// личное ChopWood/FishCatch/Craft). Farmwork и Forge — труд В ПОЛЬЗУ ПОСЕЛЕНИЯ (как Build: исход
// в общий склад, не в суму игрока), а не личная добыча. Поэтому они используют ТУ ЖЕ пашню/запас
// и ТЕ ЖЕ пропорции, что фоновая EconomySystem (econ/economy.go) — сев/жатва/плавка не задвоятся
// и не ломают баланс цепочек: игрок просто подставляет руки раньше или вместо фоновых работников.
// Заработок — обычная батрацкая плата рынка (econ.Wage), как и у всех жителей.

const forgeOreCost = 2.0 // руды за одну подмогу у горна (вполовину дневной выработки одного кузнеца)

// fieldForWork — незанятая общинная пашня этого села под сев (весной, если в закромах есть зерно
// на сев) или под жатву (осенью, если вызрела). nil — сейчас в поле нечего делать. Использует те
// же предикаты (Sowable/RipeForHarvest), что и фоновая экономика — сев/жатва не задвоятся.
func fieldForWork(w *core.World, seID core.ID, season core.Season, canSow bool) (*econ.ResourceNodeC, string) {
	var sow, reap *econ.ResourceNodeC
	w.Each("resnode", func(ne *core.Entity) {
		n := econ.Node(ne)
		if n == nil || n.Node != econ.NodeField || n.Settlement != seID {
			return
		}
		if reap == nil && season == core.Autumn && n.RipeForHarvest() {
			reap = n
		}
		if sow == nil && season == core.Spring && canSow && n.Sowable() {
			sow = n
		}
	})
	if reap != nil {
		return reap, "reap"
	}
	if sow != nil {
		return sow, "sow"
	}
	return nil, ""
}

// Farmwork — помочь в поле: весной досеять незасеянную пашню (если в закромах села есть зерно на
// сев), осенью — собрать вызревший урожай в общий закром. Та же пашня и тот же закон «сколько
// посеял — то и жнёшь», что у фоновой EconomySystem: сев/жатва решаются по тем же полям
// (Sown/Ripe), поэтому игрок не удваивает фоновую работу, а подставляет руки раньше неё.
func Farmwork(w *core.World, e *core.Entity, season core.Season, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) || !atPoi(e, "garden") {
		return false, "надо подойти к полю"
	}
	if why := siteLacks(w, e, "garden"); why != "" {
		return false, why
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < 10 {
		return false, "нет сил работать в поле"
	}
	se := settlementEnt(w, e)
	if se == nil {
		return false, "ты не в поселении"
	}
	s := core.Settle(se)
	if s == nil {
		return false, "нет полей"
	}
	if s.Store == nil {
		s.Store = map[string]float64{}
	}
	canSow := s.Store[econ.GoodWheat] >= econ.SeedPerField()
	field, action := fieldForWork(w, se.ID, season, canSow)
	if field == nil {
		switch season {
		case core.Spring:
			return false, "сеять нечем — в закромах мало зерна"
		case core.Autumn:
			return false, "жать пока нечего — не вызрело"
		}
		return false, "сейчас в поле не до того — не сезон"
	}
	switch action {
	case "sow":
		s.Store[econ.GoodWheat] -= econ.SeedPerField()
		field.SownSeed = econ.SeedPerField()
		field.Sown = true
		field.Ripe = 0
	case "reap":
		s.Store[econ.GoodWheat] += field.SownSeed * econ.AverageHarvestRatio()
		field.Sown = false
		field.SownSeed = 0
		field.Ripe = 0
	}
	v.Energy -= 10
	econ.Wage(se, e, "farmer")
	trainSkill(w, e, "farmer", 0.6)
	pc.markDone(reqid)
	return true, ""
}

// Forge — подсобить в кузнице: выплавить металл ДЛЯ СЕЛА (общий склад, не сума игрока) из общего
// запаса руды, той же пропорцией, что у фоновой кузни (econ.MetalFromOre) — не отдельная, более
// щедрая мини-игра. Платит рынок обычным батрацким заработком (econ.Wage).
func Forge(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) || !atPoi(e, "smithy") {
		return false, "нужна кузня"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < 10 {
		return false, "нет сил у горна"
	}
	se := settlementEnt(w, e)
	if se == nil {
		return false, "ты не в поселении"
	}
	s := core.Settle(se)
	if s == nil {
		return false, "нет кузни"
	}
	if s.Store == nil {
		s.Store = map[string]float64{}
	}
	if s.Store[econ.GoodOre] < forgeOreCost {
		return false, "на складе не хватает руды"
	}
	s.Store[econ.GoodOre] -= forgeOreCost
	s.Store[econ.GoodMetal] += forgeOreCost * econ.MetalFromOre()
	v.Energy -= 10
	econ.Wage(se, e, "smith")
	trainSkill(w, e, "smith", 0.6)
	pc.markDone(reqid)
	return true, ""
}
