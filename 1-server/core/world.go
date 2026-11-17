package core

import (
	"encoding/json"
	"sort"
)

// World — весь мир в памяти: все сущности и индекс по видам компонентов, чтобы системы
// быстро находили «всех, у кого есть Vitals» и т.п. Ядро не знает правил — только держит
// сущности и даёт их перебирать. NextID всегда растёт: id не переиспользуются.
type World struct {
	Seed     string
	NextID   ID
	entities  map[ID]*Entity
	byKind    map[string]map[ID]*Entity
	byKindIDs map[string][]ID // stable ascending index: Each() does not allocate or sort per pass
}

// NewWorld создаёт пустой мир с данным семенем (семя кормит кости — пакет dice).
func NewWorld(seed string) *World {
	return &World{
		Seed:     seed,
		NextID:   1,
		entities:  map[ID]*Entity{},
		byKind:    map[string]map[ID]*Entity{},
		byKindIDs: map[string][]ID{},
	}
}

// Spawn создаёт сущность со свежим id и навешивает переданные компоненты.
func (w *World) Spawn(comps ...Component) *Entity {
	e := &Entity{ID: w.NextID, Comps: map[string]Component{}}
	w.NextID++
	w.entities[e.ID] = e
	for _, c := range comps {
		e.Add(c)
		w.index(e, c.Kind())
	}
	return e
}

// Attach вешает компонент на существующую сущность и чинит индекс.
func (w *World) Attach(e *Entity, c Component) {
	e.Add(c)
	w.index(e, c.Kind())
}

// Detach снимает компонент и чинит индекс.
func (w *World) Detach(e *Entity, kind string) {
	e.Remove(kind)
	if m, ok := w.byKind[kind]; ok {
		delete(m, e.ID)
		w.removeKindID(kind, e.ID)
	}
}

func (w *World) index(e *Entity, kind string) {
	m := w.byKind[kind]
	if m == nil {
		m = map[ID]*Entity{}
		w.byKind[kind] = m
	}
	m[e.ID] = e
	ids := w.byKindIDs[kind]
	at := sort.Search(len(ids), func(i int) bool { return ids[i] >= e.ID })
	if at == len(ids) || ids[at] != e.ID {
		ids = append(ids, 0)
		copy(ids[at+1:], ids[at:])
		ids[at] = e.ID
		w.byKindIDs[kind] = ids
	}
}

func (w *World) removeKindID(kind string, id ID) {
	ids := w.byKindIDs[kind]
	at := sort.Search(len(ids), func(i int) bool { return ids[i] >= id })
	if at < len(ids) && ids[at] == id {
		copy(ids[at:], ids[at+1:])
		w.byKindIDs[kind] = ids[:len(ids)-1]
	}
}

// Get возвращает сущность по id (или nil).
func (w *World) Get(id ID) *Entity { return w.entities[id] }

// Remove убирает сущность целиком (например, умерла и выносится из мира — но её
// идентичность уже записана в хронику/память, это дело систем, а не ядра).
func (w *World) Remove(id ID) {
	e := w.entities[id]
	if e == nil {
		return
	}
	for kind := range e.Comps {
		if m, ok := w.byKind[kind]; ok {
			delete(m, id)
			w.removeKindID(kind, id)
		}
	}
	delete(w.entities, id)
}

// HasKind reports whether the world currently contains at least one entity with kind.
// It is O(1) and lets hot-path systems choose the appropriate indexed population.
func (w *World) HasKind(kind string) bool { return len(w.byKindIDs[kind]) != 0 }

// CountKind returns the number of entities carrying kind using the maintained index.
// It is O(1) and is safe for hot-path change detection.
func (w *World) CountKind(kind string) int { return len(w.byKindIDs[kind]) }

// Count — сколько всего сущностей.
func (w *World) Count() int { return len(w.entities) }

// Each перебирает все сущности, у которых есть компонент kind. Порядок — по возрастанию
// id (детерминированно: при одном семени системы ходят по сущностям в одном порядке,
// иначе случайность «поедет»). fn может помечать на удаление, но не удалять на ходу.
func (w *World) Each(kind string, fn func(e *Entity)) {
	ids := w.byKindIDs[kind]
	for i := 0; i < len(ids); i++ {
		if e := w.entities[ids[i]]; e != nil {
			fn(e)
		}
	}
}

// AllSorted — все сущности в детерминированном порядке (для снимка/инструментов).
func (w *World) AllSorted() []*Entity {
	ids := make([]ID, 0, len(w.entities))
	for id := range w.entities {
		ids = append(ids, id)
	}
	sort.Slice(ids, func(i, j int) bool { return ids[i] < ids[j] })
	out := make([]*Entity, 0, len(ids))
	for _, id := range ids {
		out = append(out, w.entities[id])
	}
	return out
}

// --- снимок мира (лоссless: сохраняем КАЖДУЮ сущность и КАЖДЫЙ компонент) ---

// WorldWire — проводной/дисковый вид мира. Сюда же кладётся снимок костей (hub) — но его
// подмешивает уровень memory, чтобы ядро не зависело от пакета dice.
type WorldWire struct {
	Seed     string          `json:"seed"`
	NextID   ID              `json:"next_id"`
	Entities []entityWire    `json:"entities"`
	Extra    json.RawMessage `json:"extra,omitempty"` // место для снимка костей и прочего
}

// Dump сворачивает мир в проводной вид без потерь. extra — произвольный довесок
// (например, снимок хаба костей), который кладётся рядом и возвращается при Load.
func (w *World) Dump(extra json.RawMessage) (WorldWire, error) {
	out := WorldWire{Seed: w.Seed, NextID: w.NextID, Extra: extra}
	for _, e := range w.AllSorted() {
		ew := entityWire{ID: e.ID, Comps: map[string]json.RawMessage{}}
		// компоненты в стабильном порядке ключей — детерминированный снимок
		kinds := make([]string, 0, len(e.Comps))
		for k := range e.Comps {
			kinds = append(kinds, k)
		}
		sort.Strings(kinds)
		for _, k := range kinds {
			raw, err := json.Marshal(e.Comps[k])
			if err != nil {
				return out, err
			}
			ew.Comps[k] = raw
		}
		out.Entities = append(out.Entities, ew)
	}
	return out, nil
}

// LoadWorld восстанавливает мир из проводного вида. Неизвестные виды компонентов
// пропускаются (загрузка не падает), зато всё зарегистрированное возвращается точь-в-точь.
func LoadWorld(wire WorldWire) *World {
	w := NewWorld(wire.Seed)
	w.NextID = wire.NextID
	for _, ew := range wire.Entities {
		e := &Entity{ID: ew.ID, Comps: map[string]Component{}}
		w.entities[e.ID] = e
		for kind, raw := range ew.Comps {
			c := newComponent(kind)
			if c == nil {
				continue // неизвестное свойство — пропускаем, но мир не роняем
			}
			if err := json.Unmarshal(raw, c); err != nil {
				continue
			}
			e.Comps[kind] = c
			w.index(e, kind)
		}
	}
	return w
}
