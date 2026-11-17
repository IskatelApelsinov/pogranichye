package sim

import (
	"fmt"
	"math"
	"sort"

	"pogranichye/core"
)

// Жильё и вход в помещения (v1.20): комната над корчмой на аренду с личным сундуком, и минимальный
// серверный флаг «внутри» для неё (без отдельной геометрии интерьера — см. comms 0174/0177).
// Фундамент (RoomC/ContainerC/CreateRoom/MoveContainerItems/RoomLeaseStateAt/CanAccessRoomChest) уже
// был в sim/containers.go; здесь — реальные op-действия игрока над ним.

const (
	tavernRoomRent      = 6.0  // монет/сутки
	tavernChestCapacity = 40.0 // условных единиц (как и прочие ContainerC)
	roomGraceDays       = 1    // сутки льготного периода после истечения оплаты
)

// CreateTavernRoom — комната над корчмой данного села. Дверь — существующий POI "tavern"
// (Settlement.Tavern всегда true, см. worldgen.go), отдельного входа в геометрии пока нет.
func CreateTavernRoom(w *core.World, settlement core.ID) (*core.Entity, *core.Entity, error) {
	return CreateRoom(w, settlement, 0, "tavern", tavernRoomRent, tavernChestCapacity)
}

// indoorTag — метка ScenePosC.Indoor для конкретной комнаты (не просто «в корчме» — у каждой
// комнаты своя метка, иначе два арендатора делили бы один и тот же флаг).
func indoorTag(roomID core.ID) string { return fmt.Sprintf("room:%d", roomID) }

// roomAndChest достаёт пару комната+сундук по ID комнаты с базовой проверкой целостности.
func roomAndChest(w *core.World, roomID core.ID) (roomEnt, chestEnt *core.Entity, room *RoomC) {
	roomEnt = w.Get(roomID)
	room = Room(roomEnt)
	if room == nil {
		return nil, nil, nil
	}
	chestEnt = w.Get(room.ChestID)
	return roomEnt, chestEnt, room
}

// otherActiveRoom — другая комната, уже снятая этим игроком (одна активная аренда на игрока).
func otherActiveRoom(w *core.World, actor, except core.ID, day int64) bool {
	found := false
	w.Each("room", func(re *core.Entity) {
		if re.ID == except {
			return
		}
		if r := Room(re); r != nil && r.Tenant == actor {
			state := RoomLeaseStateAt(r, day)
			if state == RoomLeaseActive || state == RoomLeaseGrace {
				found = true
			}
		}
	})
	return found
}

