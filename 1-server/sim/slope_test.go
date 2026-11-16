package sim

import (
	"math"
	"testing"

	"pogranichye/core"
)

// Склон в движении (v1.23): горы не стена — в гору медленнее, круча не пускает, в селе ровно.

func TestSlopeMultShape(t *testing.T) {
	if SlopeMult(0) != 1 {
		t.Fatal("по ровному — полный шаг")
	}
	prev := 1.0
	for g := 0.05; g <= SlopeCliff; g += 0.05 {
		m := SlopeMult(g)
		if m >= prev || m <= 0 {
			t.Fatalf("чем круче в гору, тем медленнее: g=%.2f m=%.3f", g, m)
		}
		prev = m
	}
	if SlopeMult(SlopeCliff+0.01) != 0 {
		t.Fatal("круча непроходима в лоб")
	}
	if m := SlopeMult(-0.1); m <= 1 || m > SlopeEasyBoost {
		t.Fatalf("пологий спуск чуть быстрее: %v", m)
	}
	if m := SlopeMult(-0.9); m >= 1 || m <= 0 {
		t.Fatalf("крутой спуск осторожнее, но проходим: %v", m)
	}
	// непрерывность на стыке пологого и крутого спуска
	if d := math.Abs(SlopeMult(-SlopeEasyDown+1e-9) - SlopeMult(-SlopeEasyDown-1e-9)); d > 1e-6 {
		t.Fatalf("скачок множителя на стыке спусков: %v", d)
	}
}

func TestTerrainMultFlatInVillageAndSlowerUphill(t *testing.T) {
	w, e, se := starterPlayer(t)
	SetSeamless(w, true)
	ter := NewTerrain(w)
	p := EnsureScenePos(w, e)
	// в селе — всегда ровно, куда бы ни шёл
	for _, at := range [][2]float64{{0, 0}, {100, -60}, {-149, 149}} {
		p.X, p.Y = at[0], at[1]
		for _, d := range [][2]float64{{1, 0}, {0, 1}, {-1, -1}} {
			if m := TerrainMult(w, ter, e, d[0], d[1]); m != 1 {
				t.Fatalf("в селе склон не действует: в (%v,%v) множитель %v", at[0], at[1], m)
			}
		}
	}
	// на склоне гребня: вверх медленнее, чем вниз, и это ровно формула от уклона земли
	ax, ay := Anchor(w, core.Settle(se))
	found := false
	for r := ter.rimR - 6000; r < ter.rimR && !found; r += 250 {
		k := r / math.Hypot(ax, ay)
		wx, wy := ax*k, ay*k // точка на луче от центра котла через село, у гребня
		out := [2]float64{ax, ay}
		g := ter.Grade(wx, wy, out[0], out[1])
		if g < 0.08 || g > SlopeCliff {
			continue
		}
		found = true
		p.X, p.Y = wx-ax, wy-ay
		up := TerrainMult(w, ter, e, out[0], out[1])
		down := TerrainMult(w, ter, e, -out[0], -out[1])
		if up != SlopeMult(g) || up >= 1 || down <= up {
			t.Fatalf("на склоне (уклон %.2f): вверх %.3f, вниз %.3f", g, up, down)
		}
		// шаг сервера действительно короче в гору
		l := LayoutFor(w, e)
		x0, y0 := p.X, p.Y
		p.Terr = up
		ApplyMove(l, p, out[0], out[1], false, 0, 0.25, 1)
		upStep := math.Hypot(p.X-x0, p.Y-y0)
		p.X, p.Y, p.Terr = x0, y0, 0
		ApplyMove(l, p, out[0], out[1], false, 0, 0.25, 2)
		flatStep := math.Hypot(p.X-x0, p.Y-y0)
		if upStep >= flatStep || math.Abs(upStep-flatStep*up) > 1e-9 {
			t.Fatalf("шаг в гору %.4f при ровном %.4f и множителе %.3f", upStep, flatStep, up)
		}
		// круча: ввод принят (seq), но тело не сдвинулось
		p.X, p.Y, p.Terr = x0, y0, -1
		ApplyMove(l, p, out[0], out[1], false, 0, 0.25, 3)
		if p.X != x0 || p.Y != y0 || p.Seq != 3 {
			t.Fatal("в кручу шага нет, но ввод учтён")
		}
	}
	if !found {
		t.Fatal("у гребня котла должен найтись проходимый склон")
	}
	mv := MovementView()["slope"].(map[string]any)
	if mv["up_k"] != SlopeUpK || mv["cliff"] != SlopeCliff {
		t.Fatal("клиенту публикуются те же параметры, по которым считает сервер")
	}
}
