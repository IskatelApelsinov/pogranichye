package sim

import (
	"testing"

	"pogranichye/core"
)

// Тесты комнаты над корчмой (v1.20): аренда/вход/выход/сундук. Фундамент (RoomC/ContainerC)
// тестируется отдельно в containers_test.go; здесь — реальные op-действия игрока.

// secondPlayer — второй взрослый житель того же села, отдельный от starterPlayer (для тестов на
// конкурирующих арендаторов/чужого тенанта).
func secondPlayer(t *testing.T, w *core.World, se *core.Entity, exclude core.ID) *core.Entity {
	t.Helper()
	var e *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		if r.ID == exclude {
			continue
		}
		if v := core.Vitals(r); e == nil && v != nil && v.Age >= 18 {
			e = r
		}
	}
	if e == nil {
		t.Fatal("нет второго жителя")
	}
	EnsureScenePos(w, e)
	EnsurePlayer(w, e)
	core.Purse(e).Coins = 30
	return e
}

// tavernRoomsOf — комнаты данного села, в порядке обхода мира (детерминированный ID-порядок).
func tavernRoomsOf(w *core.World, settlement core.ID) []core.ID {
	var ids []core.ID
	w.Each("room", func(re *core.Entity) {
		if r := Room(re); r != nil && r.Settlement == settlement {
			ids = append(ids, re.ID)
		}
	})
	return ids
}

func TestStarterVillageHasTwoTavernRooms(t *testing.T) {
	w, _, se := starterPlayer(t)
	ids := tavernRoomsOf(w, se.ID)
	if len(ids) != 2 {
		t.Fatalf("ожидал 2 комнаты над корчмой, получил %d", len(ids))
	}
	for _, id := range ids {
		room := Room(w.Get(id))
		if room.LeaseState != RoomLeaseVacant || room.Tenant != 0 {
			t.Fatalf("новая комната должна быть вакантна: %+v", room)
		}
		chest := Container(w.Get(room.ChestID))
		if chest == nil || chest.ContainerKind != ContainerRoomChest {
			t.Fatalf("у комнаты должен быть свой сундук")
		}
	}
}

func TestRentRoomRequiresBeingAtTheDoor(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	// игрок не у двери корчмы (стартовая позиция сцены — не "tavern")
	ok, why := RentRoom(w, e, rooms[0], 10, "r1")
	if ok || why == "" {
		t.Fatalf("аренда без присутствия у двери должна отклоняться, получил ok=%v why=%q", ok, why)
	}
}

func TestRentRoomSucceedsAtTheDoor(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	before := core.Purse(e).Coins
	ok, why := RentRoom(w, e, rooms[0], 10, "r1")
	if !ok {
		t.Fatalf("аренда должна пройти: %q", why)
	}
	room := Room(w.Get(rooms[0]))
	if room.Tenant != e.ID || room.LeaseState != RoomLeaseActive {
		t.Fatalf("комната должна стать активной на арендатора: %+v", room)
	}
	if room.PaidThrough != 10 || room.GraceUntil != 11 {
		t.Fatalf("неверные сроки оплаты: paid_through=%d grace=%d", room.PaidThrough, room.GraceUntil)
	}
	if got := before - core.Purse(e).Coins; got != room.RentPrice {
		t.Fatalf("списано %v монет, ожидал %v", got, room.RentPrice)
	}
	chest := Container(w.Get(room.ChestID))
	if chest.Tenant != e.ID {
		t.Fatalf("сундук должен быть привязан к новому арендатору")
	}
}

func TestRentRoomIdempotentByReqID(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1")
	before := core.Purse(e).Coins
	ok, _ := RentRoom(w, e, rooms[0], 10, "r1") // повтор того же reqid
	if !ok {
		t.Fatal("повтор того же reqid должен быть идемпотентно успешен")
	}
	if core.Purse(e).Coins != before {
		t.Fatalf("повтор не должен списывать монеты повторно: было %v, стало %v", before, core.Purse(e).Coins)
	}
}

func TestRentRoomRejectsInsufficientCoins(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	core.Purse(e).Coins = 1
	ok, why := RentRoom(w, e, rooms[0], 10, "r1")
	if ok || why == "" {
		t.Fatalf("без денег аренда должна отклоняться: ok=%v why=%q", ok, why)
	}
	room := Room(w.Get(rooms[0]))
	if room.Tenant != 0 {
		t.Fatal("отказ не должен занимать комнату")
	}
}

func TestRentRoomRejectsWhenAlreadyTenantOfAnother(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	ok, why := RentRoom(w, e, rooms[0], 10, "r1")
	if !ok {
		t.Fatalf("первая аренда должна пройти: %q", why)
	}
	ok, why = RentRoom(w, e, rooms[1], 10, "r2")
	if ok || why == "" {
		t.Fatal("нельзя снять вторую комнату, пока активна первая")
	}
}

