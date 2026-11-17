package server

import (
	"encoding/json"
	"testing"

	"pogranichye/sim"
)

// comms 0213 (2026-11-14): Unity WorldFrameParser.ValidatePosition реально сверяет на каждом
// полном кадре равенство place.anchor + me.pos == me.wpos с допуском 2 мм (ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md,
// v1.19: "me.wpos = place.anchor + me.pos"). Существующий TestWorldEndpointsAndFrameCoordinates
// (world_api_test.go) проверял эту формулу против СЫРОЙ внутренней позиции (p.X/p.Y, которую тест
// сам выставил), а не против того, что реально уходит по проводу в me.pos — так что он оставался
// зелёным даже когда mePos() округляла x/y до дециметра (round1) вместо миллиметра (round3), и
// Unity-клиент при этом отклонял кадр ("me.wpos не совпадает с place.anchor + me.pos", скрин
// игрока 2026-11-14): расхождение дециметрового округления может доходить до ~0.05-0.07 м — на
// порядок больше допуска. Этот тест читает именно то, что уходит клиенту (JSON stateJSON), а не
// внутреннее состояние — так и ловит баг, который предыдущий тест пропускал.
func TestMePosWireMatchesWposAnchorInvariant(t *testing.T) {
	s := NewStarter("me-pos-wire-precision", "", []byte("x"), "")
	cid, _, _ := s.reserveBody(1)
	e := s.world.Get(cid)
	sim.EnsurePlayer(s.world, e)
	p := sim.EnsureScenePos(s.world, e)
	// намеренно НЕ кратно 0.1 м по обеим осям — координата, кратная шагу дециметрового округления,
	// случайно прошла бы проверку и спрятала бы регресс
	p.X, p.Y, p.Z = -20.567, 31.234, 6

	var f map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &f); err != nil {
		t.Fatal(err)
	}
	me := f["me"].(map[string]any)
	wp := me["wpos"].(map[string]any)
	mp := me["pos"].(map[string]any)
	an := f["place"].(map[string]any)["anchor"].(map[string]any)

	const tolerance = 0.002 // 2 мм — тот же допуск, что в Unity WorldFrameParser (comms 0213)
	wantE := an["e"].(float64) + mp["x"].(float64)
	wantN := an["n"].(float64) + mp["y"].(float64)
	if d := wp["e"].(float64) - wantE; d > tolerance || d < -tolerance {
		t.Fatalf("me.wpos.e (%v) != place.anchor.e+me.pos.x (%v), diff=%v м — клиент отклонит кадр",
			wp["e"], wantE, d)
	}
	if d := wp["n"].(float64) - wantN; d > tolerance || d < -tolerance {
		t.Fatalf("me.wpos.n (%v) != place.anchor.n+me.pos.y (%v), diff=%v м — клиент отклонит кадр",
			wp["n"], wantN, d)
	}
}