// RentRoom — снять свободную комнату в своей корчме на сутки вперёд, либо продлить уже свою.
// Оплата сразу и атомарно: либо списываем монеты и занимаем/продлеваем, либо не меняем ничего.
// Требует: игрок у входа в корчму, комната в его текущем поселении, не занята чужим, хватает монет.
func RentRoom(w *core.World, e *core.Entity, roomID core.ID, day int64, reqid string) (bool, string) {
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	roomEnt, _, room := roomAndChest(w, roomID)
	if roomEnt == nil {
		return false, "такой комнаты нет"
	}
	se := settlementEnt(w, e)
	if se == nil || room.Settlement != se.ID {
		return false, "эта комната не в твоём селе"
	}
	if !atPoi(w, e, room.DoorPOI) {
		return false, "нужно быть у входа в корчму"
	}
	if room.Tenant != 0 && room.Tenant != e.ID {
		state := RoomLeaseStateAt(room, day)
		if state == RoomLeaseActive || state == RoomLeaseGrace {
			return false, "комната уже занята"
		}
		// Expired tenants keep ownership of their stored items until they renew and retrieve
		// them. Never hand a new tenant another player's chest contents.
		if chest := Container(w.Get(room.ChestID)); chest != nil && chest.Load() > 1e-9 {
			return false, "в сундуке остались вещи прежнего жильца"
		}
	}
	if room.Tenant != e.ID && otherActiveRoom(w, e.ID, roomID, day) {
		return false, "у тебя уже есть другая снятая комната"
	}
	pu := core.Purse(e)
	if pu == nil || pu.Coins+1e-9 < room.RentPrice {
		return false, "не хватает монет"
	}
	settle := core.Settle(se)
	if settle == nil {
		return false, "у села нет казны"
	}
	// Продление: если аренда ещё активна/в льготе — считаем от уже оплаченного дня, а не от
	// сегодняшнего (иначе повторная оплата сегодня же теряла бы оплаченные сутки).
	from := day - 1
	if room.Tenant == e.ID {
		state := RoomLeaseStateAt(room, day)
		if (state == RoomLeaseActive || state == RoomLeaseGrace) && room.PaidThrough > from {
			from = room.PaidThrough
		}
	}
	pu.Coins -= room.RentPrice
	settle.Coin += room.RentPrice // плата идёт в казну села (как и прочие платежи игрока)
	room.Tenant = e.ID
	room.PaidThrough = from + 1
	room.GraceUntil = room.PaidThrough + roomGraceDays
	room.LeaseState = RoomLeaseActive
	if chest := Container(w.Get(room.ChestID)); chest != nil {
		chest.Tenant = e.ID
	}
	pc.markDone(reqid)
	return true, ""
}

// roomInsideOffset — детерминированный сдвиг от двери до условной точки внутри ИМЕННО этой
// комнаты (среди комнат с тем же DoorPOI в том же селе), чтобы два арендатора одной корчмы не
// делили одну и ту же внутреннюю точку. Это не вторая геометрия и не новое поле wire: те же x/y
// сцены того же поселения (frame 0), что и снаружи — просто другая точка внутри стен корчмы.
// Стабильно между сохранениями: порядок — по возрастанию ID комнаты, а не по времени создания.
func roomInsideOffset(w *core.World, roomID core.ID, settlement core.ID, doorPOI string) (dx, dy float64) {
	var siblings []core.ID
	w.Each("room", func(re *core.Entity) {
		if r := Room(re); r != nil && r.Settlement == settlement && r.DoorPOI == doorPOI {
			siblings = append(siblings, re.ID)
		}
	})
	sort.Slice(siblings, func(i, j int) bool { return siblings[i] < siblings[j] })
	idx := 0
	for i, id := range siblings {
		if id == roomID {
			idx = i
		}
	}
	offsets := [][2]float64{{3, 2}, {-3, 2}, {3, 4}, {-3, 4}}
	return offsets[idx%len(offsets)][0], offsets[idx%len(offsets)][1]
}

// roomDoorPos/roomInsidePos — серверные авторитетные anchors для этой комнаты в системе координат
// сцены её поселения: дверь (та же точка, что и снаружи для rent/atPoi) и условная точка внутри.
func roomDoorPos(w *core.World, room *RoomC) (x, y float64) {
	return LayoutOf(w, room.Settlement).POI(room.DoorPOI)
}

func roomInsidePos(w *core.World, roomID core.ID, room *RoomC) (x, y float64) {
	px, py := roomDoorPos(w, room)
	dx, dy := roomInsideOffset(w, roomID, room.Settlement, room.DoorPOI)
	return px + dx, py + dy
}

// roomIDFromIndoor разбирает ScenePosC.Indoor обратно в ID комнаты (формат indoorTag).
func roomIDFromIndoor(indoor string) (core.ID, bool) {
	var raw uint64
	if n, err := fmt.Sscanf(indoor, "room:%d", &raw); n != 1 || err != nil {
		return 0, false
	}
	return core.ID(raw), true
}

