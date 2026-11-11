package sim

import (
	"testing"

	"pogranichye/core"
)

func TestEquipReqIDDeduplicatesAndFailedRequestCanRetry(t *testing.T) {
	w := core.NewWorld("equip-reqid")
	e := w.Spawn()
	inv := ensureInv(w, e)

	ok, _ := Equip(w, e, "axe", "equip-1")
	if ok {
		t.Fatal("equipping an item absent from the inventory must fail")
	}

	// A failed attempt must not consume the reqid; the same request can succeed after the item exists.
	inv.Items["axe"] = 1
	ok, reason := Equip(w, e, "axe", "equip-1")
	if !ok || reason != "" || inv.Hand != "axe" {
		t.Fatalf("expected equip retry to succeed: ok=%t reason=%q hand=%q", ok, reason, inv.Hand)
	}

	// Replaying the same reqid with a different payload must not mutate the hand again.
	inv.Items["sword"] = 1
	ok, reason = Equip(w, e, "sword", "equip-1")
	if !ok || reason != "" || inv.Hand != "axe" {
		t.Fatalf("duplicate reqid changed hand: ok=%t reason=%q hand=%q", ok, reason, inv.Hand)
	}
}

func TestWearReqIDDeduplicatesMoraleAndSameItemIsNoOp(t *testing.T) {
	w := core.NewWorld("wear-reqid")
	e := w.Spawn()
	inv := ensureInv(w, e)
	inv.Items["cloak"] = 1
	w.Attach(e, &core.VitalsC{Morale: 40})

	ok, reason := Wear(w, e, "cloak", "wear-1")
	if !ok || reason != "" || inv.Worn["body"] != "cloak" {
		t.Fatalf("expected cloak to be worn: ok=%t reason=%q worn=%q", ok, reason, inv.Worn["body"])
	}
	if got := core.Vitals(e).Morale; got != 42 {
		t.Fatalf("first wear should grant one +2 morale bonus, got %.1f", got)
	}

	// Same reqid is idempotent even if a later payload differs.
	inv.Items["hat"] = 1
	ok, reason = Wear(w, e, "hat", "wear-1")
	if !ok || reason != "" || inv.Worn["body"] != "cloak" || core.Vitals(e).Morale != 42 {
		t.Fatalf("duplicate reqid repeated wear side effect: ok=%t reason=%q worn=%q morale=%.1f",
			ok, reason, inv.Worn["body"], core.Vitals(e).Morale)
	}

	// A fresh reqid for the already-worn item is also a no-op, not a morale exploit.
	ok, reason = Wear(w, e, "cloak", "wear-2")
	if !ok || reason != "" || core.Vitals(e).Morale != 42 {
		t.Fatalf("re-wearing same cloak should not grant another morale bonus: ok=%t reason=%q morale=%.1f",
			ok, reason, core.Vitals(e).Morale)
	}
}