func TestExpiredRoomWithStoredItemsCannotBeReassigned(t *testing.T) {
	w, previousTenant, se := starterPlayer(t)
	newTenant := secondPlayer(t, w, se, previousTenant.ID)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, previousTenant, "tavern")
	GoTo(w, newTenant, "tavern")

	if ok, why := RentRoom(w, previousTenant, rooms[0], 10, "old-rent"); !ok {
		t.Fatalf("первая аренда должна пройти: %q", why)
	}
	if ok, why := EnterRoom(w, previousTenant, rooms[0], 10, "old-enter"); !ok {
		t.Fatalf("старый арендатор должен войти: %q", why)
	}
	ensureInv(w, previousTenant).Items["torch"] = 1
	if ok, why := StowToChest(w, previousTenant, rooms[0], "torch", 1, 10, "old-stow"); !ok {
		t.Fatalf("старый арендатор должен положить вещь в сундук: %q", why)
	}

	if state := RoomLeaseStateAt(Room(w.Get(rooms[0])), 12); state != RoomLeaseExpired {
		t.Fatalf("к 12-му дню аренда должна истечь, состояние %q", state)
	}
	if ok, why := RentRoom(w, newTenant, rooms[0], 12, "new-rent-blocked"); ok || why == "" {
		t.Fatal("новый арендатор не должен получать сундук с чужими вещами")
	}
	if summary := PlaceRoomsView(w, se.ID, 12); summary["vacant"] != 1 {
		t.Fatalf("комната с чужими вещами не должна считаться вакантной: %#v", summary)
	}

	// Старый арендатор может продлить именно эту комнату, забрать вещи и освободить сундук.
	if ok, why := RentRoom(w, previousTenant, rooms[0], 12, "old-renew"); !ok {
		t.Fatalf("старый арендатор должен иметь возможность продлить комнату: %q", why)
	}
	if ok, why := EnterRoom(w, previousTenant, rooms[0], 12, "old-reenter"); !ok {
		t.Fatalf("старый арендатор должен снова войти: %q", why)
	}
	if ok, why := TakeFromChest(w, previousTenant, rooms[0], "torch", 1, 12, "old-take"); !ok {
		t.Fatalf("старый арендатор должен забрать свою вещь: %q", why)
	}
	if ok, why := ExitRoom(w, previousTenant, "old-exit"); !ok {
		t.Fatalf("старый арендатор должен выйти: %q", why)
	}

	// После следующего истечения пустая комната безопасно передаётся новому арендатору.
	if ok, why := RentRoom(w, newTenant, rooms[0], 14, "new-rent"); !ok {
		t.Fatalf("пустую комнату после истечения можно пересдать: %q", why)
	}
	if Room(w.Get(rooms[0])).Tenant != newTenant.ID {
		t.Fatal("после пересдачи владельцем комнаты должен стать новый арендатор")
	}
}

func TestExpiredRoomLeaseDoesNotBlockRentingAnotherRoom(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	if ok, why := RentRoom(w, e, rooms[0], 10, "r-expired"); !ok {
		t.Fatalf("первая аренда должна пройти: %q", why)
    }
    if state := RoomLeaseStateAt(Room(w.Get(rooms[0])), 20); state != RoomLeaseExpired {
		t.Fatalf("первая аренда должна истечь к 20-му дню, состояние %q", state)
    }
    if ok, why := RentRoom(w, e, rooms[1], 20, "r-new"); !ok {
		t.Fatalf("истёкшая аренда не должна блокировать новую комнату: %q", why)
    }
    if Room(w.Get(rooms[1])).Tenant != e.ID {
		t.Fatal("новая комната должна быть закреплена за игроком")
    }
}

func TestRentRoomRejectsCompetingTenant(t *testing.T) {
	w, e, se := starterPlayer(t)
	other := secondPlayer(t, w, se, e.ID)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	GoTo(w, other, "tavern")
	ok, why := RentRoom(w, e, rooms[0], 10, "r1")
	if !ok {
		t.Fatalf("первый арендатор должен занять комнату: %q", why)
	}
	ok, why = RentRoom(w, other, rooms[0], 10, "r2")
	if ok || why == "" {
		t.Fatal("чужая занятая комната не должна сдаваться второму")
	}
}

func TestRentRoomExtendsFromPaidThroughNotToday(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1") // paid_through=10
	ok, why := RentRoom(w, e, rooms[0], 10, "r2")
	if !ok {
		t.Fatalf("продление своей комнаты должно пройти: %q", why)
	}
	room := Room(w.Get(rooms[0]))
	if room.PaidThrough != 11 { // продлили от уже оплаченного дня, а не от "сегодня" повторно
		t.Fatalf("продление должно идти от paid_through: получил %d", room.PaidThrough)
	}
}

