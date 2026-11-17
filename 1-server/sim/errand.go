package sim

import (
	"fmt"

	"pogranichye/core"
	"pogranichye/econ"
)

// Первое поручение от жителя (канон «первые 15 минут»: житель просит помочь с простым делом минуты
// на четыре, благодарит и зовёт в корчму). Поручение выдаётся В РАЗГОВОРЕ со знакомым жителем и только
// из его НАСТОЯЩЕЙ нужды: чего нет в его личных закромах, о том он и просит. Нужды нет — нет и
// поручения. Технически это обычная просьба (QuestC, kind "deliver", via "npc"): те же поля кадра и те же
// намерения, что уже есть в протоколе v1.11; новая только реплика разговора "work".

const errandQty = 2

// errandNeed — о чём этот житель мог бы попросить прямо сейчас ("" — ни о чём): первое, чего в его
// закромах меньше, чем на день-другой. Дрова — первыми (первая цепочка занятий — дровосек).
func errandNeed(tgt *core.Entity) string {
	st := econ.Stash(tgt)
	have := func(g string) float64 {
		if st == nil {
			return 0
		}
		return st.Goods[g]
	}
	for _, g := range []string{econ.GoodWood, econ.GoodFish} {
		if have(g) < errandQty {
			return g
		}
	}
	return ""
}

func errandID(tgt core.ID) string { return fmt.Sprintf("errand:%d", tgt) }

func activeErrand(e *core.Entity, tgt core.ID) *Quest {
	qc, _ := e.Get("quests").(*QuestC)
	if qc == nil {
		return nil
	}
	for i := range qc.Active {
		if qc.Active[i].ID == errandID(tgt) {
			return &qc.Active[i]
		}
	}
	return nil
}

// errandOption — текст реплики про поручение в разговоре с этим жителем ("" — реплики нет).
func errandOption(e, tgt *core.Entity) string {
	if Player(tgt) != nil || !Knows(e, tgt.ID) {
		return "" // незнакомцу поручений не дают; игроки друг другу — тоже
	}
	if q := activeErrand(e, tgt.ID); q != nil {
		if st := econ.Stash(e); st != nil && st.Goods[q.Good] >= float64(q.Qty) {
			return "Вот, принёс, что просил."
		}
		return "Напомни, о чём ты просил?"
	}
	if qc, _ := e.Get("quests").(*QuestC); qc != nil && qc.has(errandID(tgt.ID)) {
		return "" // уже помог этому жителю — второй раз подряд не просит
	}
	if errandNeed(tgt) == "" {
		return ""
	}
	return "Чем тебе помочь?"
}

// Errand — реплика "work": взять поручение у жителя, напомнить о нём или сдать, если принесено.
// Возвращает (ок, реплика жителя | причина отказа).
func Errand(w *core.World, e, tgt *core.Entity) (bool, string) {
	if errandOption(e, tgt) == "" {
		return false, "ему сейчас ничего не нужно"
	}
	qc := questsOf(w, e)
	if q := activeErrand(e, tgt.ID); q != nil {
		if _, ready := questProgress(w, e, *q); !ready {
			return true, "«" + goodAsk(q.Good) + " Как принесёшь — найди меня.»"
		}
		if ok, why := QuestTurnIn(w, e, q.ID); !ok {
			return false, why
		}
		line := "«Вот спасибо, выручил! Держи за труды."
		if s := core.Settle(settlementEnt(w, e)); s != nil && s.Tavern {
			line += " А вечером заходи в корчму — кружка там найдётся."
		}
		return true, line + "»"
	}
	if len(qc.Active) >= 5 {
		return false, "слишком много обещаний — сперва выполни взятые"
	}
	good := errandNeed(tgt)
	pay := 0.0
	if pu := core.Purse(tgt); pu != nil && pu.Coins >= 2 {
		pay = 2
	}
	pl := core.Place(e)
	qc.Active = append(qc.Active, Quest{
		ID: errandID(tgt.ID), Kind: "deliver", Via: "npc", Giver: tgt.ID, GiverID: eidStr(tgt.ID),
		GiverName: nameOf(tgt, e), Settlement: pl.Settlement, Good: good, Qty: errandQty, Coins: pay,
		Text: nameOf(tgt, e) + " просит: " + goodAsk(good),
	})
	say(w, e, "quest", "Ты взялся помочь: "+nameOf(tgt, e)+" — "+goodAsk(good))
	return true, "«" + goodAsk(good) + " Сделаешь — в долгу не останусь.»"
}

func goodAsk(good string) string {
	switch good {
	case econ.GoodWood:
		return "Принеси две охапки дров из леса — очаг стынет."
	case econ.GoodFish:
		return "Налови пару рыбин на реке — к столу подать нечего."
	}
	return "Принеси " + good + "."
}
