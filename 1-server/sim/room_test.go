package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/econ"
)

func roomTestPlayer(t *testing.T, coins float64) (*core.World, *core.Entity, *core.Entity) {
	t.Helper()
	w := core.NewWorld("room-test")
	e, se := drinker(w, coins, 0)
	EnsureTavernRooms(w, se)
	return w, e, se
}

func TestRoomRentChestTransfersAndIdempotency(t *testing.T) {
	w, e, se := roomTestPlayer(t, 20)
	rooms := RoomsView(w, e)
	if len(rooms) != 2 || rooms[0]["state"] != "available" {
		t.Fatalf("starter tavern should expose two empty rooms: %#v", rooms)
	}
	if ok, why := RentRoom(w, e, 1, "rent-1"); !ok {
		t.Fatalf("rent: %s", why)
	}
	if core.Purse(e).Coins != 19 || RoomLease(e) == nil {
		t.Fatal("first day rent must be charged once and lease stored")
	}
	if ok, why := RentRoom(w, e, 1, "rent-1"); !ok || core.Purse(e).Coins != 19 {
		t.Fatalf("duplicate rent must not charge again: ok=%t why=%q coins=%.2f", ok, why, core.Purse(e).Coins)
	}
	if ok, why := EnterRoom(w, e, 1, "enter"); !ok {
		t.Fatalf("enter: %s", why)
	}
	if !InRoom(e) {
		t.Fatal("room entry must be server-confirmed")
	}
	if ok, why := OpenRoomChest(w, e, 1, "open"); !ok {
		t.Fatalf("open chest: %s", why)
	}
	inv := ensureInv(w, e)
	inv.Items["axe"] = 1
	st := ensureStashOf(w, e)
	st.Goods[econ.GoodWood] = 3
	if ok, why := MoveRoomContainer(w, e, 1, "item", "deposit", "axe", 1, "deposit-axe"); !ok {
		t.Fatalf("deposit item: %s", why)
	}
	if ok, why := MoveRoomContainer(w, e, 1, "good", "deposit", econ.GoodWood, 2, "deposit-wood"); !ok {
		t.Fatalf("deposit good: %s", why)
	}
	re, room := roomForPlayer(w, e)
	chest := Container(w.Get(room.ContainerID))
	if chest == nil || chest.RoomID != re.ID || chest.Owner != e.ID || chest.Items["axe"] != 1 || chest.Goods[econ.GoodWood] != 2 {
		t.Fatalf("separate chest entity did not keep its contents: %#v", chest)
	}
	if inv.Items["axe"] != 0 || st.Goods[econ.GoodWood] != 1 {
		t.Fatal("deposit must atomically remove items/goods from source")
	}
	if ok, _ := MoveRoomContainer(w, e, 1, "item", "deposit", "sword", 1, "missing"); ok || chest.Items["sword"] != 0 {
		t.Fatal("failed transfer must not mutate the chest")
	}
	if ok, why := MoveRoomContainer(w, e, 1, "good", "withdraw", econ.GoodWood, 1, "withdraw-wood"); !ok {
		t.Fatalf("withdraw good: %s", why)
	}
	if chest.Goods[econ.GoodWood] != 1 || st.Goods[econ.GoodWood] != 2 {
		t.Fatal("withdraw must move the same quantity back to personal stash")
	}
	view := RoomView(w, e, 1).(map[string]any)
	if view["inside"] != true || view["chest_open"] != true || view["container"] == nil {
		t.Fatalf("room view should expose contents only while open: %#v", view)
	}
	if ok, why := LeaveRoom(w, e, "leave"); !ok || InRoom(e) {
		t.Fatalf("leave: ok=%t why=%q", ok, why)
	}
}

func TestRoomRentRequiresTavernAndFunds(t *testing.T) {
	w, e, _ := roomTestPlayer(t, 0.5)
	if ok, _ := RentRoom(w, e, 1, "poor"); ok || RoomLease(e) != nil || core.Purse(e).Coins != 0.5 {
		t.Fatal("insufficient funds must not rent or charge")
	}
	core.Purse(e).Coins = 5
	pos := ScenePos(e)
	pos.X, pos.Y = 1000, 1000
	if ok, _ := RentRoom(w, e, 1, "far"); ok || RoomLease(e) != nil || core.Purse(e).Coins != 5 {
		t.Fatal("renting away from the tavern door must not mutate state")
	}
}

