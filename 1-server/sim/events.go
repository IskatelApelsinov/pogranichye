package sim

import (
	"pogranichye/battle"
	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// --- Война и климат: РЕАЛЬНЫЕ причины бед (в т.ч. хвори), а не сезонный фон ---
// По нашей логике болезни не бывает «по календарю». Её почву создают настоящие беды: война
// (раненые, походные лагеря, скученность беженцев) и нетипичный климат (лютая зима, гнилое
// лето — холод, сырость, недород). Эти системы поднимают давление хвори (SettlementC.Strain),
// а уж вспыхнет ли мор — решает DiseaseSystem, и только если вдобавок не хватает лекарей/снадобья.
var DebugWars, DebugWarDead, DebugClimate int

// WarSystem — державы изредка сходятся в войне: на границе гибнут и калечатся ратники, растёт
// давление хвори. Война сама иссякает за срок. Потери ограничены — держава не выкашивается.
type WarSystem struct{}

func (WarSystem) Name() string { return "war" }

const (
	warDeclareChance = 0.0025 // шанс в день, что держава объявит войну соседу
	warDaysMin       = 24
	warDaysMax       = 60
	warLevyPerDay    = 3    // сколько ратников державы попадает в стычку за день
	warStrainPerDay  = 0.5  // насколько война поднимает давление хвори в задетых сёлах
	warMinAge        = 18.0 // в ратники берут мужчин призывного возраста
	warMaxAge        = 45.0
)

func (WarSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	s := hub.Stream("war")
	idx := residentIndex(w)

	// собрать державы и их столицы
	var realms []*core.Entity
	w.Each("realm", func(re *core.Entity) { realms = append(realms, re) })

	// 1) объявления войны: мирная держава может пойти на ближайшего мирного соседа
	for _, re := range realms {
		r := econ.Realm(re)
		if r == nil || r.WarWith != 0 || !s.Chance(warDeclareChance) {
			continue
		}
		foe := nearestPeacefulRealm(w, realms, re)
		if foe == nil {
			continue
		}
		fr := econ.Realm(foe)
		days := warDaysMin + s.Intn(warDaysMax-warDaysMin)
		r.WarWith, r.WarDays = foe.ID, days
		fr.WarWith, fr.WarDays = re.ID, days
		DebugWars++
		chronicle.Record(w, clock.Day(), "война", r.Name+" пошла войной на "+fr.Name)
	}

	// 2) ход войны: стычки на границе — раненые и убитые ратники, рост давления хвори
	var dead []core.ID
	for _, re := range realms {
		r := econ.Realm(re)
		if r == nil || r.WarWith == 0 {
			continue
		}
		r.WarDays--
		if r.WarDays <= 0 {
			if foe := w.Get(r.WarWith); foe != nil && econ.Realm(foe) != nil {
				chronicle.Record(w, clock.Day(), "мир", "затих спор: "+r.Name+" и "+econ.Realm(foe).Name)
			}
			r.WarWith = 0 // война выдохлась
			continue
		}
		levied := 0
		for _, sid := range r.Territory {
			if levied >= warLevyPerDay {
				break
			}
			se := w.Get(sid)
			if se == nil {
				continue
			}
			core.Settle(se).Strain += warStrainPerDay // лагеря, раненые, беженцы — почва для хвори
			for _, r2 := range idx[sid] {
				if levied >= warLevyPerDay {
					break
				}
				id := core.Ident(r2)
				v := core.Vitals(r2)
				if id == nil || v == nil || id.Sex != "м" || v.Age < warMinAge || v.Age > warMaxAge {
					continue
				}
				levied++
				// стычка: ратник против вражеского бойца (разбойник-ровня по канону боя)
				b := battle.Body(r2)
				if b == nil {
					nb := &battle.BodyC{Wounds: map[string]float64{}}
					w.Attach(r2, nb)
					b = nb
				}
				skill := 0.0
				if sk := core.Skills(r2); sk != nil {
					skill = sk.Level(id.Role)
				}
				out := battle.Resolve(skill, 0.35, "bandit", b, s)
				if !out.Win && b.Bleeding > 6 && s.Chance(0.25) {
					dead = append(dead, r2.ID) // пал в стычке
				}
			}
		}
	}
	DebugWarDead += len(dead)
	for _, id := range dead {
		w.Remove(id)
	}
}

// nearestPeacefulRealm — на кого пойти войной: мирная держава, НЕ союзник, предпочтительно близкая и
// враждебная. Союзников не трогаем (дипломатия держит мир), к врагам и далёким соседям тянет сильнее.
func nearestPeacefulRealm(w *core.World, realms []*core.Entity, self *core.Entity) *core.Entity {
	sr := econ.Realm(self)
	sc := w.Get(sr.Capital)
	if sc == nil {
		return nil
	}
	ss := core.Settle(sc)
	var best *core.Entity
	bestScore := 1e18
	for _, re := range realms {
		if re.ID == self.ID {
			continue
		}
		r := econ.Realm(re)
		if r == nil || r.WarWith != 0 {
			continue
		}
		if econ.Allied(sr, re.ID) {
			continue // на союзника не идут войной
		}
		oc := w.Get(r.Capital)
		if oc == nil {
			continue
		}
		os := core.Settle(oc)
		dx, dy := ss.X-os.X, ss.Y-os.Y
		d := dx*dx + dy*dy
		// чем враждебнее (ниже стояние) и ближе — тем «привлекательнее» как цель
		score := sr.Stand(re.ID)*1e6 + d
		if score < bestScore {
			bestScore, best = score, re
		}
	}
	return best
}

// ClimateSystem — нетипичный климат: изредка на край обрушивается лютая зима / гнилое лето /
// засуха. Это РАЗОВЫЙ удар по реальной причине (не сезон!): гибнет часть съестного и подскакивает
// давление хвори (холод, сырость, недоед). Бьёт по сёлам края разом.
type ClimateSystem struct{}

func (ClimateSystem) Name() string { return "climate" }

const (
	climateChance    = 0.0015 // шанс в день, что край накроет нетипичная напасть
	climateFoodLoss  = 0.3    // какую долю съестного губит
	climateStrainAdd = 1.2    // насколько поднимает давление хвори
)

func (ClimateSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if !clock.IsNewDay() {
		return
	}
	s := hub.Stream("climate")
	// по каждому краю — редкий шанс напасти; при ней бьём по всем его сёлам
	hit := map[core.ID]bool{}
	w.Each("region", func(re *core.Entity) {
		if s.Chance(climateChance) {
			hit[re.ID] = true
			DebugClimate++
		}
	})
	if len(hit) == 0 {
		return
	}
	w.Each("settlement", func(se *core.Entity) {
		set := core.Settle(se)
		if !hit[set.Region] {
			return
		}
		for g, v := range set.Store {
			if econ.Edible(g) {
				set.Store[g] = v * (1 - climateFoodLoss) // недород/порча от непогоды
			}
		}
		set.Strain += climateStrainAdd // холод/сырость/голод — почва для хвори
	})
}