// EnterRoom — войти в снятую комнату: должен стоять у двери корчмы СВОЕГО текущего поселения (а
// не чужого с тем же именем POI) и быть её текущим арендатором с активной либо льготной арендой.
// Серверно переставляет ScenePosC на условную точку внутри ИМЕННО этой комнаты (см.
// roomInsidePos) и ставит Indoor-метку; entity ID не меняется, клиент координат не присылает.
func EnterRoom(w *core.World, e *core.Entity, roomID core.ID, day int64, reqid string) (bool, string) {
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	roomEnt, _, room := roomAndChest(w, roomID)
	if roomEnt == nil {
		return false, "такой комнаты нет"
	}
	se := settlementEnt(w, e)
	if se == nil || room.Settlement != se.ID {
		return false, "эта комната не в твоём селе"
	}
	if room.Tenant != e.ID {
		return false, "это не твоя комната"
	}
	state := RoomLeaseStateAt(room, day)
	if state != RoomLeaseActive && state != RoomLeaseGrace {
		return false, "аренда истекла"
	}
	if !atPoi(w, e, room.DoorPOI) {
		return false, "нужно быть у входа в корчму"
	}
	sp := EnsureScenePos(w, e)
	sp.X, sp.Y = roomInsidePos(w, roomID, room)
	sp.Indoor = indoorTag(roomID)
	if nav := navOf(e); nav != nil {
		nav.Route = nil
	}
	pc.markDone(reqid)
	return true, ""
}

// ExitRoom — выйти из текущего интерьера наружу, к двери комнаты, из которой вышел. Разрешено
// всегда, пока игрок внутри — даже с истёкшей арендой (выход никогда не создаёт softlock); повторно
// войти или открыть сундук с истёкшей арендой при этом всё равно нельзя (см. EnterRoom/CanAccessRoomChest).
func ExitRoom(w *core.World, e *core.Entity, reqid string) (bool, string) {
	if reqid == "" {
		return false, "нужен reqid"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	sp := ScenePos(e)
	if sp == nil || sp.Indoor == "" {
		return false, "ты не внутри"
	}
	if roomID, ok := roomIDFromIndoor(sp.Indoor); ok {
		if _, _, room := roomAndChest(w, roomID); room != nil {
			sp.X, sp.Y = roomDoorPos(w, room)
		}
	}
	sp.Indoor = ""
	if nav := navOf(e); nav != nil {
		nav.Route = nil
	}
	pc.markDone(reqid)
	return true, ""
}

// StowToChest/TakeFromChest — переложить стак предметов между сумой игрока (InventoryC.Items) и
// личным сундуком комнаты (ContainerC.Items). Требует физически быть внутри именно этой комнаты
// (не просто в том же селе) и действующую аренду. StashC/HorseC.Bag/PurseC здесь не участвуют —
// см. comms 0174.
func StowToChest(w *core.World, e *core.Entity, roomID core.ID, item string, qty int, day int64, reqid string) (bool, string) {
	return moveChestItem(w, e, roomID, item, qty, day, reqid, true)
}

func TakeFromChest(w *core.World, e *core.Entity, roomID core.ID, item string, qty int, day int64, reqid string) (bool, string) {
	return moveChestItem(w, e, roomID, item, qty, day, reqid, false)
}

func moveChestItem(w *core.World, e *core.Entity, roomID core.ID, item string, qty int, day int64, reqid string, toChest bool) (bool, string) {
	if reqid == "" {
		return false, "нужен reqid"
	}
	if item == "" || qty <= 0 {
		return false, "укажи предмет и положительное количество"
	}
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) {
		return true, ""
	}
	roomEnt, chestEnt, room := roomAndChest(w, roomID)
	if roomEnt == nil || !CanAccessRoomChest(roomEnt, chestEnt, e.ID, day) {
		return false, "нет доступа к сундуку"
	}
	sp := ScenePos(e)
	if sp == nil || sp.Indoor != indoorTag(roomID) {
		return false, "нужно быть внутри этой комнаты"
	}
	inv := ensureInv(w, e)
	chest := Container(chestEnt)
	var err error
	if toChest {
		err = moveItemsBetweenMaps(inv.Items, chest.Items, item, qty, chest.Capacity, chest.Load())
	} else {
		err = moveItemsBetweenMaps(chest.Items, inv.Items, item, qty, math.Inf(1), 0) // сума игрока без предела
	}
	if err != nil {
		return false, err.Error()
	}
	if toChest {
		chest.Revision++
	} else {
		chest.Revision++
	}
	_ = room
	pc.markDone(reqid)
	return true, ""
}