func TestEnterExitRoomRequiresTenancyAndDoor(t *testing.T) {
	w, e, se := starterPlayer(t)
	other := secondPlayer(t, w, se, e.ID)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1")

	if ok, why := EnterRoom(w, other, rooms[0], 10, "e-other"); ok || why == "" {
		t.Fatal("чужак не может войти в снятую комнату")
	}
	ScenePos(e).X, ScenePos(e).Y = 9999, 9999 // далеко от двери
	if ok, why := EnterRoom(w, e, rooms[0], 10, "e1-far"); ok || why == "" {
		t.Fatal("вход должен требовать присутствия у двери")
	}
	GoTo(w, e, "tavern")
	ok, why := EnterRoom(w, e, rooms[0], 10, "e1")
	if !ok {
		t.Fatalf("арендатор у двери должен войти: %q", why)
	}
	if ScenePos(e).Indoor != indoorTag(rooms[0]) {
		t.Fatalf("Indoor должен быть выставлен: %q", ScenePos(e).Indoor)
	}
	ok, why = ExitRoom(w, e, "x1")
	if !ok {
		t.Fatalf("выход должен пройти: %q", why)
	}
	if ScenePos(e).Indoor != "" {
		t.Fatal("после выхода Indoor должен очиститься")
	}
	if ok, _ := ExitRoom(w, e, "x2"); ok {
		t.Fatal("повторный выход без входа должен отклоняться")
	}
}

func TestEnterRoomRejectsExpiredLease(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1") // paid_through=11, grace=12
	if ok, why := EnterRoom(w, e, rooms[0], 20, "e1"); ok || why == "" {
		t.Fatal("вход после истечения льготного периода должен отклоняться")
	}
}

func TestChestRequiresBeingInsideThisRoom(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1")
	ensureInv(w, e).Items["torch"] = 1
	if ok, why := StowToChest(w, e, rooms[0], "torch", 1, 10, "s1"); ok || why == "" {
		t.Fatal("нельзя класть в сундук, не входя в комнату")
	}
	EnterRoom(w, e, rooms[0], 10, "e1")
	ok, why := StowToChest(w, e, rooms[0], "torch", 1, 10, "s1")
	if !ok {
		t.Fatalf("внутри комнаты перенос должен пройти: %q", why)
	}
	if ensureInv(w, e).Items["torch"] != 0 {
		t.Fatal("предмет должен уйти из сумы")
	}
	chest := Container(w.Get(Room(w.Get(rooms[0])).ChestID))
	if chest.Items["torch"] != 1 {
		t.Fatal("предмет должен оказаться в сундуке")
	}
}

func TestChestRejectsWrongTenant(t *testing.T) {
	w, e, se := starterPlayer(t)
	other := secondPlayer(t, w, se, e.ID)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1")
	EnterRoom(w, e, rooms[0], 10, "e1")
	ensureInv(w, other).Items["torch"] = 1
	ScenePos(other).Indoor = indoorTag(rooms[0]) // подделываем присутствие — доступ всё равно должен быть закрыт
	if ok, why := StowToChest(w, other, rooms[0], "torch", 1, 10, "s1"); ok || why == "" {
		t.Fatal("чужой арендатор не должен иметь доступ к сундуку")
	}
}

func TestChestRejectsMissingGoodsAndOverflow(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1")
	EnterRoom(w, e, rooms[0], 10, "e1")

	if ok, why := StowToChest(w, e, rooms[0], "torch", 1, 10, "s-missing"); ok || why == "" {
		t.Fatal("нельзя положить то, чего нет в суме")
	}
	chest := Container(w.Get(Room(w.Get(rooms[0])).ChestID))
	chest.Items["torch"] = int(chest.Capacity) + 10 // переполняем сундук до предела + лишку
	ensureInv(w, e).Items["lantern"] = 1
	if ok, why := StowToChest(w, e, rooms[0], "lantern", 1, 10, "s-overflow"); ok || why == "" {
		t.Fatal("переполненный сундук должен отклонять перенос")
	}
	if ensureInv(w, e).Items["lantern"] != 1 {
		t.Fatal("отказ не должен менять суму игрока (без частичного переноса)")
	}
}

func TestChestTakeBackFromChest(t *testing.T) {
	w, e, se := starterPlayer(t)
	rooms := tavernRoomsOf(w, se.ID)
	GoTo(w, e, "tavern")
	RentRoom(w, e, rooms[0], 10, "r1")
	EnterRoom(w, e, rooms[0], 10, "e1")
	ensureInv(w, e).Items["torch"] = 2
	StowToChest(w, e, rooms[0], "torch", 2, 10, "s1")
	ok, why := TakeFromChest(w, e, rooms[0], "torch", 1, 10, "t1")
	if !ok {
		t.Fatalf("забрать обратно должно пройти: %q", why)
	}
	if ensureInv(w, e).Items["torch"] != 1 {
		t.Fatal("один факел должен вернуться в суму")
	}
	chest := Container(w.Get(Room(w.Get(rooms[0])).ChestID))
	if chest.Items["torch"] != 1 {
		t.Fatal("один факел должен остаться в сундуке")
	}
}

func TestOldSaveWithoutRoomOrContainerDoesNotBreakViews(t *testing.T) {
	w, e, _ := starterPlayer(t)
	// сущность без room/container вовсе (симулирует старое сохранение до v1.20)
	if v := RoomView(w, e, 10); v != nil {
		t.Fatalf("у игрока без снятой комнаты me.room должен быть null, получил %v", v)
	}
	if ok, why := EnterRoom(w, e, core.ID(999999), 10, "x"); ok || why == "" {
		t.Fatal("вход в несуществующую комнату должен аккуратно отклоняться, а не падать")
	}
}
