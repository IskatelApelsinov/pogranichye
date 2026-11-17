package sim

import (
	"math"

	"pogranichye/core"
)

// Хмель (v1.12): выпитое пиво даёт настоящее серверное состояние. Проходит со временем. Лёгкий хмель
// веселит, тяжёлый мешает целиться и удерживать узор. Состояние у каждого своё (пили вдвоём — хмель
// у каждого по выпитому).

func init() { core.Register("drunk", func() core.Component { return &DrunkC{} }) }

// DrunkC — хмель 0..100.
type DrunkC struct {
	Level float64 `json:"level"`
}

func (c *DrunkC) Kind() string { return "drunk" }

// Drunk — текущий хмель тела.
func Drunk(e *core.Entity) float64 {
	if c, _ := e.Get("drunk").(*DrunkC); c != nil {
		return c.Level
	}
	return 0
}

// addDrunk — кружка выпита. Выносливый хмелеет медленнее.
func addDrunk(w *core.World, e *core.Entity, amt float64) {
	c, _ := e.Get("drunk").(*DrunkC)
	if c == nil {
		c = &DrunkC{}
		w.Attach(e, c)
	}
	c.Level = math.Min(100, c.Level+amt/statMod(e, "endurance", 0.3))
}

// SoberUp — хмель выветривается в реальном времени (кружка ≈ за пару минут).
func SoberUp(w *core.World, dt float64) {
	w.Each("drunk", func(e *core.Entity) {
		c := e.Get("drunk").(*DrunkC)
		c.Level = math.Max(0, c.Level-dt*0.15)
	})
}

// drunkPenalty — штраф к точности/удержанию узора: до 30 хмеля нет, дальше растёт до 0.35.
func drunkPenalty(e *core.Entity) float64 {
	return math.Max(0, math.Min(0.35, (Drunk(e)-30)/200))
}
