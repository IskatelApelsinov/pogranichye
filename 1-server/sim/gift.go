package sim

import (
	"math"

	"pogranichye/arcane"
	"pogranichye/core"
	"pogranichye/dice"
)

// Дар и ученичество (v1.12, решение владельца 2026-11-11): «магия — дар по броску, но без него можно
// прокачаться через магические зоны и старцев и использовать ману в лёгких заклинаниях».
//   • Дар — бросок при первом вселении игрока в тело (редкий). Тело, уже несущее искру (латент из
//     мира), остаётся одарённым.
//   • Без дара ум удерживает только ЛЁГКИЕ узоры (сложность ≤ giftlessMax): свет, искра, толчок, щит.
//   • Учиться можно всем: созерцание (act:"meditate") в месте силы или рядом с пробуждённым магом;
//     старец учит сильнее. Упорный бездарный дорастает до пробуждения (awakenSkill) — дар открывается
//     трудом. Так канон «редкие маги» и «игра всегда даёт качать упорным» сходятся.

const (
	giftChance  = 0.08 // доля одарённых при вселении
	giftlessMax = 3.0  // сложность узора, доступная без дара
	awakenSkill = 30.0 // навык магии, при котором дар открывается трудом
	manaPlace   = 1.15 // мана поселения, с которой место считается местом силы
)

// GiftC — отметка, что бросок дара для этого тела уже был (чтобы не перебрасывать при каждом входе).
type GiftC struct {
	Rolled bool `json:"rolled"`
}

func (c *GiftC) Kind() string { return "gift" }

func init() { core.Register("gift", func() core.Component { return &GiftC{} }) }

// HasGift — несёт ли тело дар (пробуждённый или спящий).
func HasGift(e *core.Entity) bool { return arcane.Mage(e) != nil }

// RollGift — бросок дара при первом вселении игрока в тело.
func RollGift(w *core.World, e *core.Entity, s *dice.Stream) {
	if g, _ := e.Get("gift").(*GiftC); g != nil && g.Rolled {
		return
	}
	w.Attach(e, &GiftC{Rolled: true})
	if !HasGift(e) && s.Chance(giftChance) {
		w.Attach(e, &arcane.MageC{Awakened: true})
		say(w, e, "learn", "Ты чувствуешь мир иначе, чем прежде: в тебе есть дар.")
	}
}

// teacherNear — сила наставника в поселении: 0 — никого, 1 — пробуждённый маг, 2 — старец (от 60 лет).
func teacherNear(w *core.World, e *core.Entity) float64 {
	pl := core.Place(e)
	best := 0.0
	if pl == nil {
		return 0
	}
	for _, r := range residentsOf(w, pl.Settlement) {
		if r.ID == e.ID {
			continue
		}
		if m := arcane.Mage(r); m != nil && m.Awakened {
			k := 1.0
			if v := core.Vitals(r); v != nil && v.Age >= 60 {
				k = 2
			}
			best = math.Max(best, k)
		}
	}
	return best
}

// Meditate — созерцание: учиться чувствовать ману. Требует места силы ИЛИ мага рядом; тратит силы.
func Meditate(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	if OnRoad(e) {
		return false, "в дороге не сосредоточиться"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < 10 {
		return false, "нет сил сосредоточиться"
	}
	place := 0.0
	if s := core.Settle(settlementEnt(w, e)); s != nil && s.Mana >= manaPlace {
		place = s.Mana - manaPlace + 0.6
	}
	teach := teacherNear(w, e)
	if place <= 0 && teach <= 0 {
		return false, "здесь нечему учиться: нужно место силы или маг рядом"
	}
	v.Energy -= 10
	gain := 0.5 + place + 0.6*teach
	if !HasGift(e) {
		gain *= 0.5 // без дара вдвое труднее
	}
	trainSkill(w, e, "mage", gain)
	pc.markDone(reqid)
	if !HasGift(e) && skillOf(e, "mage") >= awakenSkill {
		w.Attach(e, &arcane.MageC{Awakened: true})
		say(w, e, "learn", "Упорство взяло своё: ты научился удерживать узор. Дар открылся трудом.")
	} else if teach >= 2 {
		say(w, e, "weave", "Старец молча поправляет твои руки. Что-то становится яснее.")
	} else if teach > 0 {
		say(w, e, "weave", "Ты следишь, как плетёт маг, и пробуешь повторить в уме.")
	} else {
		say(w, e, "weave", "Здесь густо от маны: ты учишься её чувствовать.")
	}
	return true, ""
}

// weaveGate — можно ли этому телу удержать узор такой сложности.
func weaveGate(e *core.Entity, complexity float64) (bool, string) {
	if HasGift(e) || complexity <= giftlessMax {
		return true, ""
	}
	return false, "без дара ум удерживает только самые простые узоры"
}

// GiftView — добавка к me.weave: есть ли дар, можно ли здесь учиться и до какой сложности доступно.
func GiftView(w *core.World, e *core.Entity) (gift bool, canLearn bool, limit float64) {
	gift = HasGift(e)
	s := core.Settle(settlementEnt(w, e))
	canLearn = !OnRoad(e) && ((s != nil && s.Mana >= manaPlace) || teacherNear(w, e) > 0)
	limit = giftlessMax
	if gift {
		limit = weaveMaxLen + 4
	}
	return
}
