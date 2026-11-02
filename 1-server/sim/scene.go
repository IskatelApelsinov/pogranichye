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
	SceneRadius = 30.0 // полуразмер деревенской площадки в метрах (простая «коллизия» — границы)
	walkSpeed   = 1.4  // м/с шагом
	runSpeed    = 3.2  // м/с бегом (пока есть силы)
	moveMaxDt   = 0.25 // ограничение шага интегрирования (с) — защита от рывков/читерских dt
	stamDrain   = 0.18 // трата запала в секунду бега
	stamRegen   = 0.08 // восстановление запала в секунду не-бега
)

// ScenePosC — положение тела в сцене: координаты (м), взгляд (град), запал бега (0..1) и номер
// последнего обработанного ввода (для сверки на клиенте).
type ScenePosC struct {
	X      float64 `json:"x"`
	Y      float64 `json:"y"`
	Facing float64 `json:"facing"`
	Stam   float64 `json:"stam"`
	Seq    int64   `json:"seq"`
}

func (c *ScenePosC) Kind() string { return "scenepos" }

func ScenePos(e *core.Entity) *ScenePosC { c, _ := e.Get("scenepos").(*ScenePosC); return c }

// EnsureScenePos вешает положение (в центре площадки, полный запал), если его ещё нет.
func EnsureScenePos(w *core.World, e *core.Entity) *ScenePosC {
	if p := ScenePos(e); p != nil {
		return p
	}
	p := &ScenePosC{X: 0, Y: 0, Facing: 90, Stam: 1}
	w.Attach(e, p)
	return p
}

// ApplyMove — АВТОРИТЕТНОЕ применение ввода движения: нормализует направление, считает скорость
// (бег тратит запал, без запала — только шаг), интегрирует за ограниченный dt, держит в границах
// площадки, ставит взгляд и запоминает seq. Клиент не телепортируется — только задаёт намерение.
func ApplyMove(p *ScenePosC, mx, my float64, run bool, face float64, dt float64, seq int64) {
	if p == nil {
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
		if run && p.Stam > 0 {
			speed = runSpeed
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
		p.X = clampScene(p.X + mx*speed*dt)
		p.Y = clampScene(p.Y + my*speed*dt)
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
	if seq > p.Seq {
		p.Seq = seq
	}
}

func clampScene(v float64) float64 {
	if v > SceneRadius {
		return SceneRadius
	}
	if v < -SceneRadius {
		return -SceneRadius
	}
	return v
}

// NeighborPos — устойчивое «декоративное» положение соседа по его id (сервер пока не симулирует
// каждого в сцене — это та же LoD-идея: детально ведём лишь игрока). Клиент так стабильно их
// расставит; каноническое движение НПС — отдельный следующий шаг.
func NeighborPos(id core.ID) (x, y, facing float64) {
	h := uint64(id)*2654435761 + 1013904223
	ang := float64(h%360) * math.Pi / 180
	rad := (float64((h/360)%1000)/1000)*0.8*SceneRadius + 2
	return rad * math.Cos(ang), rad * math.Sin(ang), float64((h / 7) % 360)
}
