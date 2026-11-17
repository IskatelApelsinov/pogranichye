package core

import "pogranichye/dice"

// System — одно правило мира (голод, старение, разум, экономика...). Системы и есть
// «логическая часть, то как мир думает» (блок 1.1). Каждая получает мир, время и хаб
// костей, и делает свой кусочек жизни. Добавить поведение = добавить систему в Runner;
// ядро и другие системы не трогаются. Это ключ к «меняем код как можно меньше».
type System interface {
	Name() string
	// Hour вызывается каждый час мира. Для «раз в сутки» система сама проверяет clock.IsNewDay().
	Hour(w *World, clock Clock, hub *dice.Hub)
}

// Runner прокручивает мир: один Step = один час, по очереди всеми системами. Порядок
// систем фиксирован (детерминизм), каждая берёт свой именованный поток костей по имени —
// поэтому добавление новой системы не сдвигает случайность старых.
type Runner struct {
	Clock   Clock
	Hub     *dice.Hub
	systems []System
}

// NewRunner собирает прокрутку из мира-семени и списка систем.
func NewRunner(seed string, systems ...System) *Runner {
	return &Runner{Hub: dice.NewHub(seed), systems: systems}
}

// NewRunnerWith собирает прокрутку из уже готовых часов и хаба костей (после загрузки
// снимка) — чтобы мир продолжился ровно с того места и с той же последовательностью бросков.
func NewRunnerWith(hub *dice.Hub, clock Clock, systems ...System) *Runner {
	return &Runner{Hub: hub, Clock: clock, systems: systems}
}

// Use добавляет систему (можно достраивать мир модулями на лету).
func (r *Runner) Use(s System) { r.systems = append(r.systems, s) }

// Systems возвращает подключённые системы (для инструментов/диагностики).
func (r *Runner) Systems() []System { return r.systems }

// Step — один час жизни мира: каждая система по очереди.
func (r *Runner) Step(w *World) {
	for _, s := range r.systems {
		s.Hour(w, r.Clock, r.Hub)
	}
	r.Clock.Advance()
}

// Run — прокрутить hours часов.
func (r *Runner) Run(w *World, hours int) {
	for i := 0; i < hours; i++ {
		r.Step(w)
	}
}
