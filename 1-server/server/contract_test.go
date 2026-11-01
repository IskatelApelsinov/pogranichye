package server

import (
	"encoding/json"
	"os"
	"sort"
	"testing"

	"pogranichye/core"
)

// Контракт-тест: живой кадр состояния должен совпадать ПО СХЕМЕ с общим fixture
// 3-dev/contract/fixtures/state_v1.example.json (его же использует Unity-тест совместимости).
// Если сервер меняет форму кадра, не обновив fixture и контракт, — тест падает. Так код и
// контракт держатся вместе.
func TestStateFrameMatchesContractV1(t *testing.T) {
	s := New("contract-world", 3, "", []byte("x"), "")

	// взять любого полноценного жителя (в поселении) и получить его кадр
	var cid core.ID
	s.world.Each("ident", func(e *core.Entity) {
		if cid != 0 {
			return
		}
		if core.Vitals(e) != nil && core.Mind(e) != nil && core.Home(e) != nil {
			if pl := core.Place(e); pl != nil && pl.Settlement != 0 {
				cid = e.ID
			}
		}
	})
	if cid == 0 {
		t.Fatal("в сгенерированном мире нет жителя в поселении")
	}

	var live map[string]any
	if err := json.Unmarshal([]byte(s.stateJSON(cid)), &live); err != nil {
		t.Fatalf("кадр состояния не JSON: %v", err)
	}

	// версия протокола
	if v, _ := live["v"].(float64); v != 1 {
		t.Fatalf("v должно быть 1, получили %v", live["v"])
	}
	// id персонажа — строка (uint64 строкой, чтобы JS не терял точность)
	me, _ := live["me"].(map[string]any)
	if me == nil {
		t.Fatal("нет me в кадре")
	}
	if idv, ok := me["id"].(string); !ok || idv == "" {
		t.Fatalf("me.id должен быть непустой строкой, получили %T %v", me["id"], me["id"])
	}

	// схема: набор ключей живого кадра совпадает с fixture (верхний уровень, me, сосед)
	var fx map[string]any
	raw, err := os.ReadFile("../../3-dev/contract/fixtures/state_v1.example.json")
	if err != nil {
		t.Fatalf("не прочитать fixture: %v", err)
	}
	if err := json.Unmarshal(raw, &fx); err != nil {
		t.Fatalf("fixture не JSON: %v", err)
	}

	assertSameKeys(t, "верхний уровень", fx, live)
	assertSameKeys(t, "me", fx["me"].(map[string]any), me)

	// place и сосед (у жителя поселения они есть)
	fxPlace := fx["place"].(map[string]any)
	livePlace, _ := live["place"].(map[string]any)
	if livePlace == nil {
		t.Fatal("нет place в живом кадре жителя поселения")
	}
	assertSameKeys(t, "place", fxPlace, livePlace)

	fxN := fxPlace["neighbors"].([]any)[0].(map[string]any)
	liveNs, _ := livePlace["neighbors"].([]any)
	if len(liveNs) > 0 {
		assertSameKeys(t, "neighbor", fxN, liveNs[0].(map[string]any))
	}
}

// assertSameKeys сверяет НАБОРЫ ключей двух объектов (не значения) — это и есть проверка схемы.
func assertSameKeys(t *testing.T, where string, a, b map[string]any) {
	t.Helper()
	ka, kb := keysOf(a), keysOf(b)
	if len(ka) != len(kb) {
		t.Fatalf("%s: расхождение схемы с fixture\n  fixture: %v\n  сервер:  %v", where, ka, kb)
	}
	for i := range ka {
		if ka[i] != kb[i] {
			t.Fatalf("%s: расхождение схемы с fixture\n  fixture: %v\n  сервер:  %v", where, ka, kb)
		}
	}
}

func keysOf(m map[string]any) []string {
	ks := make([]string, 0, len(m))
	for k := range m {
		ks = append(ks, k)
	}
	sort.Strings(ks)
	return ks
}
