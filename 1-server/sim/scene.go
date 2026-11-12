package sim

import (
	"math"

	"pogranichye/core"
)

// Локальная СЦЕНА поселения и ходьба (движение v1.1). Для толстого клиента: игрок шлёт input-intent
// (направление/бег/взгляд + seq), сервер АВТОРИТЕТНО считает каноническую позицию тела в сцене и
// отдаёт её в кадре. Клиент предсказывает локально и сглаживается к подтверждённой позиции.
//
// Система координат: метры, начало — центр деревенской площадки, +X на восток, +Y на север. Взгляд
// (facing) — градусы [0,360), 0 = +X. Клиент сам решает, как лечь на свои оси (1 юнит = 1 метр).
// Серверные координаты и клиентская геометрия раздельны — это лишь каноническое положение.

func init() {
	core.Register("scenepos", func() core.Component { return &ScenePosC{} })
}

const (
	walkSpeed = 1.4 // м/с обычная ходьба — не меняем
	jogSpeed  = 3.2 // м/с лёгкий бег (Alt)
	runSpeed  = 6.0 // м/с быстрый бег (Shift)
	moveMaxDt = 0.25 // ограничение шага интегрирования (с) — защита от рывков/читерских dt
	stamDrain = 0.10 // трата запала в секунду бега (около 10 с непрерывного бега)
	stamRegen = 0.10 // восстановление запала в секунду не-бега
)

// ScenePosC — положение тела в сцене: координаты (м), взгляд (град), запал бега (0..1) и номер
// последнего обработанного ввода (для сверки на клиенте).
type ScenePosC struct {
	X      float64 `json:"x"`
	Y      float64 `json:"y"`
	Facing float64 `json:"facing"`
	Stam   float64 `json:"stam"`
	Seq    int64   `json:"seq"`
	// Stuck — накопленное РЕАЛЬНОЕ время (с), которое житель простоял без прогресса у преграды.
	// Серверно-внутреннее (json:"-"): кадр/контракт не трогает, используется для measured
	// stuck-recovery в StepNPCMovement.
	Stuck float64 `json:"-"`
	// Mult — множитель скорости (v1.13): верхом быстрее, с перегруженной сумой медленнее. Ставит сервер.
	Mult float64 `json:"-"`
}

func (c *ScenePosC) Kind() string { return "scenepos" }

func ScenePos(e *core.Entity) *ScenePosC { c, _ := e.Get("scenepos").(*ScenePosC); return c }

// EnsureScenePos вешает положение (у колодца в центре деревни, на проходимом месте, полный запал),
// если его ещё нет. Это точка «вселения» игрока.
func EnsureScenePos(w *core.World, e *core.Entity) *ScenePosC {
	if p := ScenePos(e); p != nil {
		return p
	}
	p := &ScenePosC{X: 0, Y: -8, Facing: 90, Stam: 1} // у колодца, лицом на север (в деревню)
	w.Attach(e, p)
	return p
}

// ApplyMove — АВТОРИТЕТНОЕ применение ввода движения: нормализует направление, считает скорость
// (бег тратит запал, без запала — только шаг), интегрирует за ограниченный dt, держит в границах
// площадки, ставит взгляд и запоминает seq. Клиент не телепортируется — только задаёт намерение.
func ApplyMove(p *ScenePosC, mx, my float64, run bool, face float64, dt float64, seq int64, fastMode ...bool) {
	if p == nil {
		return
	}
	// #16: устаревший/повторный ввод отбрасываем ДО любых изменений состояния. Начальный seq=0,
	// поэтому первый принимаемый ввод обязан иметь seq>0. При равном seq (дубль) — тоже ничего не меняем.
	if seq <= p.Seq {
		return
	}
	if dt < 0 {
		dt = 0
	}
	if dt > moveMaxDt {
		dt = moveMaxDt
	}
	// направление движения — единичный вектор (игнорируем длину, чтобы нельзя было «разогнаться» вводом)
	if l := math.Hypot(mx, my); l > 1e-6 {
		mx, my = mx/l, my/l
		speed := walkSpeed
		if p.Mult > 0 {
			speed *= p.Mult
		}
		if p.Mult > 0 && p.Mult < 1 {
			run = false // с тяжёлой ношей не бегают
		}
		if run && p.Stam > 0 {
			speed = jogSpeed
			if len(fastMode) > 0 && fastMode[0] {
				speed = runSpeed
			}
			if p.Mult > 1 {
				speed *= p.Mult
			}
			p.Stam -= stamDrain * dt
			if p.Stam < 0 {
				p.Stam = 0
			}
		} else {
			p.Stam += stamRegen * dt
			if p.Stam > 1 {
				p.Stam = 1
			}
		}
		// коллизия (v1.3): пробуем шаг, но не сквозь стены/реку/край мира — скользим вдоль преград
		p.X, p.Y = ResolveMove(p.X, p.Y, p.X+mx*speed*dt, p.Y+my*speed*dt)
	} else {
		// стоит — запал восстанавливается
		p.Stam += stamRegen * dt
		if p.Stam > 1 {
			p.Stam = 1
		}
	}
	// взгляд в допустимом виде
	p.Facing = math.Mod(face, 360)
	if p.Facing < 0 {
		p.Facing += 360
	}
	p.Seq = seq
}
