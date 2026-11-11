package sim

import (
	"errors"
	"fmt"
	"math"

	"pogranichye/core"
)

const (
	ContainerRoomChest = "room_chest"
	ContainerBackpack  = "backpack"

	RoomLeaseVacant  = "vacant"
	RoomLeaseActive  = "active"
	RoomLeaseGrace   = "grace"
	RoomLeaseExpired = "expired"
)

// ContainerC хранит физическое содержимое отдельной сущности.
// StashC (домашние bulk goods) и HorseC.Bag (седельная поклажа) намеренно не являются ContainerC.
type ContainerC struct {
	ContainerKind string             `json:"kind"`
	Capacity     float64            `json:"capacity"`
	Goods        map[string]float64 `json:"goods"`
	Items        map[string]int     `json:"items"`
	Owner        core.ID            `json:"owner,omitempty"`
	Tenant       core.ID            `json:"tenant,omitempty"`
	AccessPolicy string             `json:"access_policy"`
	Revision     uint64             `json:"revision"`
}

func init() {
	core.Register("container", func() core.Component {
		return &ContainerC{Goods: map[string]float64{}, Items: map[string]int{}, AccessPolicy: "owner"}
	})
	core.Register("room", func() core.Component { return &RoomC{} })
}

func (c *ContainerC) Kind() string { return "container" }

// Load — текущая загрузка в условных единицах. Это не масса и не объём предметов.
func (c *ContainerC) Load() float64 {
	if c == nil {
		return 0
	}
	total := 0.0
	for _, n := range c.Goods {
		if n > 0 {
			total += n
		}
	}
	for _, n := range c.Items {
		if n > 0 {
			total += float64(n)
		}
	}
	return total
}

func (c *ContainerC) Free() float64 {
	if c == nil {
		return 0
	}
	return c.Capacity - c.Load()
}

// RoomC описывает арендуемую комнату. Сам сундук — отдельная сущность ContainerC,
// на которую указывает ChestID. Room entity ID является стабильным Room ID.
type RoomC struct {
	Settlement    core.ID `json:"settlement"`
	Building      core.ID `json:"building,omitempty"`
	DoorPOI       string  `json:"door_poi,omitempty"`
	Tenant        core.ID `json:"tenant,omitempty"`
	ChestID       core.ID `json:"chest_id"`
	RentPrice     float64 `json:"rent_price"`
	PaidThrough   int64   `json:"paid_through_day,omitempty"`
	GraceUntil    int64   `json:"grace_until_day,omitempty"`
	LeaseState    string  `json:"lease_state"`
}

func (c *RoomC) Kind() string { return "room" }

func Room(e *core.Entity) *RoomC {
	if e == nil {
		return nil
	}
	c, _ := e.Get("room").(*RoomC)
	return c
}

func Container(e *core.Entity) *ContainerC {
	if e == nil {
		return nil
	}
	c, _ := e.Get("container").(*ContainerC)
	return c
}

// CreateRoom создаёт комнату и её сундук как две независимые сохраняемые сущности.
// Геометрия/дверь задаются вызывающей стороной; эта функция не телепортирует игрока и не
// предоставляет доступ к комнате сама по себе.
func CreateRoom(w *core.World, settlement, building core.ID, doorPOI string, rentPrice, chestCapacity float64) (*core.Entity, *core.Entity, error) {
	if w == nil || settlement == 0 {
		return nil, nil, errors.New("комната должна принадлежать поселению")
	}
	if math.IsNaN(rentPrice) || math.IsInf(rentPrice, 0) || rentPrice <= 0 ||
		math.IsNaN(chestCapacity) || math.IsInf(chestCapacity, 0) || chestCapacity <= 0 {
		return nil, nil, errors.New("цена аренды и вместимость должны быть положительными")
	}
	chest := w.Spawn(&ContainerC{
		ContainerKind: ContainerRoomChest, Capacity: chestCapacity,
		Goods: map[string]float64{}, Items: map[string]int{},
		AccessPolicy: "tenant",
	})
	room := w.Spawn(&RoomC{
		Settlement: settlement, Building: building, DoorPOI: doorPOI,
		ChestID: chest.ID, RentPrice: rentPrice, LeaseState: RoomLeaseVacant,
	})
	return room, chest, nil
}

// MoveContainerGoods атомарно переносит bulk good между двумя контейнерами.
// Capacity считается в условных единицах, без притворной системы массы/объёма.
func MoveContainerGoods(from, to *ContainerC, good string, qty float64) error {
	if from == nil || to == nil || from == to {
		return errors.New("нужны два разных контейнера")
	}
	if good == "" || math.IsNaN(qty) || math.IsInf(qty, 0) || qty <= 0 {
		return errors.New("укажи товар и положительное количество")
	}
	if from.Goods[good]+1e-9 < qty {
		return fmt.Errorf("недостаточно товара: %s", good)
	}
	if to.Capacity <= 0 || to.Load()+qty > to.Capacity+1e-9 {
		return errors.New("контейнер переполнен")
	}
	if from.Goods == nil {
		return errors.New("в источнике нет товара")
	}
	if to.Goods == nil {
		to.Goods = map[string]float64{}
	}
	// Все проверки завершены до первой записи: отказ не меняет ни одну сторону.
	from.Goods[good] -= qty
	if from.Goods[good] < 1e-9 {
		delete(from.Goods, good)
	}
	to.Goods[good] += qty
	from.Revision++
	to.Revision++
	return nil
}

// MoveContainerItems атомарно переносит stackable item между двумя контейнерами.
func MoveContainerItems(from, to *ContainerC, item string, qty int) error {
	if from == nil || to == nil || from == to {
		return errors.New("нужны два разных контейнера")
	}
	if item == "" || qty <= 0 {
		return errors.New("укажи предмет и положительное количество")
	}
	if from.Items[item] < qty {
		return fmt.Errorf("недостаточно предметов: %s", item)
	}
	if to.Capacity <= 0 || to.Load()+float64(qty) > to.Capacity+1e-9 {
		return errors.New("контейнер переполнен")
	}
	if from.Items == nil {
		return errors.New("в источнике нет предметов")
	}
	if to.Items == nil {
		to.Items = map[string]int{}
	}
	from.Items[item] -= qty
	if from.Items[item] == 0 {
		delete(from.Items, item)
	}
	to.Items[item] += qty
	from.Revision++
	to.Revision++
	return nil
}
