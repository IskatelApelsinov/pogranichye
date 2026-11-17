package sim

import (
	"strings"

	"pogranichye/core"
	"pogranichye/dice"
)

// Походы за околицу (v1.26). Места без жителей — урочища — были только для игрока: мир сам в них не
// ходил. Теперь каждое село днём отправляет ходоков в ближние урочища: травников на луг, лесорубов в
// бор, рудокопов на осыпь. Они берут из ТОГО ЖЕ запаса, что и игрок (WildUseC), и несут добычу в
// закрома села. Отсюда живые следствия: у людного села ближние места выбраны, за нетронутым надо идти
// дальше; село у богатых урочищ живёт сытнее. Опасное место ходоки могут бросить ни с чем.

const (
	forayHour   = 15     // к этому часу ходоки возвращаются
	forayReach  = 2600.0 // м: дальше за день пешком с ношей не обернуться
	forayLeave  = 1.0    // столько запаса ходоки оставляют месту (и путнику)
	forayTake   = 2.0    // больше за раз одна ватага не уносит
	forayYield  = 2.0    // ватага — несколько человек: единица запаса места даёт столько в закрома
	forayScared = 0.25   // доля опасности места, с которой ватага возвращается пустой
)

// forayMark — след похода в урочище: в какой день и из какого села там были люди.
type forayMark struct {
	Day  int    `json:"day"`
	From string `json:"from"`
}

func forayParties(tier string) int {
	switch tier {
	case "city":
		return 4
	case "town":
		return 3
	case "village":
		return 2
	default:
		return 1
	}
}

func forageGood(kind string) string {
	for _, a := range wildKinds[kind].acts {
		if strings.HasPrefix(a, "forage:") {
			return strings.TrimPrefix(a, "forage:")
		}
	}
	return ""
}

// ForaySystem — раз в день сёла ходят в ближние урочища за травами, дровами, рудой, рыбой и ягодами.
type ForaySystem struct{}

func (ForaySystem) Name() string { return "foray" }

func (ForaySystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if u := peekWildUse(w); u != nil || clock.Hour() == forayHour {
		wildUse(w).Today = int(clock.Day()) // день мира для памяти мест (зачистка фронтира)
	}
	if clock.Hour() != forayHour {
		return
	}
	RunForays(w, int(clock.Day()), hub.Stream("foray"))
}

// RunForays — один день походов всех сёл. Возвращает, сколько ватаг вернулось с добычей.
func RunForays(w *core.World, day int, g *dice.Stream) int {
	t := terrainFor(w)
	done := 0
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil {
			return
		}
		parties := forayParties(s.Tier)
		ae, an := Anchor(w, s)
		for _, p := range WildPlacesNear(w, t, ae, an, forayReach) {
			if parties == 0 {
				break
			}
			good := forageGood(p.Kind)
			if good == "" {
				continue
			}
			left := wildLeft(peekWildUse(w), p.ID, day)
			take := minf(forayTake, left-forayLeave)
			if take < 1 {
				continue // место выбрано — ватага идёт к следующему
			}
			parties--
			if g.Chance(p.Danger * forayScared) {
				continue // зверь или лихие люди спугнули: день потерян, запас цел
			}
			u := wildUse(w)
			u.Stock[p.ID] = wildStock{Left: left - take, Day: day}
			if u.Foray == nil {
				u.Foray = map[string]forayMark{}
			}
			u.Foray[p.ID] = forayMark{Day: day, From: s.Name}
			if s.Store == nil {
				s.Store = map[string]float64{}
			}
			s.Store[good] += take * forayYield
			done++
		}
	})
	return done
}

func minf(a, b float64) float64 {
	if a < b {
		return a
	}
	return b
}

// forayNote — строка для путника: были ли здесь сегодня или вчера люди из села.
func forayNote(u *WildUseC, id string, day int) string {
	if u == nil {
		return ""
	}
	m, ok := u.Foray[id]
	if !ok || m.From == "" {
		return ""
	}
	switch day - m.Day {
	case 0:
		return "сегодня здесь были люди из села " + m.From
	case 1:
		return "вчера здесь были люди из села " + m.From
	}
	return ""
}
