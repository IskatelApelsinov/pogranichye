package sim

import (
	"math"

	"pogranichye/arcane"
	"pogranichye/core"
)

// Учителя плетений (ROADMAP P0 «Учителя плетений и книги»): схему теперь можно ОТКРЫТЬ не только
// пробой (успешным свободным плетением в WeaveCast), но и ученичеством у живого мага. Наставник
// передаёт ИМЕНОВАННУЮ схему из свода, которую ученик способен удержать: без дара — только самые
// простые узоры (giftlessMax), а потолок сложности поднимает сила учителя (старец учит сложнее).
//
// Канон и границы: сервер авторитетен — клиент не задаёт, какую схему выучить; её выбирает сервер по
// реальному составу свода и способностям тела. Действие расходное и идемпотентное (reqid + bounded
// dedup как у прочих act), отказ без затрат. Контракт не меняется по кадру: это новый код действия
// поверх существующего op:"act" (как farmwork/forge/apiary), результат виден в me.result и
// me.weave.known. Книги как источник схем — отдельный следующий срез (нужен предмет-источник).

const studyEnergy = 8.0 // сил на один урок у наставника

// teachCeiling — до какой сложности узора наставник доведёт ученика: ёмкость ума плюс вклад учителя
// (пробуждённый маг +1, старец +2). Так простые схемы даёт любой маг, а сложные — только сильный
// наставник или уже умелый ученик.
func teachCeiling(e *core.Entity, teach float64) float64 {
	return arcane.Capacity(skillOf(e, "mage")) + teach
}

// nextTeachable — простейшая ещё НЕ известная схема из свода, которую это тело способно удержать
// (проходит weaveGate по дару) и которая в пределах потолка наставника. Берём минимальную по
// сложности — ученичество идёт от простого к сложному и детерминировано.
func nextTeachable(w *core.World, e *core.Entity, teach float64) (Schema, bool) {
	g := grimoire(w, e)
	ceil := teachCeiling(e, teach)
	best := Schema{}
	bestC := math.MaxFloat64
	found := false
	for _, l := range schemaLore {
		if _, known := g.knows(l.Nodes); known {
			continue
		}
		sp, ok := ReadWeave(l.Nodes)
		if !ok {
			continue
		}
		if ok2, _ := weaveGate(e, sp.Complexity); !ok2 {
			continue // без дара — только самые простые узоры
		}
		if sp.Complexity > ceil {
			continue // наставник пока не доведёт до такого узора
		}
		if sp.Complexity < bestC {
			bestC, best, found = sp.Complexity, l, true
		}
	}
	return best, found
}

// StudyWeave — поучиться плетению у наставника-мага в поселении: открыть именованную схему без пробы.
// Требует мага рядом (teacherNear > 0), сил и чтобы было ещё чему учить это тело. Идемпотентно по reqid.
func StudyWeave(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" {
		return false, "нужен reqid"
	}
	if pc.alreadyDone(reqid) {
		return true, "" // идемпотентный повтор успешного урока
	}
	if OnRoad(e) {
		return false, "в дороге не до учения"
	}
	teach := teacherNear(w, e)
	if teach <= 0 {
		return false, "здесь некому учить плетениям: нужен маг рядом"
	}
	v := core.Vitals(e)
	if v == nil || v.Energy < studyEnergy {
		return false, "нет сил на урок"
	}
	schema, ok := nextTeachable(w, e, teach)
	if !ok {
		return false, "наставник не научит тебя новому узору — учись пробой или ищи сильнее"
	}
	v.Energy -= studyEnergy
	g := grimoire(w, e)
	if len(g.Known) < 16 {
		g.Known = append(g.Known, Schema{Name: schema.Name, Nodes: append([]string{}, schema.Nodes...)})
	}
	trainSkill(w, e, "mage", 0.3)
	pc.markDone(reqid) // запись только при успехе
	if teach >= 2 {
		say(w, e, "learn", "Старец ведёт твою мысль по узору — и ты запоминаешь плетение: "+schema.Name+".")
	} else {
		say(w, e, "learn", "Маг показывает узор по узлам — ты запоминаешь плетение: "+schema.Name+".")
	}
	return true, ""
}

// canStudyHere — есть ли наставник рядом и остался ли ему узор для этого тела (сигнал клиенту в
// me.weave.can_study: показать кнопку «Учиться у мага»).
func canStudyHere(w *core.World, e *core.Entity) bool {
	if OnRoad(e) {
		return false
	}
	teach := teacherNear(w, e)
	if teach <= 0 {
		return false
	}
	_, ok := nextTeachable(w, e, teach)
	return ok
}
