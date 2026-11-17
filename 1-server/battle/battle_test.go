package battle

import (
	"testing"

	"pogranichye/dice"
)

// Умелый берёт мелкого зверя с добычей; неумелый против медведя чаще получает рану.
func TestResolveWinAndWound(t *testing.T) {
	s := dice.NewStream(dice.Seed64("b", "1"))
	win := 0
	for i := 0; i < 50; i++ {
		o := Resolve(60, 0.9, "hare", &BodyC{Wounds: map[string]float64{}}, s)
		if o.Win {
			win++
		}
	}
	if win < 40 {
		t.Fatalf("умелый должен почти всегда брать зайца: %d/50", win)
	}
	// неумелый против медведя — часто ранят
	wounded := 0
	for i := 0; i < 50; i++ {
		b := &BodyC{Wounds: map[string]float64{}}
		Resolve(5, 0.2, "bear", b, s)
		if b.Severity() > 0 || b.Bleeding > 0 {
			wounded++
		}
	}
	if wounded < 20 {
		t.Fatalf("неумелый против медведя должен часто получать раны: %d/50", wounded)
	}
}

// Раны заживают со временем, со снадобьем/знахарем — быстрее.
func TestWoundHeal(t *testing.T) {
	s := dice.NewStream(42)
	slow := &BodyC{Wounds: map[string]float64{"arm": 12}}
	fast := &BodyC{Wounds: map[string]float64{"arm": 12}}
	for i := 0; i < 5; i++ {
		slow.Heal(false, 0, s)  // без покоя и медицины
		fast.Heal(true, 1.0, s) // в покое и с медициной
	}
	if fast.Wounds["arm"] >= slow.Wounds["arm"] {
		t.Fatalf("с медициной и покоем должно заживать быстрее: быстро %.1f vs медленно %.1f",
			fast.Wounds["arm"], slow.Wounds["arm"])
	}
}

// Открытая рана без чистых трав гноится; травы/лекарь не дают заразиться и чистят гной.
func TestInfectionNeedsHerbs(t *testing.T) {
	// без медицины глубокая рана за много дней хоть раз да загноится
	festered := false
	for trial := 0; trial < 40 && !festered; trial++ {
		b := &BodyC{Wounds: map[string]float64{"leg": 18}}
		s := dice.NewStream(uint64(trial + 1))
		for i := 0; i < 8; i++ {
			b.Heal(false, 0, s)
			if b.Infection > 0 {
				festered = true
				break
			}
		}
	}
	if !festered {
		t.Fatal("открытая рана без трав должна иногда загнаиваться")
	}
	// с травами (medicine высокое) та же рана не гноится
	b := &BodyC{Wounds: map[string]float64{"leg": 18}}
	s := dice.NewStream(99)
	for i := 0; i < 8; i++ {
		b.Heal(true, 1.0, s)
	}
	if b.Infection > 0 {
		t.Fatalf("с чистыми травами рана не должна гноиться: %.1f", b.Infection)
	}
	// уже начавшееся заражение травы вычищают
	inf := &BodyC{Infection: 6}
	for i := 0; i < 5; i++ {
		inf.Heal(true, 1.0, dice.NewStream(5))
	}
	if inf.Infection >= 6 {
		t.Fatalf("лечение должно уменьшать заражение: %.1f", inf.Infection)
	}
}

// Кровь без лечения точит жизнь; медицина её останавливает.
func TestBleedingNeedsCare(t *testing.T) {
	s := dice.NewStream(7)
	untreated := &BodyC{Bleeding: 10}
	totalLost := 0.0
	for i := 0; i < 5; i++ {
		totalLost += untreated.Heal(false, 0, s)
	}
	if totalLost <= 0 {
		t.Fatal("кровопотеря без лечения должна отнимать здоровье")
	}
	treated := &BodyC{Bleeding: 10}
	treated.Heal(true, 1.0, dice.NewStream(7))
	if treated.Bleeding == 10 {
		t.Fatal("лечение должно унимать кровь")
	}
}