func TestRoomDailyRentGraceRenewalAndRecovery(t *testing.T) {
	w, e, _ := roomTestPlayer(t, 1)
	if ok, why := RentRoom(w, e, 1, "rent"); !ok {
		t.Fatal(why)
	}
	re, room := roomForPlayer(w, e)
	chest := Container(w.Get(room.ContainerID))
	chest.Items["axe"] = 1
	inv := ensureInv(w, e)
	if ok, why := EnterRoom(w, e, 1, "enter"); !ok {
		t.Fatal(why)
	}
	RoomRentSystem{}.Hour(w, core.Clock{TotalHours: 24}, nil) // day 2, rent due but no coins
	if room.State != "grace" || room.GraceUntilDay != 4 {
		t.Fatalf("rent shortfall should start three-day grace: %#v", room)
	}
	RoomRentSystem{}.Hour(w, core.Clock{TotalHours: 24}, nil) // same day must not double-charge
	if room.State != "grace" || core.Purse(e).Coins != 0 {
		t.Fatal("rent system must be idempotent within a world day")
	}
	RoomRentSystem{}.Hour(w, core.Clock{TotalHours: 96}, nil) // day 5, grace expired
	if room.State != "expired" || InRoom(e) {
		t.Fatalf("expired lease must close room access: state=%s inside=%t", room.State, InRoom(e))
	}
	if ok, why := RecoverRoomChest(w, e, "recover"); !ok {
		t.Fatalf("recover chest from innkeeper: %s", why)
	}
	if !RoomVisit(e).Recovery || !RoomVisit(e).ChestOpen {
		t.Fatal("expired contents should be accessible through recovery mode")
	}
	if ok, why := MoveRoomContainer(w, e, 5, "item", "withdraw", "axe", 1, "recover-axe"); !ok {
		t.Fatalf("recover item: %s", why)
	}
	if inv.Items["axe"] != 1 || RoomLease(e) != nil || room.State != "available" {
		t.Fatalf("empty recovery chest should release the room without losing items: inv=%#v room=%#v", inv.Items, room)
	}
}

func TestRoomRenewPaysArrearsExactlyOnce(t *testing.T) {
	w, e, _ := roomTestPlayer(t, 10)
	if ok, why := RentRoom(w, e, 1, "rent"); !ok {
		t.Fatal(why)
	}
	re, room := roomForPlayer(w, e)
	_ = re
	RoomRentSystem{}.Hour(w, core.Clock{TotalHours: 24}, nil) // day 2 auto-charge 1
	if room.PaidThroughDay != 2 || core.Purse(e).Coins != 8 {
		t.Fatalf("daily rent should charge once: paid=%d coins=%.2f", room.PaidThroughDay, core.Purse(e).Coins)
	}
	core.Purse(e).Coins = 0
	RoomRentSystem{}.Hour(w, core.Clock{TotalHours: 48}, nil) // day 3 -> grace
	if room.State != "grace" {
		t.Fatalf("expected grace after failed rent: %#v", room)
	}
	core.Purse(e).Coins = 5
	if ok, why := RenewRoom(w, e, 3, "renew"); !ok {
		t.Fatalf("renew arrears: %s", why)
	}
	if room.State != "rented" || room.PaidThroughDay != 3 || core.Purse(e).Coins != 4 {
		t.Fatalf("renewal should settle only unpaid day(s): room=%#v coins=%.2f", room, core.Purse(e).Coins)
	}
	if ok, _ := RenewRoom(w, e, 3, "renew"); !ok || core.Purse(e).Coins != 4 {
		t.Fatal("duplicate renewal must not charge twice")
	}
}

func TestRoomChestCapacityRejectsAtomically(t *testing.T) {
	w, e, _ := roomTestPlayer(t, 5)
	if ok, why := RentRoom(w, e, 1, "rent"); !ok { t.Fatal(why) }
	if ok, why := EnterRoom(w, e, 1, "enter"); !ok { t.Fatal(why) }
	if ok, why := OpenRoomChest(w, e, 1, "open"); !ok { t.Fatal(why) }
	_, room := roomForPlayer(w, e)
	chest := Container(w.Get(room.ContainerID))
	chest.Capacity = 1
	ensureInv(w, e).Items["axe"] = 1
	if ok, _ := MoveRoomContainer(w, e, 1, "item", "deposit", "axe", 1, "too-heavy"); ok {
		t.Fatal("oversized deposit should fail")
	}
	if Inv(e).Items["axe"] != 1 || len(chest.Items) != 0 {
		t.Fatal("capacity failure must not partially mutate source or chest")
	}
}
