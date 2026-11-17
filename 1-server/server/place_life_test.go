package server

import (
    "testing"
    "pogranichye/core"
    "pogranichye/econ"
    "pogranichye/sim"
)

func TestPlaceLifeIsSettlementScopedAndUsesOnlyConfirmedNPCActivities(t *testing.T) {
    s := New("place-life-test", 3, "", []byte("x"), "")
    se := s.world.Spawn(&core.SettlementC{Name:"Тест", Store:map[string]float64{econ.GoodWood:7,econ.GoodOre:8,econ.GoodMetal:9,econ.GoodHerb:10}})
    other := s.world.Spawn(&core.SettlementC{Name:"Другое"})
    addNPC := func(settlement core.ID, code string) *core.Entity { return s.world.Spawn(&core.IdentC{Name:"Житель"}, &core.MindC{ActCode:code}, &core.PlaceC{Settlement:settlement}) }
    addNPC(se.ID,"farm_sow")
    addNPC(se.ID,"smith_forge")
    addNPC(se.ID,"farm_nosow")
    addNPC(se.ID,"farm_mend")
    addNPC(se.ID,"unknown_future_code")
    addNPC(se.ID,"rest_idle")
    addNPC(se.ID,"drink_tavern")
    addNPC(se.ID,"keep_tavern")
    addNPC(other.ID,"chop_wood")
    player := s.world.Spawn(&core.IdentC{Name:"Игрок"}, &core.PlaceC{Settlement:se.ID})
    sim.EnsurePlayer(s.world,player)
    s.ctl[player.ID]=1
    s.world.Spawn(&econ.ResourceNodeC{Node:econ.NodeForest,Good:econ.GoodWood,Stock:900,Settlement:se.ID})
    life := s.placeLife(se)
    if life["population"] != 8 { t.Fatalf("population: got %v, want 8",life["population"]) }
    if life["working"] != 4 { t.Fatalf("working: got %v, want 4",life["working"]) }
    acts := life["activities"].(map[string]int)
    if acts["field"]!=1 || acts["maintenance"]!=1 || acts["forge"]!=1 || acts["tavern"]!=2 || acts["rest"]!=1 { t.Fatalf("unexpected activities: %+v",acts) }
    stock := life["stock"].(map[string]any)
    if stock["wood"]!=float64(7) || stock["ore"]!=float64(8) || stock["metal"]!=float64(9) || stock["herb"]!=float64(10) { t.Fatalf("stock must reflect communal store only: %+v",stock) }
    if _, ok := stock["food"]; ok { t.Fatal("food duplicates place.food and must not be in place.life.stock") }
    if _, ok := stock["beer"]; ok { t.Fatal("beer duplicates place.beer and must not be in place.life.stock") }
}

func TestPlaceLifeReflectsNextSnapshotAndIgnoresMissingMind(t *testing.T) {
    s := New("place-life-update-test", 3, "", []byte("x"), "")
    se := s.world.Spawn(&core.SettlementC{Name:"Тест",Store:map[string]float64{}})
    npc := s.world.Spawn(&core.IdentC{Name:"Житель"}, &core.MindC{ActCode:"chop_wood"}, &core.PlaceC{Settlement:se.ID})
    s.world.Spawn(&core.IdentC{Name:"Без разума"}, &core.PlaceC{Settlement:se.ID})
    first := s.placeLife(se)
    if first["population"]!=2 || first["working"]!=1 { t.Fatalf("initial summary: %+v",first) }
    core.Mind(npc).ActCode="rest_idle"
    second := s.placeLife(se)
    if second["population"]!=2 || second["working"]!=0 || second["activities"].(map[string]int)["rest"]!=1 { t.Fatalf("updated summary: %+v",second) }
}
