package sim

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Жизненный цикл карточных партий при обрыве связи (issue #651, часть B).
//
// Партия («очко»/«дурак») живёт компонентом на теле игрока, поэтому переживает перезапуск сервера и
// короткий обрыв WS: при реконнекте клиент получает полный кадр с me.card_game и продолжает с
// доступного хода. Проблема была в другом конце жизненного цикла: если игрок ушёл или оборвался и не
// вернулся, незавершённая партия висела вечно, а StartOchko/StartDurak отвечали «ты уже играешь
// партию», и начать новую было нельзя без явного leave, которого обрушившийся клиент мог не послать.
//
// Политика здесь однозначна: короткий обрыв партия переживает, а настоящее бездействие закрывает её по
// таймауту на сервере — авторитетно, без участия клиента.

// CardGameAbandonHours — сколько игровых часов без единого хода делают партию заброшенной. Один игровой
// час мира ≈ 43 реальных минуты при штатном темпе (defaultWorldTick), так что порог — около полутора
// часов реального времени бездействия. Активный игрок стампит Touched каждым ходом и не затрагивается;
// затрагивается лишь тот, кто оборвал связь или ушёл от стола и не вернулся.
const CardGameAbandonHours int64 = 2

// TouchCardGame отмечает активную партию игрока «живой» на игровой час now. Зовётся серверным
// обработчиком card_game после каждого хода: сами функции хода (StartOchko/HitOchko/ActDurak/…) часов
// не принимают, чтобы не менять их сигнатуры и тесты, поэтому отметку ставит держащий часы обработчик.
func TouchCardGame(e *core.Entity, now int64) {
	if e == nil {
		return
	}
	if g := Ochko(e); g != nil && !g.Done {
		g.Touched = now
	}
	if g := Durak(e); g != nil && !g.Done {
		g.Touched = now
	}
}

// CardGameStale — есть ли у игрока незавершённая партия, которой не касались не меньше порога часов.
// Нулевой Touched (старые сохранения до появления поля) тоже считается заброшенным, как только игровое
// время перешагнуло порог, — такие осиротевшие партии не должны держать игрока после обновления.
func CardGameStale(e *core.Entity, now int64) bool {
	if e == nil {
		return false
	}
	if g := Ochko(e); g != nil && !g.Done && now-g.Touched >= CardGameAbandonHours {
		return true
	}
	if g := Durak(e); g != nil && !g.Done && now-g.Touched >= CardGameAbandonHours {
		return true
	}
	return false
}

// AbandonCardGame принудительно закрывает незавершённые партии игрока (заброшены по таймауту или при
// попытке начать новую поверх осиротевшей). Уже списанная при старте ставка заведению не возвращается —
// ровно как при явном leave. Не требует нахождения у стола: игрок мог уйти или оборваться. Идемпотентно:
// на уже завершённой партии ничего не меняет. Возвращает true, если что-то действительно закрыл.
func AbandonCardGame(e *core.Entity) bool {
	if e == nil {
		return false
	}
	closed := false
	if g := Ochko(e); g != nil && !g.Done {
		g.Done = true
		if g.Result == "" {
			g.Result = "abandoned"
		}
		g.Message = "Партия заброшена — ставка ушла заведению."
		closed = true
	}
	if g := Durak(e); g != nil && !g.Done {
		g.Done = true
		if g.Result == "" {
			g.Result = "abandoned"
		}
		g.Message = "Партия заброшена — ставка ушла заведению."
		closed = true
	}
	return closed
}

// CardGameSystem каждый игровой час закрывает заброшенные карточные партии, чтобы осиротевшая после
// обрыва связи партия не держала игрока вечно и не мешала сесть за новую (issue #651, часть B). Короткий
// обрыв партия переживает: состояние лежит в компоненте и восстанавливается в кадре при реконнекте;
// закрывается лишь по настоящему таймауту бездействия, измеренному игровым временем, а не одним тиком.
type CardGameSystem struct{}

func (CardGameSystem) Name() string { return "cardgame" }

func (CardGameSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if w == nil {
		return
	}
	now := clock.TotalHours
	sweep := func(e *core.Entity) {
		if e == nil || Player(e) == nil {
			return
		}
		if CardGameStale(e, now) {
			AbandonCardGame(e)
		}
	}
	if w.HasKind("ochko") {
		w.Each("ochko", sweep)
	}
	if w.HasKind("durak") {
		w.Each("durak", sweep)
	}
}
