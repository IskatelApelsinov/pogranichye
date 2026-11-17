package sim

import "pogranichye/core"

// Знание игрока о мире (v1.5, первый шаг по #26 — решение владельца: начинаем с двух СТРОГО различных
// фактов). На теле игрока; в кадр попадают только поселения, о которых игрок что-то знает.
//
//   - visited — персонаж ФИЗИЧЕСКИ дошёл до поселения (подтверждённый факт присутствия);
//   - known   — у персонажа есть ОСМЫСЛЕННОЕ местное знание (познакомился с кем-то там, был внутри дел).
//
// Это НЕ линейная лестница: можно услышать о далёком городе (будущие rumoured/located — следующая
// фаза), не побывав; здесь же только твёрдые visited/known от личного опыта. Глобальная карта и слухи
// между поселениями требуют overworld-перемещения (отдельная веха).

func init() {
	core.Register("knowledge", func() core.Component { return &KnowledgeC{Places: map[core.ID]PlaceFact{}} })
}

// PlaceFact — что игрок знает о конкретном поселении.
type PlaceFact struct {
	Visited bool `json:"visited"`
	Known   bool `json:"known"`
}

// KnowledgeC — знание игрока о поселениях (по стабильному id поселения).
type KnowledgeC struct {
	Places map[core.ID]PlaceFact `json:"places"`
}

func (c *KnowledgeC) Kind() string { return "knowledge" }

func Knowledge(e *core.Entity) *KnowledgeC { c, _ := e.Get("knowledge").(*KnowledgeC); return c }

func ensureKnowledge(w *core.World, e *core.Entity) *KnowledgeC {
	if k := Knowledge(e); k != nil {
		if k.Places == nil {
			k.Places = map[core.ID]PlaceFact{}
		}
		return k
	}
	k := &KnowledgeC{Places: map[core.ID]PlaceFact{}}
	w.Attach(e, k)
	return k
}

// LearnVisited — отметить, что игрок физически был в поселении (идемпотентно, только повышает факт).
func LearnVisited(w *core.World, e *core.Entity, settlement core.ID) {
	if settlement == 0 {
		return
	}
	k := ensureKnowledge(w, e)
	f := k.Places[settlement]
	if !f.Visited {
		f.Visited = true
		k.Places[settlement] = f
	}
}

// LearnKnown — отметить осмысленное местное знание (напр. познакомился с жителем). Known подразумевает
// и Visited, но хранятся строго различимо: known не выставляется «автоматически» от простого присутствия.
func LearnKnown(w *core.World, e *core.Entity, settlement core.ID) {
	if settlement == 0 {
		return
	}
	k := ensureKnowledge(w, e)
	f := k.Places[settlement]
	if !f.Known || !f.Visited {
		f.Known, f.Visited = true, true
		k.Places[settlement] = f
	}
}
