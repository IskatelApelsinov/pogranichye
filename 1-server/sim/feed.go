package sim

import "pogranichye/core"

// Лента событий игрока (v1.10): короткие серверно-подтверждённые сообщения о том, что РЕАЛЬНО
// произошло с его телом (попал/промахнулся, ранен, плетение сорвалось, добыча, дорога). Клиент
// проигрывает каждое событие ОДИН раз по монотонному seq (как social_event). Кольцо ограничено.

const feedCap = 16

// FeedItem — одно событие ленты.
type FeedItem struct {
	Seq  int64   `json:"seq"`
	Kind string  `json:"kind"` // hit|miss|kill|hurt|block|dodge|ko|weave|backfire|learn|loot|road|quest|craft|party
	Text string  `json:"text"`
	Foe  string  `json:"foe,omitempty"`  // id врага, если событие про него
	Part string  `json:"part,omitempty"` // часть тела
	Dmg  float64 `json:"dmg,omitempty"`
}

// FeedC — лента событий игрока.
type FeedC struct {
	Seq   int64      `json:"seq"`
	Items []FeedItem `json:"items"`
}

func (c *FeedC) Kind() string { return "feed" }

func init() { core.Register("feed", func() core.Component { return &FeedC{} }) }

func feedOf(e *core.Entity) *FeedC { c, _ := e.Get("feed").(*FeedC); return c }

// Emit кладёт событие в ленту игрока. Для НПС (без PlayerC) — молча ничего не делает.
func Emit(w *core.World, e *core.Entity, it FeedItem) {
	if e == nil || Player(e) == nil {
		return
	}
	c := feedOf(e)
	if c == nil {
		c = &FeedC{}
		w.Attach(e, c)
	}
	c.Seq++
	it.Seq = c.Seq
	c.Items = append(c.Items, it)
	if len(c.Items) > feedCap {
		c.Items = c.Items[len(c.Items)-feedCap:]
	}
}

func say(w *core.World, e *core.Entity, kind, text string) {
	Emit(w, e, FeedItem{Kind: kind, Text: text})
}

// Feed — лента для кадра (возрастающий seq). Пусто, если событий не было.
func Feed(e *core.Entity) []FeedItem {
	if c := feedOf(e); c != nil && len(c.Items) > 0 {
		return c.Items
	}
	return []FeedItem{}
}
