package sim

import (
	"testing"

	"pogranichye/core"
)

func code(role, goal string, season core.Season, hour int, r localResources) string {
	return chooseActivity(role, goal, season, hour, r).code
}

// Занятие под трудом зависит от профессии и сезона — у каждого своя причина.
func TestWorkActivityByRoleAndSeason(t *testing.T) {
	full := localResources{gameNearby: true, oreInStore: true, mineAvailable: true, fiber: true, canSow: true, fieldRipe: true}
	cases := []struct {
		role   string
		season core.Season
		want   string
	}{
		{"farmer", core.Spring, "farm_sow"},
		{"farmer", core.Autumn, "farm_reap"},
		{"farmer", core.Winter, "farm_mend"},   // зимой поле спит
		{"fisher", core.Spring, "fisher_mend"}, // нерест — лов закрыт
		{"fisher", core.Summer, "fish_river"},
		{"herbalist", core.Winter, "brew_remedy"}, // травы под снегом не рвут
		{"herbalist", core.Summer, "forage_herbs"},
		{"herder", core.Winter, "herd_barn"},
		{"herder", core.Summer, "herd_graze"},
	}
	for _, c := range cases {
		if got := code(c.role, "work", c.season, 12, full); got != c.want {
			t.Errorf("%s/%v: хотели %s, получили %s", c.role, c.season, c.want, got)
		}
	}
}

// Занятие требует РЕСУРСА: нет дичи/руды/сырья/маны — у действия нет причины состояться.
func TestWorkActivityNeedsResources(t *testing.T) {
	with := localResources{gameNearby: true, oreInStore: true, mineAvailable: true, fiber: true}
	none := localResources{}
	pairs := []struct {
		role      string
		withCode  string
		withoutIt string
	}{
		{"hunter", "hunt_wild", "hunter_empty"},
		{"smith", "smith_forge", "smith_cold"},
		{"weaver", "weave", "weaver_wait"},
		{"miner", "mine_ore", "mine_spent"},
	}
	for _, p := range pairs {
		if got := code(p.role, "work", core.Summer, 12, with); got != p.withCode {
			t.Errorf("%s с ресурсом: хотели %s, получили %s", p.role, p.withCode, got)
		}
		if got := code(p.role, "work", core.Summer, 12, none); got != p.withoutIt {
			t.Errorf("%s без ресурса: хотели %s, получили %s", p.role, p.withoutIt, got)
		}
	}
}

// Честность полей: сев/жатва признают реальное состояние пашни, а не утверждают ложно.
func TestFarmerFieldHonesty(t *testing.T) {
	if code("farmer", "work", core.Spring, 12, localResources{canSow: true}) != "farm_sow" {
		t.Error("есть несеяная пашня и зерно — вышел на сев")
	}
	if code("farmer", "work", core.Autumn, 12, localResources{fieldRipe: true}) != "farm_reap" {
		t.Error("есть вызревшее поле — вышел на жатву")
	}
	if code("farmer", "work", core.Autumn, 12, localResources{fieldRipe: false}) != "farm_noreap" {
		t.Error("урожай не дошёл — подпись не должна утверждать жатву")
	}
	if code("farmer", "work", core.Spring, 12, localResources{canSow: false}) != "farm_nosow" {
		t.Error("нет зерна на сев — подпись должна признавать невозможность, а не утверждать сев")
	}
}

// Занятие под целью зависит от времени суток и доступного (корчма с питьём).
func TestActivityByGoalAndTime(t *testing.T) {
	if code("farmer", "rest", core.Summer, 2, localResources{}) != "sleep" {
		t.Error("ночью отдых должен быть сном")
	}
	if code("farmer", "rest", core.Summer, 13, localResources{}) != "rest_idle" {
		t.Error("днём отдых — не сон")
	}
	if code("cook", "socialize", core.Summer, 19, localResources{tavernDrink: true}) != "drink_tavern" {
		t.Error("при питье в корчме идут в корчму")
	}
	if code("cook", "socialize", core.Summer, 19, localResources{}) != "visit_neighbors" {
		t.Error("без питья в корчме идут к соседям")
	}
	if code("cook", "eat", core.Summer, 12, localResources{}) != "meal" {
		t.Error("цель eat → трапеза")
	}
}

// Переход: у одного НПС занятие МЕНЯЕТСЯ со сменой часа и сезона (различимые переходы).
func TestActivityTransitions(t *testing.T) {
	r := localResources{gameNearby: true}
	day := code("hunter", "work", core.Summer, 12, r)  // днём на промысле
	night := code("hunter", "rest", core.Summer, 1, r) // ночью спит
	if day == night {
		t.Fatal("день и ночь должны давать разные занятия")
	}
	sow := code("farmer", "work", core.Spring, 12, r)
	reap := code("farmer", "work", core.Autumn, 12, r)
	if sow == reap {
		t.Fatal("весной и осенью землепашец занят разным (сев против жатвы)")
	}
	// выбился зверь — охотник переходит с добычи на пустые руки (та же цель, другой исход)
	gone := code("hunter", "work", core.Summer, 12, localResources{gameNearby: false})
	if gone == day {
		t.Fatal("без дичи занятие охотника должно отличаться от удачного промысла")
	}
}
