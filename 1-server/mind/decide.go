// Пакет mind — «мысли» НПС (блок 1.4). Здесь мы НАТЯГИВАЕМ ПИРАМИДУ МАСЛОУ на конечный
// автомат A-Life: житель не получает приказ «береги зерно», он сам рассуждает снизу вверх по
// ярусам нужд. Нижний неудовлетворённый ярус перебивает верхние (голодному не до амбиций).
//
//  1. Выживание (физиология): голод, усталость — поесть / отдохнуть ПРЯМО СЕЙЧАС.
//  2. Безопасность: обеспечить БУДУЩУЮ еду и кров. Тут и рождается бережливость: «я живу
//     в деревне, мне надо есть; земледелец — значит растить (и беречь зерно на сев);
//     горожанин-ремесленник этим не занят — значит нужен доход, чтобы купить». Голод деревни
//     толкает работника его ремеслом закрывать эту нужду.
//  3. Принадлежность: тяга к людям (таверна, свои).
//  4. Признание: честолюбие — расти навыком/достатком или уйти за лучшей долей.
//
// Decide — чистая функция: читает состояние жителя и ВОСПРИЯТИЕ среды (Context), возвращает
// цель. Ничего не мутирует — исполняет цель система sim. Так разум усложняется (планы, память,
// покупка еды за монеты) не трогая остального.
package mind

import (
	"pogranichye/core"
	"pogranichye/dice"
)

const (
	GoalEat       = "eat"
	GoalRest      = "rest"
	GoalWork      = "work" // землепашец/промысловик — добыть еду; прочие — заработок
	GoalSocialize = "socialize"
	GoalSeekHome  = "seek_home"
	GoalLeave     = "leave"
	GoalWander    = "wander"
)

// Context — то, что житель ВОСПРИНИМАЕТ о своём месте. Среда для рассуждения о безопасности:
// сыта ли деревня (сколько дней еды на душу). Так агент «знает», грозит ли голод.
type Context struct {
	Hour     int
	Season   core.Season
	FoodDays float64 // запас еды деревни в днях на душу (мера продовольственной безопасности)
}

// порог «деревне грозит голод»: меньше стольких дней запаса — безопасность под угрозой.
const foodSecureDays = 12.0

// пищевые ремёсла добывают саму еду; прочие кормятся с общего котла, но работают ради дохода/благ.
func isFoodProducer(role string) bool {
	switch role {
	case "farmer", "fisher", "hunter":
		return true
	}
	return false
}

// Decide выбирает цель по пирамиде нужд. Нижний неудовлетворённый ярус побеждает.
func Decide(e *core.Entity, ctx Context, s *dice.Stream) (goal string, holdHours int64) {
	v := core.Vitals(e)
	if v == nil {
		return GoalWander, 2
	}
	d := core.Drives(e)
	home := core.Home(e)
	id := core.Ident(e)
	night := ctx.Hour >= 22 || ctx.Hour < 6
	hold := int64(2 + s.Intn(3)) // цель держится 2..4 ч, чтобы не дёргаться каждый час

	// ── Ярус 1: ВЫЖИВАНИЕ (перебивает всё) ──
	if v.Hunger > 65 { // остро голоден — есть
		return GoalEat, hold
	}
	if night || v.Energy < 18 { // ночь или без сил — отдыхать
		return GoalRest, hold
	}

	// ── Ярус 2: БЕЗОПАСНОСТЬ — обеспечить будущую еду и кров ──
	// деревне грозит голод → закрыть нужду своим ремеслом (вот откуда «растить и беречь зерно»)
	if ctx.FoodDays < foodSecureDays && v.Energy > 10 {
		role := ""
		if id != nil {
			role = id.Role
		}
		if isFoodProducer(role) {
			return GoalWork, hold // землепашец/рыбак/охотник — ДОБЫТЬ еду
		}
		// горожанин-ремесленник сам еду не растит — работает ради дохода (купить, когда будет рынок)
		return GoalWork, hold
	}
	// кров: нет своего угла и есть на что осесть
	if home != nil && home.Status == "none" {
		if p := core.Purse(e); p != nil && p.Coins >= 25 {
			return GoalSeekHome, hold
		}
	}

	// ── Ярус 3: ПРИНАДЛЕЖНОСТЬ ──
	if d != nil && d.Social > 60 && !night {
		return GoalSocialize, hold
	}

	// ── Ярус 4: ПРИЗНАНИЕ ──
	if d != nil {
		// большое честолюбие без роста и без своего дома → уйти искать долю
		if d.Ambition > 85 && home != nil && home.Status == "none" {
			return GoalLeave, hold
		}
		if d.Ambition > 40 { // растить навык/достаток трудом
			return GoalWork, hold
		}
	}

	// базовое: заняться делом или слоняться
	if v.Energy > 30 && !night {
		return GoalWork, hold
	}
	return GoalWander, 2
}
