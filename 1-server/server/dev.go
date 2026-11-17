package server

import (
	"fmt"
	"log"
	"math"
	"os"
	"strconv"
	"strings"
	"time"

	"pogranichye/core"
	"pogranichye/sim"
)

// Режим разработчика (task_ai/027, comms 0276): ход времени мира и скорость шага своего персонажа —
// только для аккаунтов из списка POG_DEV_ACCOUNTS. Право решает сервер по аккаунту проверенной
// сессии; ничего, что прислал клиент, его не даёт. Список пуст — режима нет ни у кого.
//
// POG_DEV_ACCOUNTS — через запятую числовые id аккаунтов и/или логины. Логин — по прямому решению
// владельца игры (Kate: «user@user.user — это мой профиль»); id надёжнее, потому что логин может
// занять тот, кто зарегистрируется первым на пустом сервере.

const (
	devScaleMin, devScaleMax = 0.1, 20.0 // множитель хода времени мира
	devWalkMin, devWalkMax   = 0.5, 5.0  // множитель шага своего персонажа
	devStepMax               = 168       // за раз — не больше недели игровых часов
	devHoursPerFast          = 50        // предохранитель: часов мира за один шаг петли
)

type devState struct {
	ids    map[uint64]bool
	logins map[string]bool
	uidOf  map[core.ID]uint64 // персонаж → аккаунт его текущей сессии
	scale  float64            // ход времени: 1 — обычный
	paused bool
	acc    time.Duration // накоплено реального времени к следующему игровому часу
	walk   map[core.ID]float64
	done   map[string]string // reqid → исход (повтор не применяется дважды)
}

func parseDevAccounts(v string) (map[uint64]bool, map[string]bool) {
	ids, logins := map[uint64]bool{}, map[string]bool{}
	for _, part := range strings.Split(v, ",") {
		p := strings.ToLower(strings.TrimSpace(part))
		if p == "" {
			continue
		}
		if id, err := strconv.ParseUint(p, 10, 64); err == nil && id > 0 {
			ids[id] = true
		} else {
			logins[p] = true
		}
	}
	return ids, logins
}

// devLocked — состояние режима (под s.mu). Создаётся при первом обращении из POG_DEV_ACCOUNTS.
func (s *Server) devLocked() *devState {
	if s.dev == nil {
		ids, logins := parseDevAccounts(os.Getenv("POG_DEV_ACCOUNTS"))
		s.dev = &devState{ids: ids, logins: logins, uidOf: map[core.ID]uint64{}, scale: 1,
			walk: map[core.ID]float64{}, done: map[string]string{}}
	}
	return s.dev
}

// SetDevAccounts — список разработчиков из кода (тесты, инструменты). Заменяет POG_DEV_ACCOUNTS.
func (s *Server) SetDevAccounts(list string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	d := s.devLocked()
	d.ids, d.logins = parseDevAccounts(list)
}

// isDevLocked — есть ли у персонажа право: его сессия принадлежит аккаунту из списка.
func (s *Server) isDevLocked(cid core.ID) bool {
	d := s.devLocked()
	uid := d.uidOf[cid]
	if uid == 0 {
		return false
	}
	if d.ids[uid] {
		return true
	}
	if len(d.logins) == 0 || s.acc == nil {
		return false
	}
	return d.logins[strings.ToLower(s.acc.LoginOf(uid))]
}

// hoursDue — сколько игровых часов пора прокрутить за прошедший шаг реального времени, с учётом
// паузы и множителя хода времени (под s.mu).
func (s *Server) hoursDue(real time.Duration) int {
	d := s.devLocked()
	if d.paused {
		return 0
	}
	d.acc += time.Duration(float64(real) * d.scale)
	n := 0
	for d.acc >= s.tickEach && n < devHoursPerFast {
		d.acc -= s.tickEach
		n++
	}
	if n == devHoursPerFast {
		d.acc = 0 // не копим долг, если мир не успевает
	}
	return n
}

