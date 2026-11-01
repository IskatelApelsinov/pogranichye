package sim

import (
	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

// Повседневные ЗАНЯТИЯ НПС. Грубую цель (пирамида Маслоу: work/rest/eat/…) уточняем до КОНКРЕТНОГО
// занятия по профессии, времени года и суток и доступным ресурсам. У каждого занятия — реальная
// причина: нет дичи в угодьях — охотник не «охотится», а остался без добычи; зимой поле спит —
// землепашец чинит снасть, а не сеет; нет руды — кузнец у холодного горна. Это НЕ новые поля кадра
// и НЕ изменение контракта: занятие ложится в уже существующее человекочитаемое поле `last`.
//
// Код занятия (code) — внутренний (для тестов/будущей структуризации), НЕ входит в словарь
// намерений игрока из контракта. Label — строка для `last`.

type activity struct{ code, label string }

// localResources — что РЕАЛЬНО доступно в этом поселении сейчас (дешёвые сигналы из склада/фауны/
// маны/сезона, без сканирования угодий). От них зависит, есть ли у занятия причина состояться.
type localResources struct {
	gameNearby   bool // в угодьях есть зверьё (фауна) — есть на кого охотиться
	oreAround    bool // в селе есть руда — кузнецу/рудокопу есть с чем работать
	fiber        bool // шерсть/лён/шкуры — ткачу есть из чего ткать
	tavernDrink  bool // в корчме есть медовуха — есть за чем идти в корчму
	herbsSeason  bool // не зима — травы можно собирать
	placeOfPower bool // мана места высока — магу есть откуда черпать
}

// settlementResources снимает дешёвые сигналы доступности с поселения (склад, фауна, мана, сезон).
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
		gameNearby:   game,
		oreAround:    store[econ.GoodOre] >= 1,
		fiber:        store[econ.GoodWool]+store[econ.GoodFlax]+store[econ.GoodHide] >= 1,
		tavernDrink:  store[econ.GoodMead] >= 1,
		herbsSeason:  season != core.Winter,
		placeOfPower: s.Mana >= 1.3,
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
		if r.tavernDrink {
			return activity{"drink_tavern", "в корчме за кружкой"}
		}
		return activity{"visit_neighbors", "в гостях у соседей"}
	case "seek_home":
		return activity{"seek_home", "ищет, где осесть"}
	case "leave":
		return activity{"travel", "в пути за лучшей долей"}
	case "work":
		return workActivity(role, season, r)
	}
	return activity{"wander", "бродит"}
}

// workActivity — конкретный труд по профессии, сезону и наличию сырья/дичи.
func workActivity(role string, season core.Season, r localResources) activity {
	switch role {
	case "farmer":
		switch season {
		case core.Spring:
			return activity{"farm_sow", "сеет яровые"}
		case core.Autumn:
			return activity{"farm_reap", "жнёт хлеб"}
		case core.Winter:
			return activity{"farm_mend", "чинит снасть в избе"} // поле спит
		default:
			return activity{"farm_tend", "правит в поле"}
		}
	case "fisher":
		if season == core.Spring {
			return activity{"fisher_mend", "чинит сети — нерест, лов закрыт"}
		}
		return activity{"fish_river", "ловит рыбу на реке"}
	case "hunter":
		if r.gameNearby {
			return activity{"hunt_wild", "бьёт дичь в угодьях"}
		}
		return activity{"hunter_empty", "вернулся пустым — зверь ушёл"}
	case "cook":
		return activity{"cook_hearth", "печёт у очага"}
	case "herbalist":
		if season == core.Winter {
			return activity{"brew_remedy", "варит снадобье"} // травы под снегом не рвут
		}
		return activity{"forage_herbs", "собирает травы на лугу"}
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
			return activity{"mine_ore", "бьёт руду в шахте"}
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
		if r.placeOfPower {
			return activity{"study_power", "черпает ману места силы"}
		}
		return activity{"study_mana", "упражняется в плетениях"}
	}
	return activity{"work", "трудится"}
}
