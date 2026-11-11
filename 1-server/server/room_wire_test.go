package server

import (
	"encoding/json"
	"fmt"
	"path/filepath"
	"sync"
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
	"pogranichye/sim"
)

func roomTestCharacter(s *Server, settlement core.ID) *core.Entity {
	var found *core.Entity
	s.world.Each("ident", func(e *core.Entity) {
		if found != nil {
			return
		}
		v, p := core.Vitals(e), core.Place(e)
		if v != nil && v.Age >= 18 && p != nil && p.Settlement == settlement {
			found = e
		}
	})
	return found
}

func TestRoomIntentChestPersistenceAndFrame(t *testing.T) {
	s := NewStarter("room-wire", "", []byte("x"), "")
	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid == 0 {
			if v := core.Vitals(e); v != nil && v.Age >= 18 {
				if p := core.Place(e); p != nil && p.Settlement != 0 {
					cid = e.ID
				}
			}
		}
	})
	if cid == 0 {
		t.Fatal("нет персонажа для аренды комнаты")
	}
	e := s.world.Get(cid)
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	if core.Purse(e) == nil {
		s.world.Attach(e, &core.PurseC{Coins: 20, Debts: map[string]float64{}})
	} else {
		core.Purse(e).Coins = 20
	}
	if sim.Inv(e) == nil {
		s.world.Attach(e, &sim.InventoryC{Items: map[string]int{}, Worn: map[string]string{}})
	}
	if econ.Stash(e) == nil {
		s.world.Attach(e, &econ.StashC{Goods: map[string]float64{}})
	}
	core.Settle(s.world.Get(core.Place(e).Settlement)).Tavern = true
	if ok, why := sim.GoTo(s.world, e, "tavern"); !ok {
		t.Fatalf("подойти к корчме: %s", why)
	}

	send := func(action, item, good, side, reqid string, qty int) map[string]any {
		t.Helper()
		msg := fmt.Sprintf(`{"v":1,"op":"room","action":%q,"item":%q,"good":%q,"side":%q,"qty":%d,"reqid":%q}`, action, item, good, side, qty, reqid)
		s.applyIntent(cid, msg)
		var frame map[string]any
		if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
			t.Fatal(err)
		}
		me := frame["me"].(map[string]any)
		result, _ := me["result"].(map[string]any)
		if result == nil || result["reqid"] != reqid {
			t.Fatalf("нет коррелируемого me.result для %s: %#v", reqid, result)
		}
		return me
	}

	me := send("rent", "", "", "", "rent", 0)
	room, ok := me["room"].(map[string]any)
	if !ok || room["status"] != "rented" || room["number"] == nil {
		t.Fatalf("аренда не появилась в me.room: %#v", me["room"])
	}
	place := framePlace(t, s, cid)
	if len(place["rooms"].([]any)) != 2 {
		t.Fatalf("корчма должна показывать две комнаты: %#v", place["rooms"])
	}
	me = send("enter", "", "", "", "enter", 0)
	room = me["room"].(map[string]any)
	if room["inside"] != true {
		t.Fatalf("вход должен быть подтверждён сервером: %#v", room)
	}
	me = send("open", "", "", "", "open", 0)
	room = me["room"].(map[string]any)
	if room["chest_open"] != true {
		t.Fatalf("сундук не открылся: %#v", room)
	}

	sim.Inv(e).Items["axe"] = 1
	econ.Stash(e).Goods[econ.GoodWood] = 2
	me = send("move", "axe", "", "deposit", "deposit-axe", 1)
	me = send("move", "", econ.GoodWood, "deposit", "deposit-wood", 1)
	room = me["room"].(map[string]any)
	container := room["container"].(map[string]any)
	items := container["items"].(map[string]any)
	goods := container["goods"].(map[string]any)
	if items["axe"] != float64(1) || goods[econ.GoodWood] != float64(1) {
		t.Fatalf("в сундуке должны лежать и предмет, и товар: %#v", container)
	}
	if sim.Inv(e).Items["axe"] != 0 || econ.Stash(e).Goods[econ.GoodWood] != 1 {
		t.Fatal("депозит должен атомарно убрать вещи из исходных хранилищ")
	}
	me = send("move", "axe", "", "deposit", "deposit-axe", 1)
	container = me["room"].(map[string]any)["container"].(map[string]any)
	if container["items"].(map[string]any)["axe"] != float64(1) {
		t.Fatal("повтор reqid не должен дублировать предмет")
	}

	path := filepath.Join(t.TempDir(), "world.sav")
	if err := s.SaveTo(path); err != nil {
		t.Fatalf("save: %v", err)
	}
	restored := NewStarter("other-room-seed", "", []byte("x"), "")
	loaded, err := restored.LoadFrom(path)
	if err != nil || !loaded {
		t.Fatalf("load: loaded=%t err=%v", loaded, err)
	}
	s = restored
	e = s.world.Get(cid)
	if sim.RoomLease(e) == nil || sim.RoomVisit(e) == nil || !sim.RoomVisit(e).ChestOpen {
		t.Fatal("lease and open chest session must survive save/load")
	}
	if sim.Container(s.world.Get(sim.Room(s.world.Get(sim.RoomLease(e).RoomID)).ContainerID)).Items["axe"] != 1 {
		t.Fatal("chest contents must survive save/load")
	}

	me = send("move", "axe", "", "withdraw", "withdraw-axe", 1)
	if sim.Inv(e).Items["axe"] != 1 {
		t.Fatal("withdraw must return the item to carried inventory")
	}
	me = send("leave", "", "", "", "leave", 0)
	if me["room"].(map[string]any)["inside"] != false || sim.InRoom(e) {
		t.Fatal("leave must close server-side room presence")
	}
}

