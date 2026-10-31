// Пакет mind — «мысли» НПС (блок 1.4). Ровно та вещь, которой не хватало прототипу: там
// житель ждал команды и сползал в «сидит в таверне». Здесь у каждого есть нужды и тяги, и
// разум САМ выбирает цель по их остроте (utility-подход: у каждого варианта своя «полезность
// сейчас», берём сильнейший, с лёгкой костью, чтобы не было роботизма). Это чистая функция:
// она ничего не меняет в мире, только возвращает намерение — исполняет его система sim.
// Так разум можно переписывать и усложнять (планы, цели-цепочки, память) не трогая остальное.
package mind

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Goal — что НПС решил делать. Строкой, чтобы системы и клиент читали без магии.
const (
	GoalEat       = "eat"
	GoalRest      = "rest"
	GoalWork      = "work"
	GoalSocialize = "socialize"
	GoalSeekHome  = "seek_home"
	GoalLeave     = "leave"
	GoalWander    = "wander"
)

// Decide выбирает цель для сущности на ближайшие часы. Чистая: читает компоненты и кости,
// возвращает цель и сколько часов её держать. Ничего не мутирует.
func Decide(e *core.Entity, hour int, s *dice.Stream) (goal string, holdHours int64) {
	v := core.Vitals(e)
	d := core.Drives(e)
	home := core.Home(e)
	purse := core.Purse(e)
	if v == nil {
		return GoalWander, 2
	}

	night := hour >= 22 || hour < 6

	// полезность каждого варианта «прямо сейчас» (0..~1.3), с небольшим шумом кости
	type opt struct {
		goal  string
		score float64
	}
	noise := func() float64 { return s.Uniform(-0.05, 0.05) }

	eat := v.Hunger / 100.0
	if v.Hunger > 70 {
		eat += 0.3 // сильный голод перебивает почти всё
	}

	rest := (100.0 - v.Energy) / 100.0
	if night {
		rest += 0.45 // ночью тянет отдыхать
	}

	social := 0.0
	work := 0.30
	seek := 0.0
	leave := 0.0
	if d != nil {
		social = d.Social / 120.0
		work += d.Ambition / 240.0
		if home != nil && home.Status == "none" {
			seek = 0.35 + d.Dwelling/200.0
			// долгая бездомность + большая амбиция → мысль уйти искать долю
			if d.Ambition > 85 {
				leave = 0.7 + d.Ambition/300.0
			}
		}
	}
	if purse != nil && purse.Coins < 10 {
		work += 0.25 // без денег надо работать
	}
	if night {
		work -= 0.5 // ночью не работают
		social -= 0.2
	}

	opts := []opt{
		{GoalEat, eat + noise()},
		{GoalRest, rest + noise()},
		{GoalWork, work + noise()},
		{GoalSocialize, social + noise()},
		{GoalSeekHome, seek + noise()},
		{GoalLeave, leave + noise()},
	}
	best := opts[0]
	for _, o := range opts[1:] {
		if o.score > best.score {
			best = o
		}
	}
	if best.score < 0.12 {
		return GoalWander, 2 // ничто не горит — просто слоняется
	}
	// цель держится 2..4 часа, чтобы НПС не дёргался каждый тик
	return best.goal, int64(2 + s.Intn(3))
}
