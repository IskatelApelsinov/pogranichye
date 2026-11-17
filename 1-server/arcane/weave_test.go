package arcane

import (
	"testing"

	"pogranichye/dice"
)

// Наполнение маной идёт из поля: в месте силы быстро, в мёртвой зоне — еле-еле, и не выше предела.
func TestRefillFromField(t *testing.T) {
	rich := &MageC{}
	poor := &MageC{}
	rich.Refill(10, 1.8) // место силы
	poor.Refill(10, 0.4) // мёртвая зона
	if rich.Mana <= poor.Mana {
		t.Fatalf("в месте силы маны набирается больше: сила=%.2f зона=%.2f", rich.Mana, poor.Mana)
	}
	// не переполняется выше предела
	full := &MageC{Mana: MaxMana(10)}
	if d := full.Refill(10, 2.0); d != 0 || full.Mana > MaxMana(10)+1e-9 {
		t.Fatalf("запас не должен переливаться через предел: draw=%.2f mana=%.2f", d, full.Mana)
	}
}

// Не хватает маны — плетение рвётся, и запас не уходит в минус.
func TestWeaveManaGate(t *testing.T) {
	m := &MageC{Mana: 2}
	ok, _ := m.Weave(6, 10, dice.NewStream(1))
	if ok {
		t.Fatal("без достаточной маны плетение не должно удаваться")
	}
	if m.Mana < 0 {
		t.Fatalf("мана не уходит в минус: %.2f", m.Mana)
	}
}

// Навык помогает удерживать плетение: у опытного мага та же сложность срывается реже.
func TestWeaveSkillHelps(t *testing.T) {
	novWins, admWins := 0, 0
	for i := 0; i < 400; i++ {
		nov := &MageC{Mana: 100}
		adm := &MageC{Mana: 100}
		if ok, _ := nov.Weave(6, 3, dice.NewStream(uint64(i))); ok {
			novWins++
		}
		if ok, _ := adm.Weave(6, 60, dice.NewStream(uint64(i))); ok {
			admWins++
		}
	}
	if admWins <= novWins {
		t.Fatalf("опытный маг должен срывать реже: новичок=%d архимаг=%d", novWins, admWins)
	}
}

// Сильный перебор над ёмкостью ума даёт тяжёлый откат при срыве.
func TestOverreachBackfires(t *testing.T) {
	hurtSeen := false
	for i := 0; i < 50 && !hurtSeen; i++ {
		m := &MageC{Mana: 100}
		if ok, back := m.Weave(40, 2, dice.NewStream(uint64(i))); !ok && back > 5 {
			hurtSeen = true
		}
	}
	if !hurtSeen {
		t.Fatal("плетение сильно выше ёмкости ума должно давать тяжёлый откат при срыве")
	}
}
