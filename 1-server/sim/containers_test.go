package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

func TestContainerMovesAreAtomicAndCapacityBounded(t *testing.T) {
	from := &ContainerC{Capacity: 20, Goods: map[string]float64{"wood": 8.5}, Items: map[string]int{"bandage": 3}}
	to := &ContainerC{Capacity: 5, Goods: map[string]float64{}, Items: map[string]int{}}

	if err := MoveContainerGoods(from, to, "wood", 2.5); err != nil {
		t.Fatalf("move fractional goods: %v", err)
	}
	if from.Goods["wood"] != 6 || to.Goods["wood"] != 2.5 || from.Revision != 1 || to.Revision != 1 {
		t.Fatalf("goods move must update both sides and revisions: from=%+v to=%+v", from, to)
	}

	if err := MoveContainerItems(from, to, "bandage", 2); err != nil {
		t.Fatalf("move item stack: %v", err)
	}
	if from.Items["bandage"] != 1 || to.Items["bandage"] != 2 || from.Revision != 2 || to.Revision != 2 {
		t.Fatalf("item move must update both sides and revisions: from=%+v to=%+v", from, to)
	}

	beforeFrom, beforeTo := from.Load(), to.Load()
	beforeFromRev, beforeToRev := from.Revision, to.Revision
	if err := MoveContainerItems(from, to, "bandage", 99); err == nil {
		t.Fatal("missing source quantity must be rejected")
	}
	if err := MoveContainerGoods(from, to, "wood", 1); err == nil {
		t.Fatal("over-capacity destination must be rejected")
	}
	if err := MoveContainerGoods(from, to, "wood", math.NaN()); err == nil {
		t.Fatal("NaN quantity must be rejected")
	}
	if err := MoveContainerItems(from, to, "bandage", 0); err == nil {
		t.Fatal("zero quantity must be rejected")
	}
	if from.Load() != beforeFrom || to.Load() != beforeTo ||
		from.Revision != beforeFromRev || to.Revision != beforeToRev {
		t.Fatal("every rejected transfer must leave both containers and revisions unchanged")
	}
}

func TestRoomAndChestAreSeparatePersistentEntities(t *testing.T) {
	w := core.NewWorld("room-container-save")
	roomEntity, chestEntity, err := CreateRoom(w, 42, 7, "tavern-room-1", 4, 40)
	if err != nil {
		t.Fatalf("create room: %v", err)
	}
	room, chest := Room(roomEntity), Container(chestEntity)
	if room == nil || chest == nil || roomEntity.ID == chestEntity.ID {
		t.Fatal("room and chest must be separate entities")
	}
	if room.ChestID != chestEntity.ID || room.Settlement != 42 || room.Building != 7 ||
		room.DoorPOI != "tavern-room-1" || room.LeaseState != RoomLeaseVacant {
		t.Fatalf("room must point at its own chest and preserve location/lease metadata: %+v", room)
	}
	if chest.Kind != ContainerRoomChest || chest.AccessPolicy != "tenant" || chest.Capacity != 40 {
		t.Fatalf("unexpected chest policy or capacity: %+v", chest)
	}

	chest.Goods["wood"] = 2.5
	chest.Items["mug"] = 1
	room.Tenant, room.PaidThrough, room.LeaseState = 15, 12, RoomLeaseActive
	wire, err := w.Dump(nil)
	if err != nil {
		t.Fatalf("dump world: %v", err)
	}
	loaded := core.LoadWorld(wire)
	loadedRoomEntity, loadedChestEntity := loaded.Get(roomEntity.ID), loaded.Get(chestEntity.ID)
	loadedRoom, loadedChest := Room(loadedRoomEntity), Container(loadedChestEntity)
	if loadedRoom == nil || loadedChest == nil {
		t.Fatal("room and chest components must survive full world save/load")
	}
	if loadedRoom.ChestID != loadedChestEntity.ID || loadedRoom.Tenant != 15 ||
		loadedRoom.PaidThrough != 12 || loadedRoom.LeaseState != RoomLeaseActive {
		t.Fatalf("lease and stable chest link must survive save/load: %+v", loadedRoom)
	}
	if loadedChest.Goods["wood"] != 2.5 || loadedChest.Items["mug"] != 1 {
		t.Fatalf("container contents must survive save/load: %+v", loadedChest)
	}
}

func TestCreateRoomRejectsInvalidCapacityAndPrice(t *testing.T) {
	w := core.NewWorld("room-validation")
	if _, _, err := CreateRoom(w, 0, 1, "door", 4, 20); err == nil {
		t.Fatal("room without settlement must be rejected")
	}
	if _, _, err := CreateRoom(w, 1, 1, "door", 0, 20); err == nil {
		t.Fatal("free rent is not valid in this first slice")
	}
	if _, _, err := CreateRoom(w, 1, 1, "door", 4, 0); err == nil {
		t.Fatal("non-positive chest capacity must be rejected")
	}
	if w.Count() != 0 {
		t.Fatalf("invalid room creation must not leave orphan entities: count=%d", w.Count())
	}
}
