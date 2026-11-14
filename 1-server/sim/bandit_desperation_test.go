package sim

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
	"pogranichye/econ"
)

// comms: «обычные НПС становятся бандитами, если денег нет и нужно заработать, а обычными
// способами не выходит» — честный путь это econ.Wage() (платит ВСЕГДА, так что «не нашёл
// работу» просто не бывает), поэтому единственный честный сигнал «не выходит» — это СТОЙКИЙ
// долг рынку при отсутствии своего дома (desperate*-константы в systems.go). Тест проверяет
// путь целиком: бездомный житель в глубоком долгу рано или поздно превращается в настоящую
// сущность foe species=bandit, с уважением к уже сложившемуся живому потолку (foeStats cap).
func TestHomelessDebtorBecomesBandit(t *testing.T) {
	w, hub := GenerateStarter("bandit-desperation")
	var se *core.Entity
	w.Each("settlement", func(s *core.Entity) {
		if se == nil && core.Settle(s).Name == "Починок" {
			se = s
		}
	})
	if se == nil {
		t.Fatal("нет стартового села")
	}
	// убираем живых бандитов из сцены — тест о ПОЯВЛЕНИИ нового, а не о тех, что уже в стане
	w.Each("foe", func(fe *core.Entity) {
		if Foe(fe).Species == "bandit" {
			w.Remove(fe.ID)
		}
	})

	var target *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		v := core.Vitals(r)
		if target == nil && v != nil && v.Age >= 18 && v.Age < 30 && core.Ident(r) != nil {
			target = r
		}
	}
	if target == nil {
		t.Fatal("нет подходящего взрослого жителя")
	}
	id := target.ID
	core.Home(target).Status = "none"
	pu := core.Purse(target)
	pu.Coins = 0
	pu.Debts = map[string]float64{"торг": desperatePovertyDebt + 5}
	if got := econ.MarketDebt(target); got < desperatePovertyDebt {
		t.Fatalf("econ.MarketDebt не видит выставленный долг: %v", got)
	}

	converted := false
	for day := int64(1); day <= 250 && !converted; day++ {
		clock := core.Clock{TotalHours: day * core.HoursPerDay}
		LifeSystem{}.Hour(w, clock, hub)
		if w.Get(id) == nil {
			converted = true
		}
	}
	if !converted {
		t.Fatal("бездомный должник не стал бандитом за 250 дней — путь не сработал")
	}

	found := false
	w.Each("foe", func(fe *core.Entity) {
		f := Foe(fe)
		if f.Species == "bandit" && f.Settlement == se.ID {
			found = true
			if f.HP <= 0 || f.Max <= 0 || f.State == "" {
				t.Fatalf("новый бандит без валидного hp/max/state: %+v", f)
			}
		}
	})
	if !found {
		t.Fatal("житель пропал из мира, но настоящего foe-бандита в сцене не появилось")
	}

	// живой потолок: бандитов в сцене не больше foeStats["bandit"].cap даже после конверсии
	alive := 0
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Species == "bandit" && f.Settlement == se.ID && f.State != "dead" {
			alive++
		}
	})
	if alive > foeStats["bandit"].cap {
		t.Fatalf("бандитов в сцене больше живого потолка: %d > %d", alive, foeStats["bandit"].cap)
	}
}

// Защита тела игрока: даже в точно такой же нищете (без дома, в долгу, на нуле монет) бывший/
// текущий игрок никогда не становится бандитом фоновой симуляцией — EnsurePlayer оставляет
// sim.Player(e)!=nil навсегда, и becomeBandit это проверяет.
func TestPlayerBodyNeverBecomesBandit(t *testing.T) {
	w, e, _ := starterPlayer(t)
	core.Home(e).Status = "none"
	pu := core.Purse(e)
	pu.Coins = 0
	pu.Debts = map[string]float64{"торг": desperatePovertyDebt + 20}

	hub := dice.NewHub("player-safety")
	for day := int64(1); day <= 250; day++ {
		clock := core.Clock{TotalHours: day * core.HoursPerDay}
		LifeSystem{}.Hour(w, clock, hub)
	}
	if w.Get(e.ID) == nil {
		t.Fatal("тело игрока исчезло из мира — фон не должен удалять игрока")
	}
	if core.Place(e) == nil {
		t.Fatal("ожидался place-компонент у тела игрока после долгой нищеты")
	}
}

// Уважение к живому потолку: когда сцена уже полна бандитов, новый кандидат НЕ добавляется
// сверх foeStats["bandit"].cap, даже оставаясь в глубокой нищете сколько угодно дней.
func TestBanditCapRespectedDuringConversion(t *testing.T) {
	w, hub := GenerateStarter("bandit-desperation-cap")
	var se *core.Entity
	w.Each("settlement", func(s *core.Entity) {
		if se == nil && core.Settle(s).Name == "Починок" {
			se = s
		}
	})
	if se == nil {
		t.Fatal("нет стартового села")
	}
	liveCap := foeStats["bandit"].cap
	// набиваем сцену живыми бандитами ровно до потолка
	w.Each("foe", func(fe *core.Entity) {
		if Foe(fe).Species == "bandit" {
			w.Remove(fe.ID)
		}
	})
	for n := 0; n < liveCap; n++ {
		w.Spawn(&FoeC{Species: "bandit", Settlement: se.ID, HP: 45, Max: 45, State: "roam"})
	}

	var target *core.Entity
	for _, r := range residentsOf(w, se.ID) {
		v := core.Vitals(r)
		if target == nil && v != nil && v.Age >= 18 && v.Age < 30 && core.Ident(r) != nil {
			target = r
		}
	}
	if target == nil {
		t.Fatal("нет подходящего взрослого жителя")
	}
	id := target.ID
	core.Home(target).Status = "none"
	pu := core.Purse(target)
	pu.Coins = 0
	pu.Debts = map[string]float64{"торг": desperatePovertyDebt + 5}

	for day := int64(1); day <= 250; day++ {
		clock := core.Clock{TotalHours: day * core.HoursPerDay}
		LifeSystem{}.Hour(w, clock, hub)
	}
	if w.Get(id) == nil {
		t.Fatal("должник исчез из мира, хотя сцена была полна бандитов — живой потолок не удержан")
	}
	alive := 0
	w.Each("foe", func(fe *core.Entity) {
		if f := Foe(fe); f.Species == "bandit" && f.Settlement == se.ID && f.State != "dead" {
			alive++
		}
	})
	if alive > liveCap {
		t.Fatalf("бандитов в сцене больше живого потолка: %d > %d", alive, liveCap)
	}
}
