package server

import (
	"fmt"
	"path/filepath"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// TestRoomRentAndChestPersistAcrossWorldSaveLoadAndReconnect — вертикальный срез op:"room" по
// протоколу WS (не внутренний sim-вызов): аренда, вход, перенос предмета в сундук, затем
// save/load на новый процесс и повторная проверка через me.room (reconnect-эквивалент).
func TestRoomRentAndChestPersistAcrossWorldSaveLoadAndReconnect(t *testing.T) {
	s := NewStarter("room-persist", "", []byte("x"), "")
	var cid core.ID
	var roomID core.ID
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
		t.Fatal("нет тела для теста")
	}
	e := s.world.Get(cid)
	sim.EnsureScenePos(s.world, e)
	sim.EnsurePlayer(s.world, e)
	core.Purse(e).Coins = 30
	sim.GoTo(s.world, e, "tavern")
	s.world.Each("room", func(re *core.Entity) {
		if roomID == 0 {
			if r := sim.Room(re); r != nil && r.Settlement == core.Place(e).Settlement {
				roomID = re.ID
			}
		}
	})
	if roomID == 0 {
		t.Fatal("в селе должна быть хотя бы одна комната над корчмой")
	}

	rent := fmt.Sprintf(`{"v":1,"op":"room","action":"rent","target":"%d","reqid":"wr1"}`, roomID)
	s.applyIntent(cid, rent)
	room := sim.Room(s.world.Get(roomID))
	if room == nil || room.Tenant != cid {
		t.Fatalf("аренда через op:room не прошла: %+v", room)
	}

	enter := fmt.Sprintf(`{"v":1,"op":"room","action":"enter","target":"%d","reqid":"we1"}`, roomID)
	s.applyIntent(cid, enter)
	if sim.ScenePos(e).Indoor == "" {
		t.Fatal("вход через op:room не выставил Indoor")
	}

	s.world.Attach(e, &sim.InventoryC{Items: map[string]int{"torch": 1}, Worn: map[string]string{}})
	stow := fmt.Sprintf(`{"v":1,"op":"room","action":"stow","target":"%d","item":"torch","qty":1,"reqid":"ws1"}`, roomID)
	s.applyIntent(cid, stow)
	chest := sim.Container(s.world.Get(room.ChestID))
	if chest.Items["torch"] != 1 {
		t.Fatalf("предмет должен лежать в сундуке после op:room stow: %+v", chest.Items)
	}

	path := filepath.Join(t.TempDir(), "world.sav")
	if err := s.SaveTo(path); err != nil {
		t.Fatalf("save: %v", err)
	}
	restored := NewStarter("other-seed", "", []byte("x"), "")
	loaded, err := restored.LoadFrom(path)
	if err != nil || !loaded {
		t.Fatalf("load: loaded=%t err=%v", loaded, err)
	}

	re := restored.world.Get(cid)
	rroom := sim.Room(restored.world.Get(roomID))
	if rroom == nil || rroom.Tenant != cid {
		t.Fatalf("аренда должна выжить save/load: %+v", rroom)
	}
	rchest := sim.Container(restored.world.Get(rroom.ChestID))
	if rchest == nil || rchest.Items["torch"] != 1 {
		t.Fatalf("содержимое сундука должно выжить save/load: %+v", rchest)
	}
	if sim.ScenePos(re).Indoor == "" {
		t.Fatal("Indoor-флаг должен выжить save/load (игрок перезашёл тем же телом)")
	}

	// me.room после "reconnect" (новый процесс) должен отражать восстановленную аренду.
	view := sim.RoomView(restored.world, re, restored.runner.Clock.Day())
	if view == nil || view["id"] != fmt.Sprintf("%d", uint64(roomID)) {
		t.Fatalf("me.room должен показать восстановленную комнату: %#v", view)
	}
}
