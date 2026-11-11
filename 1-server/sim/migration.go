package sim

import (
	"math"

	"pogranichye/chronicle"
	"pogranichye/core"
	"pogranichye/dice"
)

// MigrationC — persistent abstract transit for an NPC between settlements.
// It preserves the resident entity ID and road itinerary; it is not the player's JourneyC
// and does not create a second physical position or alter the client wire contract.
type MigrationC struct {
	From          core.ID   `json:"from"`
	To            core.ID   `json:"to"`
	Route         []core.ID `json:"route"`
	ProgressHours float64   `json:"progress_hours"`
	TotalHours    float64   `json:"total_hours"`
	Reason        string    `json:"reason"`
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
func startMigration(w *core.World, e *core.Entity, to core.ID, reason string) bool {
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
	w.Attach(e, &MigrationC{
		From: from, To: to, Route: append([]core.ID(nil), route...),
		TotalHours: math.Max(12, distance), Reason: reason,
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
		if w.Get(m.From) == nil || w.Get(m.To) == nil || m.TotalHours <= 0 {
			// Corrupt/missing destination: return the person to the source rather than orphaning them.
			if pl := core.Place(e); pl != nil {
				pl.Settlement = m.From
			}
			w.Detach(e, "migration")
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
		chronicle.Record(w, clock.Day(), "прибытие", "переселенец прибыл в "+core.Settle(w.Get(to)).Name)
		w.Detach(e, "migration")
	}
}

// StartMigration exposes the same guarded path to future group systems without exposing wire actions.
func StartMigration(w *core.World, e *core.Entity, to core.ID, reason string) bool {
	return startMigration(w, e, to, reason)
}

