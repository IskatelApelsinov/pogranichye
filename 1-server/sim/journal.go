package sim

import "pogranichye/core"

// Личная летопись игрока (#54, вопрос 2 ChatGPT): server-authoritative вехи, которые тонкий клиент и
// Unity показывают во вкладке «Журнал». Аддитивно к кадру (контракт остаётся "v":1): поле me.journal
// легаси-клиент просто игнорирует.
//
// Записи появляются ТОЛЬКО по факту изменения РЕАЛЬНОГО состояния мира (знакомство с жителем, первый
// труд, кружка пива в корчме) — не по нажатию кнопки и не оптимистично. Монотонный Seq растёт строго
// вверх, набор вех конечен, поэтому список ограничен по росту; на всякий случай есть ring journalCap.
// Идемпотентность: веха каждого рода (Kind) записывается ровно один раз — повторный пересчёт кадра
// не плодит дублей.

const journalCap = 32

// JournalEntry — одна веха личной летописи.
type JournalEntry struct {
	Seq  int64  `json:"seq"`
	Day  int64  `json:"day"`
	Kind string `json:"kind"`
	Text string `json:"text"`
}

// JournalC — личная летопись игрока (компонент сущности). Seq и Entries сериализуемы, чтобы летопись
// переживала снапшот мира.
type JournalC struct {
	Seq     int64          `json:"seq"`
	Entries []JournalEntry `json:"entries"`
}

func (c *JournalC) Kind() string { return "journal" }

func init() { core.Register("journal", func() core.Component { return &JournalC{} }) }

func journalOf(e *core.Entity) *JournalC { c, _ := e.Get("journal").(*JournalC); return c }

func ensureJournal(w *core.World, e *core.Entity) *JournalC {
	if c := journalOf(e); c != nil {
		return c
	}
	c := &JournalC{}
	w.Attach(e, c)
	return c
}

// has — записана ли уже веха этого рода (Kind служит ключом идемпотентности).
func (c *JournalC) has(kind string) bool {
	for _, en := range c.Entries {
		if en.Kind == kind {
			return true
		}
	}
	return false
}

// record добавляет веху рода kind ровно один раз (по возрастанию Seq, с кольцевым ограничением).
func (c *JournalC) record(day int64, kind, text string) {
	if c.has(kind) {
		return
	}
	c.Seq++
	c.Entries = append(c.Entries, JournalEntry{Seq: c.Seq, Day: day, Kind: kind, Text: text})
	if len(c.Entries) > journalCap {
		c.Entries = c.Entries[len(c.Entries)-journalCap:]
	}
}

// Journal — личная летопись игрока для кадра. Сверяет вехи с РЕАЛЬНЫМ состоянием, дозаписывает новые
// (идемпотентно) и возвращает текущие записи (возрастающий Seq). Клиент дорисовывает новые по Seq.
func Journal(w *core.World, e *core.Entity, day int64) []map[string]any {
	c := ensureJournal(w, e)

	// веха «met»: игрок познакомился хотя бы с одним жителем.
	if a := Acquaint(e); a != nil && len(a.Known) > 0 {
		c.record(day, "met", "Познакомился с жителем деревни.")
	}
	// веха «worked»: игрок впервые поработал (любой рабочий навык поднялся выше нуля).
	if sk := core.Skills(e); sk != nil {
		for _, s := range []string{"woodcutter", "fisher", "builder", "hunter"} {
			if sk.Level(s) > 0 {
				c.record(day, "worked", "Впервые поработал своими руками.")
				break
			}
		}
	}
	// веха «drank»: игрок выпил кружку пива в корчме.
	if p := Player(e); p != nil && p.DrankBeer {
		c.record(day, "drank", "Выпил кружку пива в корчме.")
	}

	out := make([]map[string]any, 0, len(c.Entries))
	for _, en := range c.Entries {
		out = append(out, map[string]any{"seq": en.Seq, "day": en.Day, "kind": en.Kind, "text": en.Text})
	}
	return out
}
