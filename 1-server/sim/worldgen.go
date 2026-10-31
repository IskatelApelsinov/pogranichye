package sim

import (
	"fmt"

	"pogranichye/core"
	"pogranichye/dice"
)

var villageNames = []string{"Белогорье", "Отрадное", "Соловьёво", "Усть-Ворон", "Лещина",
	"Яворово", "Тихий Брод", "Межгорье", "Краснолесье", "Студёное"}

// Generate создаёт живой мир-затравку: несколько поселений с жителями. Разброс свойств —
// через кости (возраст, пол, тяги, стартовый достаток), чтобы НЕ было клонов и идеальной
// ровности. Возвращает мир и хаб костей (детерминированно от seed).
func Generate(seed string, settlements int) (*core.World, *dice.Hub) {
	w := core.NewWorld(seed)
	hub := dice.NewHub(seed)
	gen := hub.Stream("worldgen")

	if settlements > len(villageNames) {
		settlements = len(villageNames)
	}
	for i := 0; i < settlements; i++ {
		s := w.Spawn(&core.SettlementC{
			Name:    villageNames[i],
			Founded: 0,
			Tier:    "village",
			Store:   map[string]float64{"food": gen.Uniform(40, 120), "wood": gen.Uniform(20, 60)},
			Tavern:  true,
		})
		// население деревни — не ровное: кость задаёт 6..14 душ
		pop := gen.Between(6, 14)
		var residents []*core.Entity
		for j := 0; j < pop; j++ {
			p := spawnPerson(w, gen, s.ID)
			residents = append(residents, p)
		}
		// корчмарь — случайный взрослый житель
		if len(residents) > 0 {
			k := residents[gen.Intn(len(residents))]
			core.Ident(k).Role = "tavernkeeper"
			core.Settle(s).Keeper = k.ID
		}
	}
	return w, hub
}

// spawnPerson создаёт жителя с согласованной личностью и разбросанными свойствами.
func spawnPerson(w *core.World, s *dice.Stream, settlement core.ID) *core.Entity {
	sex := randomSex(s)
	ident := makeIdentity(s, sex, "", "")
	// возраст — колоколом вокруг 30, обрезанный в [1,70]; дети и старики существуют
	age := s.Normal(30, 15)
	if age < 1 {
		age = 1
	}
	if age > 70 {
		age = 70
	}
	home := "none"
	if s.Chance(0.4) {
		home = "owned" // часть жителей уже обжилась
	}
	e := w.Spawn(
		ident,
		&core.VitalsC{
			Age:    round1(age),
			Health: s.Uniform(80, 100),
			Hunger: s.Uniform(0, 40),
			Energy: s.Uniform(50, 100),
			Morale: s.Uniform(45, 80),
		},
		&core.DrivesC{
			Ambition: s.Uniform(0, 60),
			Social:   s.Uniform(0, 50),
			Dwelling: s.Uniform(0, 60),
		},
		&core.MindC{Goal: "", LastAct: "—"},
		&core.SkillsC{Skills: map[string]float64{
			ident.Role: s.Uniform(2, 25), // стартовое умение по ремеслу, скромное
		}},
		&core.PurseC{Coins: round1(s.Uniform(2, 40)), Debts: map[string]float64{}},
		&core.HomeC{Status: home, Comfort: 0, Settlement: settlement},
		&core.PlaceC{Settlement: settlement},
	)
	return e
}

func round1(f float64) float64 { return float64(int(f*10+0.5)) / 10 }

// Describe — короткая человекочитаемая сводка мира (для cmd/диагностики).
func Describe(w *core.World) string {
	people, settles := 0, 0
	w.Each("vitals", func(e *core.Entity) {
		if core.Ident(e) != nil {
			people++
		}
	})
	w.Each("settlement", func(e *core.Entity) { settles++ })
	return fmt.Sprintf("мир: %d поселений, %d живых сущностей", settles, people)
}
