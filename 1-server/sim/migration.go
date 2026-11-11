package sim

import (
	"math"

	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// MigrationC — persistent abstract transit for an NPC between settlements.
// It preserves the resident entity ID and road itinerary; it is not the player's JourneyC
// and does not create a second physical position or alter the client wire contract.
type MigrationC struct {
	From          core.ID   `json:"from"`
	To            core.ID   `json:"to"`
	Route         []core.ID `json:"route"`
	ProgressHours float64   `json:"progress_hours"`
	StartedAtHour int64     `json:"started_at_hour"`
	TotalHours    float64   `json:"total_hours"`
	Reason        string    `json:"reason"`
	FoodSupply    int       `json:"food_supply"`
}

func init() {
	core.Register("migration", func() core.Component { return &MigrationC{} })
}

func (c *MigrationC) Kind() string { return "migration" }

func Migration(e *core.Entity) *MigrationC {
	if e == nil {
		return nil
	}
	c, _ := e.Get("migration").(*MigrationC)
	return c
}

// startMigration moves an NPC out of settlement population while it travels.
// One map verst per world hour is the initial simulation rate; route distance remains authoritative
// and can be balanced later without changing the stored route or NPC identity.
// startMigrationAt records the world hour in which transit was created. MigrationSystem
// must not count the remainder of that same Runner.Step as a full travel hour.
func startMigrationAt(w *core.World, e *core.Entity, to core.ID, reason string, startedAtHour int64) bool {
	if w == nil || e == nil || Player(e) != nil || to == 0 || Migration(e) != nil {
		return false
	}
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 || pl.Settlement == to {
		return false
	}
	from := pl.Settlement
	src, dst := w.Get(from), w.Get(to)
	if src == nil || dst == nil || core.Settle(src) == nil || core.Settle(dst) == nil {
		return false
	}
	outer := isOuter(w, core.Settle(src))
	if outer != isOuter(w, core.Settle(dst)) {
		return false
	}
	route, distance := RoadRoute(RoadGraph(w, outer), from, to)
	if len(route) < 2 || route[0] != from || route[len(route)-1] != to || distance <= 0 {
		return false
	}
	totalHours := math.Max(12, distance)
	foodNeeded := migrationMeals(e, totalHours)
	store := core.Settle(src).Store
	if foodNeeded > 0 && (store == nil || store[econ.GoodBread] < float64(foodNeeded)) {
		return false // no supplies: a real migration cannot begin on an empty road
	}
	if foodNeeded > 0 {
		store[econ.GoodBread] -= float64(foodNeeded)
	}
	w.Attach(e, &MigrationC{
		From: from, To: to, Route: append([]core.ID(nil), route...),
		StartedAtHour: startedAtHour, TotalHours: totalHours, Reason: reason, FoodSupply: foodNeeded,
	})
	pl.Settlement = 0 // transit is not counted as a resident of either endpoint
	if h := core.Home(e); h != nil {
		h.Status = "none"
		h.Settlement = to
	}
	if ScenePos(e) != nil {
		w.Detach(e, "scenepos")
	}
	if navOf(e) != nil {
		w.Detach(e, "nav")
	}
	return true
}


func migrationMeals(e *core.Entity, totalHours float64) int {
	hunger := 20.0
	if v := core.Vitals(e); v != nil {
		hunger = v.Hunger
	}
	// Match NeedsSystem's hourly order exactly: hunger rises by 0.6, then a meal is
	// consumed at 25 and reduces hunger by 45. Round travel duration up to whole ticks.
	meals := 0
	for hour := 0; hour < int(math.Ceil(totalHours)); hour++ {
		hunger = math.Min(100, hunger+0.6)
		if hunger >= 25 {
			meals++
			hunger = math.Max(0, hunger-45)
		}
	}
	return meals
}


// migrationFoodNeededForGroup plans the whole founding band against the actual road route.
// A straight-line offset is not a safe bound: RoadGraph may route through intermediate settlements.
func migrationFoodNeededForGroup(w *core.World, source, destination *core.Entity, travellers []*core.Entity) (int, bool) {
	if w == nil || source == nil || destination == nil || len(travellers) == 0 {
		return 0, false
	}
	src, dst := core.Settle(source), core.Settle(destination)
	if src == nil || dst == nil {
		return 0, false
	}
	outer := isOuter(w, src)
	if outer != isOuter(w, dst) {
		return 0, false
	}
	route, distance := RoadRoute(RoadGraph(w, outer), source.ID, destination.ID)
	if len(route) < 2 || route[0] != source.ID || route[len(route)-1] != destination.ID || distance <= 0 {
		return 0, false
	}

	totalHours := math.Max(12, distance)
	needed := 0
	for _, traveller := range travellers {
		if traveller == nil || Player(traveller) != nil || Migration(traveller) != nil {
			return 0, false
		}
		place := core.Place(traveller)
		if place == nil || place.Settlement != source.ID {
			return 0, false
		}
		needed += migrationMeals(traveller, totalHours)
	}
	if needed > 0 && src.Store == nil {
		return 0, false
	}
	return needed, true
}

func returnMigrationFood(w *core.World, settlement core.ID, supply int) {
	if w == nil || settlement == 0 || supply <= 0 {
		return
	}
	entity := w.Get(settlement)
	if entity == nil {
		return
	}
	if s := core.Settle(entity); s != nil {
		if s.Store == nil {
			s.Store = map[string]float64{}
		}
		s.Store[econ.GoodBread] += float64(supply)
	}
}

// MigrationSystem advances NPC travel independently of connected players.
// Needs, aging and mortality continue in the existing world systems while PlaceC.Settlement == 0.
type MigrationSystem struct{}

func (MigrationSystem) Name() string { return "migration" }

func (MigrationSystem) Hour(w *core.World, clock core.Clock, hub *dice.Hub) {
	if w == nil {
		return
	}
	var arrived []*core.Entity
	w.Each("migration", func(e *core.Entity) {
		m := Migration(e)
		if m == nil {
			return
		}
		var src, dst *core.SettlementC
		if entity := w.Get(m.From); entity != nil {
			src = core.Settle(entity)
		}
		if entity := w.Get(m.To); entity != nil {
			dst = core.Settle(entity)
		}
		if src == nil || dst == nil || m.TotalHours <= 0 ||
			math.IsNaN(m.TotalHours) || math.IsInf(m.TotalHours, 0) ||
			m.ProgressHours < 0 || math.IsNaN(m.ProgressHours) || math.IsInf(m.ProgressHours, 0) || len(m.Route) < 2 ||
			m.Route[0] != m.From || m.Route[len(m.Route)-1] != m.To {
			// Corrupt/missing endpoint: prefer the original settlement, otherwise the destination.
			returnTo := core.ID(0)
			if pl := core.Place(e); pl != nil {
				switch {
				case src != nil:
					pl.Settlement = m.From
					returnTo = m.From
				case dst != nil:
					pl.Settlement = m.To
					returnTo = m.To
				default:
					pl.Settlement = 0
				}
			}
			if home := core.Home(e); home != nil {
				home.Status = "none"
				home.Settlement = returnTo
			}
			if mind := core.Mind(e); mind != nil {
				mind.Goal = ""
				mind.GoalUntil = 0
				mind.ActCode = ""
				mind.LastAct = "путь прерван, вернулся в поселение"
			}
			returnMigrationFood(w, returnTo, m.FoodSupply)
			w.Detach(e, "migration")
			return
		}
		// A migration created by a system earlier in this same Runner.Step has not
		// travelled for a full world hour yet.
		if clock.TotalHours <= m.StartedAtHour {
			return
		}
		m.ProgressHours++
		if m.ProgressHours >= m.TotalHours {
			arrived = append(arrived, e)
		}
	})
	for _, e := range arrived {
		m := Migration(e)
		if m == nil {
			continue
		}
		pl := core.Place(e)
		if pl == nil {
			pl = &core.PlaceC{}
			w.Attach(e, pl)
		}
		pl.Settlement = m.To
		to := m.To
		if h := core.Home(e); h != nil {
			h.Settlement = to
		}
		if mind := core.Mind(e); mind != nil {
			mind.Goal = ""
			mind.GoalUntil = 0
			mind.ActCode = ""
			mind.LastAct = "добрался до нового поселения"
		}
		returnMigrationFood(w, to, m.FoodSupply)
		chronicle.Record(w, clock.Day(), "прибытие", "переселенец прибыл в "+core.Settle(w.Get(to)).Name)
		w.Detach(e, "migration")
	}
}

// StartMigration exposes the same guarded path to future group systems without exposing wire actions.
func StartMigration(w *core.World, e *core.Entity, to core.ID, reason string, startedAtHour int64) bool {
	return startMigrationAt(w, e, to, reason, startedAtHour)
}