// moveItemsBetweenMaps — атомарный перенос стака между двумя картами item→qty с проверкой
// вместимости получателя (destCap/destLoad в тех же условных единицах, что и ContainerC.Load).
// Все проверки — до первой записи: отказ не меняет ни одну сторону.
func moveItemsBetweenMaps(from, to map[string]int, item string, qty int, destCap, destLoad float64) error {
	if from[item] < qty {
		return fmt.Errorf("недостаточно предметов: %s", item)
	}
	if !math.IsInf(destCap, 1) && destLoad+float64(qty) > destCap+1e-9 {
		return fmt.Errorf("сундук переполнен")
	}
	from[item] -= qty
	if from[item] == 0 {
		delete(from, item)
	}
	to[item] += qty
	return nil
}

// RoomView — состояние снятой игроком комнаты для me.room (null, если он никого не снял).
func RoomView(w *core.World, e *core.Entity, day int64) map[string]any {
	var found *core.Entity
	var room *RoomC
	w.Each("room", func(re *core.Entity) {
		if found != nil {
			return
		}
		if r := Room(re); r != nil && r.Tenant == e.ID {
			found, room = re, r
		}
	})
	if found == nil {
		return nil
	}
	chest := Container(w.Get(room.ChestID))
	sp := ScenePos(e)
	out := map[string]any{
		"id": eidStr(found.ID), "settlement": eidStr(room.Settlement), "door_poi": room.DoorPOI,
		"rent_price": room.RentPrice, "paid_through_day": room.PaidThrough, "grace_until_day": room.GraceUntil,
		"lease_state": RoomLeaseStateAt(room, day), "indoor": sp != nil && sp.Indoor == indoorTag(found.ID),
	}
	if chest != nil {
		out["chest_cap"] = chest.Capacity
		out["chest_load"] = math.Round(chest.Load())
		out["chest_items"] = chest.Items
	}
	return out
}

// PlaceRoomsView — комнаты этого села для place.rooms: сколько всего и сколько вакантно,
// без утечки чужого содержимого сундука (видно только своя через me.room).
func PlaceRoomsView(w *core.World, settlement core.ID, day int64) map[string]any {
	total := 0
	vacant := 0
	rent := 0.0
	rooms := make([]*core.Entity, 0, 4)
	w.Each("room", func(re *core.Entity) {
		r := Room(re)
		if r == nil || r.Settlement != settlement {
			return
		}
		total++
		rooms = append(rooms, re)
	})

	// ECS iteration order is an implementation detail. Keep the public room catalogue stable
	// across save/load and server runs so clients can preserve room labels and UI selection.
	sort.Slice(rooms, func(i, j int) bool { return rooms[i].ID < rooms[j].ID })
	items := make([]map[string]any, 0, len(rooms))
	for _, re := range rooms {
		r := Room(re)
		state := RoomLeaseStateAt(r, day)
		available := state == RoomLeaseVacant
		if state == RoomLeaseExpired {
			chest := Container(w.Get(r.ChestID))
			available = chest == nil || chest.Load() <= 1e-9
		}
		if available {
			vacant++
		}
		rent = r.RentPrice
		items = append(items, map[string]any{
			"id": eidStr(re.ID), "lease_state": state, "rent_price": r.RentPrice, "available": available,
		})
	}
	return map[string]any{"total": total, "vacant": vacant, "rent_price": rent, "items": items}
}
