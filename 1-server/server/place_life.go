package server

import (
    "pogranichye/core"
    "pogranichye/econ"
    "pogranichye/sim"
)

// placeLife aggregates the current settlement only. It reads NPC identity/activity and
// the communal store, never private containers or natural resource-node stock.
func (s *Server) placeLife(se *core.Entity) map[string]any {
    if s == nil || s.world == nil || se == nil { return nil }
    st := core.Settle(se)
    if st == nil { return nil }
    activities := map[string]int{
        "field":0, "maintenance":0, "wood":0, "forge":0, "mine":0, "fish":0, "hunt":0, "herb":0,
        "cook":0, "herd":0, "magic":0, "tavern":0, "rest":0, "social":0, "eating":0, "other":0,
    }
    population, working := 0, 0
    s.world.Each("place", func(e *core.Entity) {
        place := core.Place(e)
        if place == nil || place.Settlement != se.ID || core.Ident(e) == nil || sim.Player(e) != nil { return }
        population++
        mind := core.Mind(e)
        if mind == nil { return }
        category, productive := "", false
        switch mind.ActCode {
        case "farm_sow", "farm_reap", "farm_tend": category, productive = "field", true
        case "farm_mend": category, productive = "maintenance", true
        case "chop_wood": category, productive = "wood", true
        case "smith_forge": category, productive = "forge", true
        case "mine_ore": category, productive = "mine", true
        case "fish_river", "fisher_mend": category, productive = "fish", true
        case "hunt_wild": category, productive = "hunt", true
        case "forage_herbs", "brew_remedy": category, productive = "herb", true
        case "cook_hearth": category, productive = "cook", true
        case "herd_barn", "herd_graze": category, productive = "herd", true
        case "weave", "study_mana": category, productive = "magic", true
        case "keep_tavern": category, productive = "tavern", true
        case "drink_tavern": category = "tavern"
        case "sleep", "rest_idle": category = "rest"
        case "visit_neighbors": category = "social"
        case "meal": category = "eating"
        case "work": category, productive = "other", true
        default: return // unknown and blocked/no-op codes are not counted
        }
        activities[category]++
        if productive { working++ }
    })
    store := st.Store
    if store == nil { store = map[string]float64{} }
    return map[string]any{
        "population": population, "working": working, "activities": activities,
        "stock": map[string]any{
            "wood": round0(store[econ.GoodWood]),
            "ore": round0(store[econ.GoodOre]), "metal": round0(store[econ.GoodMetal]),
            "herb": round0(store[econ.GoodHerb]),
        },
    }
}
