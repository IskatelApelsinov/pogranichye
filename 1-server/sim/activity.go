package sim

import (
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Повседневные ЗАНЯТИЯ НПС. Грубую цель (пирамида Маслоу: work/rest/eat/…) уточняем до КОНКРЕТНОГО
// занятия по профессии, времени года/суток и доступным ресурсам.
//
// Подписи НАБЛЮДАТЕЛЬНО ЧЕСТНЫ (по замечанию ревью): они говорят, чем человек ЗАНЯТ или что ПЫТАЕТСЯ
// делать, либо прямо называют НЕВОЗМОЖНОСТЬ, — но не утверждают подтверждённый исход, которого
// симуляция не гарантирует. «Вышел на жатву» (занят), а не «намолотил хлеб» (исход); «промышляет в
// угодьях» (занят), а не «добыл зверя»; «у холодного горна — нет руды» (невозможность). Где дёшево,
// подпись считается из тех же предусловий, что и экономика (есть ли зерно на сев, руда, сырьё, дичь).
//
// Код занятия (code) — внутренний (для тестов), НЕ входит в словарь намерений игрока из контракта.
// Label кладётся в уже существующее поле `last` — новых полей кадра нет, контракт не меняется.

type activity struct{ code, label string }

// localResources — дешёвые сигналы доступности в поселении сейчас (склад/фауна/сезон), по которым у
// занятия есть или нет реальной причины состояться.
type localResources struct {
	gameNearby  bool // в угодьях есть зверьё — есть на кого выйти на промысел
	oreAround   bool // в селе есть руда — кузнецу/рудокопу есть с чем работать
	fiber       bool // шерсть/лён/шкуры — ткачу есть из чего ткать
	tavernDrink bool // в корчме есть медовуха — есть чем угоститься
	canSow      bool // зерна в закромах общины хватает на засев хотя бы одной пашни
}

// settlementResources снимает сигналы с поселения (дёшево, без сканирования угодий).
func settlementResources(se *core.Entity, season core.Season) localResources {
	s := core.Settle(se)
	store := s.Store
	game := false
	if f := battle.Fauna(se); f != nil {
		for _, p := range f.Pop {
			if p >= 1 {
				game = true
				break
			}
		}
	}
	return localResources{
		gameNearby:  game,
		oreAround:   store[econ.GoodOre] >= 1,
		fiber:       store[econ.GoodWool]+store[econ.GoodFlax]+store[econ.GoodHide] >= 1,
		tavernDrink: store[econ.GoodMead] >= 1,
		canSow:      store[econ.GoodWheat] >= econ.SeedPerField(),
	}
}

// chooseActivity уточняет цель до занятия. Чистая функция от входов — тестируется без мира.
func chooseActivity(role, goal string, season core.Season, hour int, r localResources) activity {
	night := hour >= 22 || hour < 6
	switch goal {
	case "eat":
		return activity{"meal", "ест"}
	case "rest":
		if night {
			return activity{"sleep", "спит"}
		}
		return activity{"rest_idle", "отдыхает"}
	case "socialize":
		// фактический исход (угостился ли медовухой) решает и списывает execGoal — здесь лишь намерение
		if r.tavernDrink {
			return activity{"drink_tavern", "идёт в корчму"}
		}
		return activity{"visit_neighbors", "коротает время с соседями"}
	case "seek_home":
		return activity{"seek_home", "ищет, где осесть"}
	case "leave":
		return activity{"travel", "в пути за лучшей долей"}
	case "work":
		return workActivity(role, season, r)
	}
	return activity{"wander", "бродит"}
}

// workActivity — чем ЗАНЯТ работник по профессии/сезону/наличию сырья. Подписи — о занятости или
// невозможности, не о подтверждённом результате.
func workActivity(role string, season core.Season, r localResources) activity {
	switch role {
	case "farmer":
		switch season {
		case core.Spring:
			if r.canSow {
				return activity{"farm_sow", "вышел на сев"}
			}
			return activity{"farm_noseed", "ладит пашню — нет зерна на сев"} // невозможность
		case core.Autumn:
			return activity{"farm_reap", "вышел на жатву"} // занят жатвой (намолот — дело поля)
		case core.Winter:
			return activity{"farm_mend", "чинит снасть в избе"} // поле спит
		default:
			return activity{"farm_tend", "правит в поле"}
		}
	case "fisher":
		if season == core.Spring {
			return activity{"fisher_mend", "чинит сети — нерест, лов закрыт"}
		}
		return activity{"fish_river", "на реке с сетью"}
	case "hunter":
		if r.gameNearby {
			return activity{"hunt_wild", "промышляет в угодьях"} // занят промыслом, не «добыл»
		}
		return activity{"hunter_empty", "вернулся пустым — зверь ушёл"}
	case "cook":
		return activity{"cook_hearth", "у печи"}
	case "herbalist":
		if season == core.Winter {
			return activity{"brew_remedy", "у котла со снадобьем"} // травы под снегом не рвут
		}
		return activity{"forage_herbs", "на травяном лугу за травами"}
	case "smith":
		if r.oreAround {
			return activity{"smith_forge", "кует у горна"}
		}
		return activity{"smith_cold", "у холодного горна — нет руды"}
	case "weaver":
		if r.fiber {
			return activity{"weave", "за ткацким станом"}
		}
		return activity{"weaver_wait", "ждёт пряжу — нет сырья"}
	case "miner":
		if r.oreAround {
			return activity{"mine_ore", "в шахте за рудой"}
		}
		return activity{"mine_spent", "жила скудеет"}
	case "woodcutter":
		return activity{"chop_wood", "рубит лес"}
	case "herder":
		if season == core.Winter {
			return activity{"herd_barn", "при скоте в хлеву"}
		}
		return activity{"herd_graze", "пасёт стадо"}
	case "tavernkeeper":
		return activity{"keep_tavern", "за стойкой корчмы"}
	case "mage":
		// расход маны/плетение решает MagicSystem и сам ставит честную подпись; здесь — занятость
		return activity{"study_mana", "упражняется в плетениях"}
	}
	return activity{"work", "трудится"}
}
