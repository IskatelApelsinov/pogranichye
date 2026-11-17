package sim

import (
	"testing"

	"pogranichye/dice"
)

// Фамилия строится из корня и согласуется по полу; ребёнок наследует РОД (тот же корень) в своей
// форме — даже для процедурных/синтезированных корней.
func TestSurnameInheritanceByRoot(t *testing.T) {
	s := dice.NewStream(1)
	for _, root := range []string{"Волк", "Град", "Сед", "Черольх", "Соболь"} {
		m := surnameFromRoot(root, "м")
		f := surnameFromRoot(root, "ж")
		if m == f {
			// допустимо лишь если суффиксы совпали бы — но в наших семьях они различны
			t.Fatalf("мужская и женская формы должны различаться: %s", m)
		}
		if recoverRoot(m) != root || recoverRoot(f) != root {
			t.Fatalf("корень должен восстанавливаться: %s→%s, %s→%s", m, recoverRoot(m), f, recoverRoot(f))
		}
		// ребёнок-дочь от отца с мужской формой наследует женскую форму ТОГО ЖЕ рода
		if got := inheritedSurname(s, m, "ж"); got != f {
			t.Fatalf("дочь должна носить %s (род %s), а не %s", f, root, got)
		}
		// сын от матери — мужскую форму того же рода
		if got := inheritedSurname(s, f, "м"); got != m {
			t.Fatalf("сын должен носить %s, а не %s", m, got)
		}
	}
}

// Имён хватает без потолка: процедурная сборка даёт много разных личных имён.
func TestGivenNameVariety(t *testing.T) {
	s := dice.NewStream(2)
	seen := map[string]bool{}
	for i := 0; i < 300; i++ {
		seen[givenName(s, "м")] = true
		seen[givenName(s, "ж")] = true
	}
	if len(seen) < 80 {
		t.Fatalf("имён должно быть много разных (без потолка): %d", len(seen))
	}
}

// Держав тоже без потолка: процедурные имена держав разнообразны.
func TestRealmNameVariety(t *testing.T) {
	g := dice.NewStream(3)
	seen := map[string]bool{}
	for i := 0; i < 200; i++ {
		seen[procRealmName(g)] = true
	}
	if len(seen) < 30 {
		t.Fatalf("имён держав должно быть много разных: %d", len(seen))
	}
}
