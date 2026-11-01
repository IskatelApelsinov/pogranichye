package mind

import (
	"testing"

	"pogranichye/core"
	"pogranichye/dice"
)

// строим жителя с нужными свойствами для проверки рассуждения.
func who(role string, hunger, energy, social, ambition float64, home string, coins float64) *core.Entity {
	e := &core.Entity{ID: 1, Comps: map[string]core.Component{}}
	e.Add(&core.IdentC{Name: "Т", Role: role, Sex: "м"})
	e.Add(&core.VitalsC{Age: 30, Health: 100, Hunger: hunger, Energy: energy, Morale: 60})
	e.Add(&core.DrivesC{Ambition: ambition, Social: social})
	e.Add(&core.HomeC{Status: home})
	e.Add(&core.PurseC{Coins: coins})
	return e
}

func dec(e *core.Entity, hour int, foodDays float64) string {
	g, _ := Decide(e, Context{Hour: hour, FoodDays: foodDays}, dice.NewStream(1))
	return g
}

// Выживание перебивает всё: остро голодный идёт есть, даже если деревне грозит голод.
func TestSurvivalFirst(t *testing.T) {
	e := who("farmer", 80, 90, 90, 90, "none", 0)
	if g := dec(e, 12, 3); g != GoalEat {
		t.Fatalf("остро голодный должен есть, а решил %q", g)
	}
}

// Ночью (не голодая остро) — отдыхать.
func TestNightRest(t *testing.T) {
	e := who("farmer", 10, 90, 0, 0, "owned", 0)
	if g := dec(e, 23, 30); g != GoalRest {
		t.Fatalf("ночью должен отдыхать, а решил %q", g)
	}
}

// Безопасность: деревне грозит голод → и землепашец, и горожанин берутся за РАБОТУ
// (первый добывает еду, второй — доход). Бережливость «растить хлеб» тут и рождается.
func TestFoodInsecurityDrivesWork(t *testing.T) {
	farmer := who("farmer", 10, 80, 0, 0, "owned", 0)
	if g := dec(farmer, 12, 5); g != GoalWork {
		t.Fatalf("землепашец при угрозе голода должен работать, а решил %q", g)
	}
	smith := who("smith", 10, 80, 0, 0, "owned", 0)
	if g := dec(smith, 12, 5); g != GoalWork {
		t.Fatalf("ремесленник при угрозе голода должен работать (ради дохода), а решил %q", g)
	}
}

// Сыт и в безопасности, но бездомен и есть монеты → обзавестись домом (ярус безопасности/кров).
func TestSeekHomeWhenSecure(t *testing.T) {
	e := who("farmer", 10, 80, 0, 0, "none", 50)
	if g := dec(e, 12, 30); g != GoalSeekHome {
		t.Fatalf("сытый бездомный с деньгами должен искать дом, а решил %q", g)
	}
}

// Базовые нужды закрыты, тянет к людям → в таверну (ярус принадлежности).
func TestBelongingSocialize(t *testing.T) {
	e := who("farmer", 10, 80, 80, 0, "owned", 0)
	if g := dec(e, 12, 30); g != GoalSocialize {
		t.Fatalf("при высокой тяге к людям должен идти общаться, а решил %q", g)
	}
}

// Всё закрыто, большое честолюбие и нет своего угла → уйти за лучшей долей (ярус признания).
func TestEsteemLeave(t *testing.T) {
	e := who("farmer", 10, 80, 0, 90, "none", 0) // монет нет → дом не ищет, честолюбие гонит прочь
	if g := dec(e, 12, 30); g != GoalLeave {
		t.Fatalf("честолюбивый бездомный без денег должен уйти, а решил %q", g)
	}
}
