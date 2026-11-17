package sim

import (
	"sort"

	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
)

// Свободные места в ремёслах (v1.31). Ушёл корчмарь (умер, переселился, подался в разбой) — и за
// стойкой пусто навсегда: никто не продаёт пива, у села нет хозяина. Теперь раз в сутки село
// смотрит, какие нужные места пустуют, и на них встаёт свой: сперва житель без ремесла, иначе — из
// самого многолюдного ремесла (лишний пахарь становится кузнецом). Новое место кормит: корчмарь
// получает выручку стойки, кузнец — за работу.

// essentialRoles — без кого селу не жить, и с какого числа взрослых это место заводится.
var essentialRoles = []struct {
	role   string
	adults int
}{
	{"tavernkeeper", 1}, // корчма есть в каждом селе — и кто-то должен стоять за стойкой
	{"smith", 6},
	{"herbalist", 8},
	{"cook", 10},
}

// VacancySystem — раз в сутки занять пустующие нужные места жителями села.
type VacancySystem struct{}

func (VacancySystem) Name() string { return "vacancy" }

func (VacancySystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if clock.Hour() != 7 {
		return
	}
	FillVacancies(w, int64(clock.Day()))
}

func canTakeJob(e *core.Entity) bool {
	v := core.Vitals(e)
	return v != nil && v.Age >= 15 && Player(e) == nil && Migration(e) == nil && !IsBanditPerson(e)
}

// FillVacancies — один проход (для системы и тестов). Возвращает, сколько мест занято.
func FillVacancies(w *core.World, day int64) int {
	filled := 0
	idx := residentIndex(w)
	sids := make([]core.ID, 0, len(idx))
	for sid := range idx {
		sids = append(sids, sid)
	}
	sort.Slice(sids, func(i, j int) bool { return sids[i] < sids[j] })
	for _, sid := range sids {
		se := w.Get(sid)
		s := core.Settle(se)
		if s == nil {
			continue
		}
		res := idx[sid]
		count := map[string]int{}
		adults := 0
		for _, r := range res {
			if canTakeJob(r) {
				adults++
				count[core.Ident(r).Role]++
			}
		}
		if n := demoteExtraMages(res, count); n > 0 {
			filled += n
			chronicle.Record(w, day, "ремесло", "в селе "+s.Name+" лишние маги вернулись к ремеслу — дар при них, но кормит дело")
		}
		// хозяин корчмы: тот, кто записан, должен быть жив, здесь и при деле
		if k := w.Get(s.Keeper); k == nil || core.Place(k) == nil || core.Place(k).Settlement != sid ||
			core.Ident(k) == nil || core.Ident(k).Role != "tavernkeeper" || IsBanditPerson(k) {
			s.Keeper = 0
			for _, r := range res {
				if canTakeJob(r) && core.Ident(r).Role == "tavernkeeper" {
					s.Keeper = r.ID // за стойкой уже стоит другой корчмарь — он и хозяин
					break
				}
			}
		}
		for _, er := range essentialRoles {
			if adults < er.adults || count[er.role] > 0 {
				continue
			}
			who := pickWorker(res, count)
			if who == nil {
				continue
			}
			id := core.Ident(who)
			from := id.Role
			count[from]--
			count[er.role]++
			setRole(who, er.role)
			if m := core.Mind(who); m != nil {
				m.GoalUntil = 0 // со следующего часа — новое дело
			}
			if er.role == "tavernkeeper" {
				s.Keeper = who.ID
			}
			filled++
			chronicle.Record(w, day, "ремесло", fullName(who)+" занял пустовавшее место: "+roleRu(er.role)+" в селе "+s.Name)
		}
	}
	return filled
}

// mageSeats — сколько жителей села могут жить одним ремеслом мага: один на село и ещё по одному на
// каждые 15 душ. Пробуждённых может быть больше — они плетут при нужде, но кормятся своим ремеслом.
func mageSeats(pop int) int { return 1 + pop/15 }

func mageRolesIn(w *core.World, settlement core.ID) int {
	n := 0
	for _, r := range residentsOf(w, settlement) {
		if id := core.Ident(r); id != nil && id.Role == "mage" {
			n++
		}
	}
	return n
}

// demoteExtraMages — если магов-ремесленников в селе больше мест (старые сохранения, переселения в
// школу), лишние возвращаются к делу: на пустующее нужное место, иначе — в пахари. Дар у них
// остаётся, плести при нужде они по-прежнему могут. Возвращает, скольких вернули к ремеслу.
func demoteExtraMages(res []*core.Entity, count map[string]int) int {
	var mages []*core.Entity
	for _, r := range res {
		if canTakeJob(r) && core.Ident(r).Role == "mage" {
			mages = append(mages, r)
		}
	}
	extra := len(mages) - mageSeats(len(res))
	if extra <= 0 {
		return 0
	}
	// место мага держат самые умелые; к ремеслу возвращаются те, у кого навык дара меньше
	sort.SliceStable(mages, func(i, j int) bool {
		return skillLevel(mages[i], "mage") < skillLevel(mages[j], "mage")
	})
	n := 0
	for _, r := range mages[:extra] {
		role := "farmer"
		for _, er := range essentialRoles {
			if er.role != "tavernkeeper" && count[er.role] == 0 {
				role = er.role
				break
			}
		}
		count["mage"]--
		count[role]++
		setRole(r, role)
		if m := core.Mind(r); m != nil {
			m.GoalUntil = 0
		}
		n++
	}
	return n
}

func skillLevel(e *core.Entity, name string) float64 {
	if sk := core.Skills(e); sk != nil {
		return sk.Level(name)
	}
	return 0
}

// pickWorker — кто встанет на пустое место: житель без ремесла; иначе — из самого многолюдного
// ремесла, если в нём хотя бы трое (последнего пахаря от плуга не отрываем).
func pickWorker(res []*core.Entity, count map[string]int) *core.Entity {
	for _, r := range res {
		if canTakeJob(r) && core.Ident(r).Role == "idler" {
			return r
		}
	}
	best, bn := "", 2
	for role, n := range count {
		if role == "tavernkeeper" || role == "mage" || role == "idler" || role == "guard" {
			continue
		}
		if n > bn || (n == bn && best != "" && role < best) {
			best, bn = role, n
		}
	}
	if best == "" {
		return nil
	}
	for _, r := range res {
		if canTakeJob(r) && core.Ident(r).Role == best {
			return r
		}
	}
	return nil
}

func roleRu(role string) string {
	switch role {
	case "tavernkeeper":
		return "корчмарь"
	case "smith":
		return "кузнец"
	case "herbalist":
		return "травник"
	case "cook":
		return "кухарь"
	}
	return role
}
