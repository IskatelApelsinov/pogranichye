package sim

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Родство и пары. Дети родятся не «из воздуха при двух взрослых», а у ПАРЫ — мужчины и женщины.
// Пары складываются логикой принадлежности: одинокие взрослые одного села, встречаясь (таверна,
// соседство), сходятся. Нет взрослых обоих полов — нет пар — нет детей: демография выправляется
// сама, без скрипта. Овдовел (партнёр умер/ушёл) — со временем может сойтись снова.

func init() {
	core.Register("kin", func() core.Component { return &KinC{} })
}

// KinC — семейные узы: с кем в паре. Детей не считаем числом — они полноценные сущности мира.
type KinC struct {
	Partner core.ID `json:"partner"` // супруг(а); 0 — одинок
}

func (c *KinC) Kind() string { return "kin" }

func Kin(e *core.Entity) *KinC { c, _ := e.Get("kin").(*KinC); return c }

// partnerAlive возвращает живого партнёра в ТОМ ЖЕ селе, иначе nil (и чистит устаревшую связь).
func partnerAlive(w *core.World, e *core.Entity) *core.Entity {
	k := Kin(e)
	if k == nil || k.Partner == 0 {
		return nil
	}
	p := w.Get(k.Partner)
	if p == nil || core.Ident(p) == nil { // партнёр покинул мир — овдовел
		k.Partner = 0
		return nil
	}
	// в разных сёлах (кто-то ушёл) — связь рвётся
	if sameSettlement(e, p) {
		return p
	}
	k.Partner = 0
	if pk := Kin(p); pk != nil && pk.Partner == e.ID {
		pk.Partner = 0
	}
	return nil
}

func sameSettlement(a, b *core.Entity) bool {
	pa, pb := core.Place(a), core.Place(b)
	return pa != nil && pb != nil && pa.Settlement != 0 && pa.Settlement == pb.Settlement
}

// pairUp сводит одиноких взрослых обоих полов в сёлах за один проход (логика принадлежности:
// встретились, оба свободны — сошлись). Зовётся раз в сутки из LifeSystem по готовому индексу.
func pairUp(w *core.World, byHome map[core.ID][]*core.Entity, s *dice.Stream) {
	for _, res := range byHome {
		var men, women []*core.Entity
		for _, e := range res {
			id := core.Ident(e)
			v := core.Vitals(e)
			if id == nil || v == nil || v.Age < 16 {
				continue
			}
			if k := Kin(e); k != nil && k.Partner != 0 {
				continue // уже в паре
			}
			if id.Sex == "м" {
				men = append(men, e)
			} else {
				women = append(women, e)
			}
		}
		// сводим столько пар, сколько получается; ухаживание не мгновенно — с вероятностью
		n := len(men)
		if len(women) < n {
			n = len(women)
		}
		for i := 0; i < n; i++ {
			if !s.Chance(0.25) { // не каждый день складывается
				continue
			}
			bond(w, men[i], women[i])
		}
	}
}

func bond(w *core.World, a, b *core.Entity) {
	ka, kb := Kin(a), Kin(b)
	if ka == nil {
		ka = &KinC{}
		w.Attach(a, ka)
	}
	if kb == nil {
		kb = &KinC{}
		w.Attach(b, kb)
	}
	ka.Partner = b.ID
	kb.Partner = a.ID
	// обустроенность растёт — семья осела (тяга к дому удовлетворяется совместно)
	for _, e := range []*core.Entity{a, b} {
		if d := core.Drives(e); d != nil {
			d.Social = clamp(d.Social-20, 0, 100)
		}
	}
}