// walkMultLocked — множитель шага персонажа от режима разработчика (1 — нет).
func (s *Server) walkMultLocked(cid core.ID) float64 {
	if m := s.devLocked().walk[cid]; m > 0 {
		return m
	}
	return 1
}

func devClamp(v, lo, hi float64) (float64, bool) {
	if math.IsNaN(v) || math.IsInf(v, 0) {
		return 0, false
	}
	return math.Max(lo, math.Min(hi, v)), true
}

// applyDev — команда op:"dev" (под s.mu). Возвращает исход для me.result.
func (s *Server) applyDev(cid core.ID, action string, value float64, reqid string) (bool, string) {
	d := s.devLocked()
	if !s.isDevLocked(cid) {
		return false, "режим разработчика недоступен этому аккаунту"
	}
	if reqid != "" {
		if why, seen := d.done[reqid]; seen {
			return true, why
		}
	}
	clock := &s.runner.Clock
	var why string
	switch action {
	case "pause":
		d.paused, why = true, "время мира остановлено"
	case "resume":
		d.paused, why = false, "время мира идёт"
	case "time_scale":
		v, ok := devClamp(value, devScaleMin, devScaleMax)
		if !ok {
			return false, "нужно число"
		}
		d.scale, why = v, fmt.Sprintf("время мира идёт ×%.2g", v)
	case "step_hours":
		n := int(math.Round(value))
		if n < 1 || n > devStepMax {
			return false, fmt.Sprintf("шаг — от 1 до %d часов", devStepMax)
		}
		for i := 0; i < n; i++ {
			s.runner.Step(s.world)
		}
		why = fmt.Sprintf("мир прокручен на %d ч", n)
	case "set_hour":
		h := int(math.Round(value))
		if h < 0 || h >= core.HoursPerDay {
			return false, "час — от 0 до 23"
		}
		n := 0
		for clock.Hour() != h && n < core.HoursPerDay { // только вперёд: прошлое мира не отматываем
			s.runner.Step(s.world)
			n++
		}
		why = fmt.Sprintf("теперь %02d:00 (прокручено %d ч)", h, n)
	case "walk_speed":
		v, ok := devClamp(value, devWalkMin, devWalkMax)
		if !ok {
			return false, "нужно число"
		}
		d.walk[cid], why = v, fmt.Sprintf("шаг персонажа ×%.2g (тестовая прибавка)", v)
	case "reset":
		d.paused, d.scale, d.acc = false, 1, 0
		delete(d.walk, cid)
		why = "время и шаг — как обычно"
	default:
		return false, "неизвестная команда разработчика"
	}
	if reqid != "" {
		if len(d.done) > 512 {
			d.done = map[string]string{}
		}
		d.done[reqid] = why
	}
	log.Printf("dev: аккаунт=%d персонаж=%d действие=%s значение=%v reqid=%q → %s", d.uidOf[cid], cid, action, value, reqid, why)
	return true, why
}

// devView — me.dev: только разработчику; остальным поля нет вовсе.
func (s *Server) devView(cid core.ID) any {
	if !s.isDevLocked(cid) {
		return nil
	}
	d := s.devLocked()
	return map[string]any{"enabled": true, "paused": d.paused, "time_scale": d.scale, "walk_mult": s.walkMultLocked(cid),
		"hour": s.runner.Clock.Hour(), "day": s.runner.Clock.Day(),
		"real_minutes_per_hour": math.Round(s.tickEach.Minutes()/d.scale*10) / 10,
		"actions":               []string{"pause", "resume", "time_scale", "step_hours", "set_hour", "walk_speed", "reset"},
		"limits": map[string]any{"time_scale": []float64{devScaleMin, devScaleMax}, "walk_mult": []float64{devWalkMin, devWalkMax},
			"step_hours_max": devStepMax}}
}

var _ = sim.SetResult

// ExpandWorld — добавить новые края (три державы с большими городами и 50 деревень), если в мире их
// ещё нет. Безопасно звать на каждом старте: второй раз ничего не делает.
func (s *Server) ExpandWorld() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return sim.ExpandWorld(s.world, int64(s.runner.Clock.Day()))
}
