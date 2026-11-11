package sim

import (
	"fmt"
	"math"
	"sort"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

const (
	TavernRoomCount   = 2
	RoomDailyRent     = 1.0
	RoomGraceDays     = int64(3)
	RoomChestCapacity = 80.0
)

func init() {
	core.Register("room", func() core.Component { return &RoomC{} })
	core.Register("container", func() core.Component { return &ContainerC{} })
	core.Register("room_lease", func() core.Component { return &RoomLeaseC{} })
	core.Register("room_visit", func() core.Component { return &RoomVisitC{} })
}

// RoomC is a persistent rentable room entity. Its chest is a separate ContainerC entity.
type RoomC struct {
	Settlement    core.ID  `json:"settlement"`
	Building      string   `json:"building"`
	Number        int      `json:"number"`
	DoorPOI       string   `json:"door_poi"`
	Tenant        core.ID  `json:"tenant"`
	State         string   `json:"state"` // available | rented | grace | expired
	RentPerDay    float64  `json:"rent_per_day"`
	PaidThroughDay int64   `json:"paid_through_day"`
	GraceUntilDay int64    `json:"grace_until_day,omitempty"`
	ContainerID   core.ID  `json:"container_id"`
}

func (c *RoomC) Kind() string { return "room" }
func Room(e *core.Entity) *RoomC {
	if e == nil { return nil }
	c, _ := e.Get("room").(*RoomC)
	return c
}

// ContainerC is a private physical container, independent from household StashC and horse bags.
type ContainerC struct {
	RoomID   core.ID          `json:"room_id"`
	Owner    core.ID          `json:"owner"`
	Type     string           `json:"kind"`
	Capacity float64          `json:"capacity"`
	Items    map[string]int   `json:"items"`
	Goods    map[string]float64 `json:"goods"`
	Revision uint64           `json:"revision"`
}

func (c *ContainerC) Kind() string { return "container" }
func Container(e *core.Entity) *ContainerC {
	if e == nil { return nil }
	c, _ := e.Get("container").(*ContainerC)
	return c
}

// RoomLeaseC is the player's stable reference to their rented room.
type RoomLeaseC struct { RoomID core.ID `json:"room_id"` }
func (c *RoomLeaseC) Kind() string { return "room_lease" }
func RoomLease(e *core.Entity) *RoomLeaseC {
	if e == nil { return nil }
	c, _ := e.Get("room_lease").(*RoomLeaseC)
	return c
}

// RoomVisitC is server-owned door/room presence. Clients may render the interior only after EnterRoom.
type RoomVisitC struct {
	RoomID    core.ID `json:"room_id"`
	Inside    bool    `json:"inside"`
	ChestOpen bool    `json:"chest_open"`
	Recovery  bool    `json:"recovery"`
}
func (c *RoomVisitC) Kind() string { return "room_visit" }
func RoomVisit(e *core.Entity) *RoomVisitC {
	if e == nil { return nil }
	c, _ := e.Get("room_visit").(*RoomVisitC)
	return c
}
func InRoom(e *core.Entity) bool {
	v := RoomVisit(e)
	return v != nil && (v.Inside || v.Recovery)
}

// EnsureTavernRooms creates the two starter rooms once. IDs are stable and snapshots preserve them.
func EnsureTavernRooms(w *core.World, settlement *core.Entity) {
	if w == nil || settlement == nil { return }
	st := core.Settle(settlement)
	if st == nil || !st.Tavern { return }
	existing := map[int]bool{}
	w.Each("room", func(re *core.Entity) {
		r := Room(re)
		if r != nil && r.Settlement == settlement.ID { existing[r.Number] = true }
	})
	for number := 1; number <= TavernRoomCount; number++ {
		if existing[number] { continue }
		chest := w.Spawn(&ContainerC{
			Type: "room_chest", Capacity: RoomChestCapacity,
			Items: map[string]int{}, Goods: map[string]float64{},
		})
		roomEntity := w.Spawn(&RoomC{
			Settlement: settlement.ID, Building: "tavern", Number: number,
			DoorPOI: "tavern", State: "available", RentPerDay: RoomDailyRent,
			ContainerID: chest.ID,
		})
		chest.RoomID = roomEntity.ID
	}
}

func roomDoor(w *core.World, e *core.Entity, r *RoomC) bool {
	if e == nil || r == nil || OnRoad(e) || !atPoi(e, r.DoorPOI) { return false }
	pl := core.Place(e)
	return pl != nil && pl.Settlement == r.Settlement && inTavernSettlement(w, e)
}

func roomForPlayer(w *core.World, e *core.Entity) (*core.Entity, *RoomC) {
	lease := RoomLease(e)
	if lease == nil || lease.RoomID == 0 { return nil, nil }
	re := w.Get(lease.RoomID)
	return re, Room(re)
}

func containerEmpty(c *ContainerC) bool {
	if c == nil { return true }
	for _, n := range c.Items { if n > 0 { return false } }
	for _, n := range c.Goods { if n > 0.0001 { return false } }
	return true
}

func forceLeaveRoom(w *core.World, r *RoomC) {
	if r == nil { return }
	if owner := w.Get(r.Tenant); owner != nil {
		if visit := RoomVisit(owner); visit != nil && visit.Inside {
			visit.Inside, visit.ChestOpen, visit.Recovery = false, false, false
		}
	}
}

func releaseRoom(w *core.World, re *core.Entity, r *RoomC) {
	if w == nil || re == nil || r == nil { return }
	tenant := r.Tenant
	if owner := w.Get(tenant); owner != nil {
		w.Detach(owner, "room_lease")
		w.Detach(owner, "room_visit")
	}
	if chest := Container(w.Get(r.ContainerID)); chest != nil {
		chest.Owner = 0
		chest.Items = map[string]int{}
		chest.Goods = map[string]float64{}
		chest.Revision++
	}
	r.Tenant, r.State = 0, "available"
	r.PaidThroughDay, r.GraceUntilDay = 0, 0
}

func RentRoom(w *core.World, e *core.Entity, day int64, reqid string) (bool, string) {
	if e == nil { return false, "нет тела" }
	if reqid == "" { return false, "нужен reqid" }
	pc := EnsurePlayer(w, e)
	if pc.alreadyDone(reqid) { return true, "" }
	if OnRoad(e) || !atPoi(e, "tavern") || !inTavernSettlement(w, e) {
		return false, "комнаты снимают у корчмаря"
	}
	if h := core.Home(e); h != nil && h.Status == "owned" {
		return false, "у тебя уже есть собственный дом"
	}
	if lease := RoomLease(e); lease != nil {
		if _, r := roomForPlayer(w, e); r != nil && r.Tenant == e.ID {
			return false, "у тебя уже есть комната"
		}
		w.Detach(e, "room_lease")
	}
	pl := core.Place(e)
	se := w.Get(pl.Settlement)
	EnsureTavernRooms(w, se)
	var candidates []*core.Entity
	w.Each("room", func(re *core.Entity) {
		r := Room(re)
		if r != nil && r.Settlement == pl.Settlement {
			if r.State == "expired" && containerEmpty(Container(w.Get(r.ContainerID))) {
				releaseRoom(w, re, r)
			}
			if r.Tenant == 0 && r.State == "available" { candidates = append(candidates, re) }
		}
	})
	sort.Slice(candidates, func(i, j int) bool { return Room(candidates[i]).Number < Room(candidates[j]).Number })
	if len(candidates) == 0 { return false, "все комнаты заняты" }
	pu := core.Purse(e)
	r := Room(candidates[0])
	if pu == nil || pu.Coins < r.RentPerDay {
		return false, fmt.Sprintf("нужно %.0f монет за первые сутки", r.RentPerDay)
	}
	chestEntity := w.Get(r.ContainerID)
	chest := Container(chestEntity)
	if chest == nil {
		chestEntity = w.Spawn(&ContainerC{Type: "room_chest", Capacity: RoomChestCapacity, Items: map[string]int{}, Goods: map[string]float64{}})
		chest = Container(chestEntity)
		r.ContainerID = chestEntity.ID
	}
	pu.Coins -= r.RentPerDay
	r.Tenant, r.State = e.ID, "rented"
	r.PaidThroughDay, r.GraceUntilDay = day, 0
	chest.RoomID, chest.Owner = candidates[0].ID, e.ID
	if chest.Items == nil { chest.Items = map[string]int{} }
	if chest.Goods == nil { chest.Goods = map[string]float64{} }
	w.Attach(e, &RoomLeaseC{RoomID: candidates[0].ID})
	pc.markDone(reqid)
	return true, ""
}

func EnterRoom(w *core.World, e *core.Entity, day int64, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	re, r := roomForPlayer(w, e)
	if r == nil || r.Tenant != e.ID { return false, "у тебя нет арендованной комнаты" }
	if !roomDoor(w, e, r) { return false, "подойди к двери своей комнаты в корчме" }
	if r.State == "grace" && day > r.GraceUntilDay {
		r.State = "expired"
		forceLeaveRoom(w, r)
	}
	if r.State != "rented" && r.State != "grace" { return false, "срок комнаты истёк — обратись к корчмарю за вещами" }
	w.Attach(e, &RoomVisitC{RoomID: re.ID, Inside: true})
	pc.markDone(reqid)
	return true, ""
}

func OpenRoomChest(w *core.World, e *core.Entity, day int64, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	visit := RoomVisit(e)
	_, r := roomForPlayer(w, e)
	if visit == nil || r == nil || visit.RoomID != RoomLease(e).RoomID || r.Tenant != e.ID {
		return false, "сначала войди в свою комнату"
	}
	if !visit.Inside && !visit.Recovery { return false, "сундук сейчас недоступен" }
	if visit.Inside {
		if r.State == "grace" && day > r.GraceUntilDay { r.State = "expired" }
		if r.State != "rented" && r.State != "grace" {
			visit.Inside, visit.ChestOpen = false, false
			return false, "срок доступа к комнате истёк"
		}
	}
	if visit.Recovery && r.State != "expired" { return false, "возврат вещей пока не требуется" }
	if visit.Recovery && !roomDoor(w, e, r) { return false, "вернись к корчмарю за вещами" }
	visit.ChestOpen = true
	pc.markDone(reqid)
	return true, ""
}

func CloseRoomChest(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	visit := RoomVisit(e)
	if visit == nil || !visit.ChestOpen { return false, "сундук уже закрыт" }
	visit.ChestOpen = false
	pc.markDone(reqid)
	return true, ""
}

func LeaveRoom(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	visit := RoomVisit(e)
	if visit == nil { return false, "ты не в комнате" }
	w.Detach(e, "room_visit")
	pc.markDone(reqid)
	return true, ""
}

func RecoverRoomChest(w *core.World, e *core.Entity, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	re, r := roomForPlayer(w, e)
	if r == nil || re == nil || r.Tenant != e.ID || r.State != "expired" {
		return false, "возвращать вещи пока не требуется"
	}
	if !roomDoor(w, e, r) { return false, "обратись к корчмарю в своей корчме" }
	chest := Container(w.Get(r.ContainerID))
	if containerEmpty(chest) {
		releaseRoom(w, re, r)
		pc.markDone(reqid)
		return true, ""
	}
	w.Attach(e, &RoomVisitC{RoomID: re.ID, Recovery: true, ChestOpen: true})
	pc.markDone(reqid)
	return true, ""
}

func RenewRoom(w *core.World, e *core.Entity, day int64, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	re, r := roomForPlayer(w, e)
	if r == nil || re == nil || r.Tenant != e.ID { return false, "у тебя нет комнаты для продления" }
	if !roomDoor(w, e, r) { return false, "продлить комнату можно у корчмаря" }
	if r.State != "grace" && r.State != "expired" { return false, "комната уже оплачена" }
	dueDays := day - r.PaidThroughDay
	if dueDays < 1 { dueDays = 1 }
	due := r.RentPerDay * float64(dueDays)
	pu := core.Purse(e)
	if pu == nil || pu.Coins < due { return false, fmt.Sprintf("для продления нужно %.0f монет за просрочку", due) }
	pu.Coins -= due
	r.PaidThroughDay, r.GraceUntilDay, r.State = day, 0, "rented"
	pc.markDone(reqid)
	return true, ""
}

func itemContainerWeight(item string) float64 {
	switch item {
	case "sword", "shield", "jerkin": return 3
	case "axe", "bow", "lantern": return 2
	case "cloak", "boots", "rod", "torch": return 1
	case "purse", "hat": return 0.5
	default: return 1
	}
}

func containerLoad(c *ContainerC) float64 {
	if c == nil { return 0 }
	load := 0.0
	for _, n := range c.Goods { if n > 0 { load += n } }
	for item, n := range c.Items { if n > 0 { load += float64(n) * itemContainerWeight(item) } }
	return load
}

func MoveRoomContainer(w *core.World, e *core.Entity, day int64, kind, side, key string, qty int, reqid string) (bool, string) {
	pc := EnsurePlayer(w, e)
	if reqid == "" { return false, "нужен reqid" }
	if pc.alreadyDone(reqid) { return true, "" }
	if qty <= 0 || key == "" { return false, "укажи предмет и количество" }
	visit := RoomVisit(e)
	re, r := roomForPlayer(w, e)
	if visit == nil || !visit.ChestOpen || r == nil || re == nil || visit.RoomID != re.ID || r.Tenant != e.ID {
		return false, "сначала открой свой сундук"
	}
	if visit.Recovery {
		if r.State != "expired" || side != "withdraw" { return false, "из хранилища возврата можно только забрать вещи" }
		if !roomDoor(w, e, r) { return false, "вернись к корчмарю за вещами" }
	} else {
		if r.State == "grace" && day > r.GraceUntilDay {
			r.State = "expired"
			forceLeaveRoom(w, r)
			return false, "срок доступа к комнате истёк"
		}
		if !visit.Inside || (r.State != "rented" && r.State != "grace") {
			return false, "срок доступа к комнате истёк"
		}
	}
	chest := Container(w.Get(r.ContainerID))
	if chest == nil || chest.Owner != e.ID { return false, "сундук недоступен" }
	if side != "deposit" && side != "withdraw" { return false, "неизвестное направление переноса" }
	if chest.Capacity <= 0 { chest.Capacity = RoomChestCapacity }
	switch kind {
	case "item":
		inv := ensureInv(w, e)
		if side == "deposit" {
			if inv.Items[key] < qty { return false, "в суме недостаточно предметов" }
			added := float64(qty) * itemContainerWeight(key)
			if containerLoad(chest)+added > chest.Capacity+0.0001 { return false, "в сундуке не хватает места" }
			inv.Items[key] -= qty
			if inv.Items[key] == 0 { delete(inv.Items, key) }
			chest.Items[key] += qty
		} else if side == "withdraw" {
			if chest.Items[key] < qty { return false, "в сундуке недостаточно предметов" }
			chest.Items[key] -= qty
			if chest.Items[key] == 0 { delete(chest.Items, key) }
			inv.Items[key] += qty
		} else { return false, "неизвестное направление переноса" }
	case "good":
		st := ensureStashOf(w, e)
		amount := float64(qty)
		if side == "deposit" {
			if st.Goods[key] < amount { return false, "в закромах недостаточно товара" }
			if containerLoad(chest)+amount > chest.Capacity+0.0001 { return false, "в сундуке не хватает места" }
			st.Goods[key] -= amount
			if st.Goods[key] < 0.0001 { delete(st.Goods, key) }
			chest.Goods[key] += amount
		} else if side == "withdraw" {
			if chest.Goods[key] < amount { return false, "в сундуке недостаточно товара" }
			chest.Goods[key] -= amount
			if chest.Goods[key] < 0.0001 { delete(chest.Goods, key) }
			st.Goods[key] += amount
		} else { return false, "неизвестное направление переноса" }
	default:
		return false, "переносить можно только предметы или товары"
	}
	chest.Revision++
	pc.markDone(reqid)
	if visit.Recovery && containerEmpty(chest) { releaseRoom(w, re, r) }
	return true, ""
}

// RoomRentSystem charges once per new world day. Arrears are paid together or access enters grace.
type RoomRentSystem struct{}
func (RoomRentSystem) Name() string { return "room-rent" }
func (RoomRentSystem) Hour(w *core.World, clock core.Clock, _ *dice.Hub) {
	if !clock.IsNewDay() { return }
	day := clock.Day()
	w.Each("room", func(re *core.Entity) {
		r := Room(re)
		if r == nil || r.Tenant == 0 { return }
		chest := Container(w.Get(r.ContainerID))
		if r.State == "expired" {
			forceLeaveRoom(w, r)
			if containerEmpty(chest) { releaseRoom(w, re, r) }
			return
		}
		if day <= r.PaidThroughDay { return }
		dueDays := day - r.PaidThroughDay
		due := r.RentPerDay * float64(dueDays)
		owner := w.Get(r.Tenant)
		var pu *core.PurseC
		if owner != nil { pu = core.Purse(owner) }
		if pu != nil && pu.Coins >= due {
			pu.Coins = math.Max(0, pu.Coins-due)
			r.PaidThroughDay, r.GraceUntilDay, r.State = day, 0, "rented"
			return
		}
		if r.State != "grace" {
			r.State = "grace"
			r.GraceUntilDay = day + RoomGraceDays - 1
		}
		if day > r.GraceUntilDay { r.State = "expired" }
		if r.State == "expired" {
			forceLeaveRoom(w, r)
			if containerEmpty(chest) { releaseRoom(w, re, r) }
		}
	})
}

func RoomsView(w *core.World, e *core.Entity) []map[string]any {
	pl := core.Place(e)
	if pl == nil || pl.Settlement == 0 { return []map[string]any{} }
	EnsureTavernRooms(w, w.Get(pl.Settlement))
	var rooms []*core.Entity
	w.Each("room", func(re *core.Entity) {
		r := Room(re)
		if r != nil && r.Settlement == pl.Settlement { rooms = append(rooms, re) }
	})
	sort.Slice(rooms, func(i, j int) bool { return Room(rooms[i]).Number < Room(rooms[j]).Number })
	out := make([]map[string]any, 0, len(rooms))
	for _, re := range rooms {
		r := Room(re)
		out = append(out, map[string]any{
			"number": r.Number, "state": r.State, "rent_per_day": r.RentPerDay,
			"mine": r.Tenant == e.ID,
		})
	}
	return out
}

func RoomView(w *core.World, e *core.Entity, day int64) any {
	re, r := roomForPlayer(w, e)
	if r == nil || re == nil || r.Tenant != e.ID { return nil }
	visit := RoomVisit(e)
	inside := visit != nil && visit.RoomID == re.ID && visit.Inside
	recovery := visit != nil && visit.RoomID == re.ID && visit.Recovery
	chestOpen := visit != nil && visit.RoomID == re.ID && visit.ChestOpen
	var chestView any
	if chestOpen {
		c := Container(w.Get(r.ContainerID))
		if c != nil {
			items := map[string]int{}
			goods := map[string]float64{}
			for k, v := range c.Items { items[k] = v }
			for k, v := range c.Goods { goods[k] = v }
			chestView = map[string]any{
				"revision": c.Revision, "capacity": c.Capacity, "load": math.Round(containerLoad(c)*100)/100,
				"items": items, "goods": goods,
			}
		}
	}
	return map[string]any{
		"room_id": fmt.Sprint(uint64(re.ID)), "number": r.Number, "building": r.Building,
		"status": r.State, "rent_per_day": r.RentPerDay, "paid_through_day": r.PaidThroughDay,
		"grace_until_day": r.GraceUntilDay, "inside": inside, "chest_open": chestOpen,
		"recovery": recovery, "recovery_available": r.State == "expired",
		"container": chestView, "day": day,
	}
}