func framePlace(t *testing.T, s *Server, cid core.ID) map[string]any {
	t.Helper()
	var frame map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &frame); err != nil {
		t.Fatal(err)
	}
	return frame["place"].(map[string]any)
}

func TestRoomRentalContentionOnlyTwoRooms(t *testing.T) {
	s := NewStarter("room-contention", "", []byte("x"), "")
	var settlement core.ID
	s.world.Each("settlement", func(se *core.Entity) {
		if settlement == 0 && core.Settle(se) != nil && core.Settle(se).Tavern {
			settlement = se.ID
		}
	})
	if settlement == 0 {
		t.Fatal("starter world needs a tavern")
	}
	players := make([]*core.Entity, 0, 3)
	for len(players) < 3 {
		e := roomTestCharacter(s, settlement)
		if e == nil {
			break
		}
		players = append(players, e)
		s.world.Detach(e, "ident") // exclude this entity from the next selection pass
	}
	if len(players) != 3 {
		t.Fatal("need three adult characters in one tavern settlement")
	}
	for _, e := range players {
		s.world.Attach(e, &core.IdentC{Name: fmt.Sprintf("Игрок-%d", e.ID)})
		sim.EnsureScenePos(s.world, e)
		sim.EnsurePlayer(s.world, e)
		if core.Purse(e) == nil {
			s.world.Attach(e, &core.PurseC{Coins: 20, Debts: map[string]float64{}})
		} else {
			core.Purse(e).Coins = 20
		}
		if ok, why := sim.GoTo(s.world, e, "tavern"); !ok {
			t.Fatalf("go tavern: %s", why)
		}
	}
	var wg sync.WaitGroup
	for i, e := range players {
		wg.Add(1)
		go func(i int, e *core.Entity) {
			defer wg.Done()
			msg := fmt.Sprintf(`{"v":1,"op":"room","action":"rent","reqid":"rent-%d"}`, i)
			s.applyIntent(e.ID, msg)
		}(i, e)
	}
	wg.Wait()
	leased := 0
	for _, e := range players {
		if sim.RoomLease(e) != nil {
			leased++
		}
	}
	if leased != 2 {
		t.Fatalf("two rooms must be allocated atomically to exactly two of three players; got %d", leased)
	}
}
