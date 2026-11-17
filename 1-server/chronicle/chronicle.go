// Пакет chronicle — ЛЕТОПИСЬ мира. Несущая тема «Приграничья»: мир постоянен, персонажи преходящи,
// и МИР ПОМНИТ. Сюда ложатся заметные события — основания сёл, войны, моры, восхождение городов,
// пробуждения магов, уход заметных людей. Это память самого мира (не отдельного игрока): единая
// сущность-летопись живёт в мире и переживает снимок вместе со всем остальным.
//
// Храним кольцом последних записей (чтобы снимок не рос без предела) плюс ПОЖИЗНЕННЫЕ счётчики по
// родам событий — так «давнее» не теряется совсем (остаётся числом), а свежее читается построчно.
package chronicle

import "pogranichye/core"

func init() {
	core.Register("chronicle", func() core.Component {
		return &ChronicleC{Tally: map[string]int{}}
	})
}

const maxEntries = 400 // сколько последних записей держим построчно (старее — сворачивается в счётчики)

// Entry — одна строка летописи: когда (день мира), какого рода событие и человеческое описание.
type Entry struct {
	Day  int64  `json:"day"`
	Kind string `json:"kind"`
	Text string `json:"text"`
}

// ChronicleC — память мира: последние записи и пожизненные счётчики по родам событий.
type ChronicleC struct {
	Entries []Entry        `json:"entries"`
	Tally   map[string]int `json:"tally"`
}

func (c *ChronicleC) Kind() string { return "chronicle" }

// Of — найти летопись мира (единственная сущность с этим компонентом), или nil.
func Of(w *core.World) *ChronicleC {
	var out *ChronicleC
	w.Each("chronicle", func(e *core.Entity) {
		if out == nil {
			out = e.Get("chronicle").(*ChronicleC)
		}
	})
	return out
}

// Record заносит событие в летопись мира: растит пожизненный счётчик рода и дописывает строку,
// сворачивая самое старое, если записей стало больше предела. Нет летописи — тихо ничего.
func Record(w *core.World, day int64, kind, text string) {
	c := Of(w)
	if c == nil {
		return
	}
	if c.Tally == nil {
		c.Tally = map[string]int{}
	}
	c.Tally[kind]++
	c.Entries = append(c.Entries, Entry{Day: day, Kind: kind, Text: text})
	if len(c.Entries) > maxEntries {
		c.Entries = c.Entries[len(c.Entries)-maxEntries:]
	}
}
