package sim

import (
	"math"

	"pogranichye/core"
)

// StepMigrants даёт прямому (один прогон дорожного графа, соседние сёла) переселению НПС
// настоящее тело в бесшовном мире: вместо того чтобы всё время пути оставаться невидимым, пока
// MigrationC.ProgressHours тикает к TotalHours (старый, полностью абстрактный переход — он
// остаётся единственным механизмом для многошаговых маршрутов, горных перевалов и небесшовных
// миров, см. MigrationSystem), идущий получает ScenePosC на прямой между якорями двух сёл — в той
// же доле ProgressHours/TotalHours, что и раньше продвигалась каждый игровой час. Скорость, еда и
// момент прибытия не меняются ни на йоту: это делает уже существующий переход видимым и
// пешеходным, не трогая баланс (comms 0174 — приоритет B; comms 0179 — RFC с этим же планом).
//
// Жители с MigrationC исключены из residentsOf/residentIndex (см. systems.go), поэтому задания и
// нужды поселения их не трогают, пока они в пути; то же освобождает их от архивации/материализации
// по теплу — ровно как у игрока, тело не снимается и не переставляется чужим кодом.
func StepMigrants(w *core.World) {
	if w == nil || !Seamless(w) {
		return
	}
	w.Each("migration", func(e *core.Entity) {
		m := Migration(e)
		if m == nil || Player(e) != nil || len(m.Route) != 2 || m.TotalHours <= 0 {
			return // многошаговый/перевальный маршрут — абстрактный путь не трогаем (ADR 0003)
		}
		src, dst := settleOf(w, m.From), settleOf(w, m.To)
		if src == nil || dst == nil || isOuter(w, src) != isOuter(w, dst) {
			return
		}
		fx, fy := Anchor(w, src)
		tx, ty := Anchor(w, dst)
		frac := m.ProgressHours / m.TotalHours
		switch {
		case frac < 0:
			frac = 0
		case frac > 1:
			frac = 1
		}
		wx := fx + (tx-fx)*frac
		wy := fy + (ty-fy)*frac
		// Чья земля под ногами сейчас — тот же критерий, что у StepLands для игроков (ближайший
		// якорь), только выбор ограничен двумя концами этого прямого перехода.
		owner, ox, oy := m.From, fx, fy
		if math.Hypot(wx-tx, wy-ty) < math.Hypot(wx-fx, wy-fy) {
			owner, ox, oy = m.To, tx, ty
		}
		pl := core.Place(e)
		if pl == nil {
			pl = &core.PlaceC{}
			w.Attach(e, pl)
		}
		pl.Settlement = owner
		p := ScenePos(e)
		if p == nil {
			p = &ScenePosC{Stam: 1}
			w.Attach(e, p)
		}
		p.X, p.Y, p.Facing = wx-ox, wy-oy, facingDeg(tx-fx, ty-fy, p.Facing)
	})
}
