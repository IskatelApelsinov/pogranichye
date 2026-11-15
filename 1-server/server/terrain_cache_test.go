package server

import (
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Кэш Terrain (comms 0208, пункт B): s.terrain() не должен пересобирать рельеф на каждый вызов —
// только когда меняется число поселений (единственное, от чего зависят якоря/рельеф, см.
// sim.NewTerrain). Без этого P подключённых клиентов платят за O(settlements) на каждый свой
// stateJSON/HTTP-запрос за один игровой тик, хотя рельеф между запросами не менялся.
func TestServerTerrainCacheHitsAcrossCalls(t *testing.T) {
	s := NewStarter("terrain-cache", "", []byte("x"), "")
	s.mu.Lock()
	a := s.terrain()
	b := s.terrain()
	s.mu.Unlock()
	if a != b {
		t.Fatal("s.terrain() должен отдавать один и тот же Terrain, пока число поселений не менялось")
	}
}

// Новое село (переселенцы живого мира, frontier) обязано инвалидировать кэш — иначе клиент, зашедший
// в только что открытое село, получил бы координаты/высоту по старому Terrain без его якоря.
func TestServerTerrainCacheInvalidatesOnNewSettlement(t *testing.T) {
	s := NewStarter("terrain-cache-new", "", []byte("x"), "")
	s.mu.Lock()
	before := s.terrain()
	beforeN := s.world.CountKind("settlement")
	region := s.world.Spawn(&core.RegionC{Name: "Новый край", Biome: "plain"})
	fresh := s.world.Spawn(&core.SettlementC{Name: "Новосёл", X: 900, Y: 900, Region: region.ID})
	after := s.terrain()
	s.mu.Unlock()

	if after == before {
		t.Fatal("появление нового села должно пересобрать Terrain (новый якорь иначе не учтён)")
	}
	if got := s.world.CountKind("settlement"); got != beforeN+1 {
		t.Fatalf("ожидалось %d поселений после спавна, получили %d", beforeN+1, got)
	}
	// Height под новым якорем должен быть детерминирован и совпадать при повторном вызове того же
	// (уже невалидированного под новое село) Terrain — сам факт плоской площадки под каждым селом
	// проверяет sim-пакет, здесь важна только корректная инвалидация кэша на новом сервере.
	e, n := sim.Anchor(s.world, core.Settle(fresh))
	h1, h2 := after.Height(e, n), after.Height(e, n)
	if h1 != h2 {
		t.Fatalf("Height под новым якорем должен быть детерминирован: %v != %v", h1, h2)
	}

	// повторный вызов без новых сёл снова отдаёт закэшированный Terrain
	s.mu.Lock()
	again := s.terrain()
	s.mu.Unlock()
	if again != after {
		t.Fatal("после инвалидации кэш должен снова держаться, пока сёла не меняются")
	}
}

// Один и тот же Terrain из кэша должен давать одинаковую высоту для одной и той же точки через все
// пути, которые его используют (stateJSON напрямую и sim.AnchorViewUsing/WorldManifestUsing/
// TerrainChunkUsing, вызываемые с тем же кэшем) — иначе клиент увидит разные высоты одной точки
// в зависимости от того, какой запрос её вычислил.
func TestServerTerrainCacheConsistentAcrossViews(t *testing.T) {
	s := NewStarter("terrain-cache-consistency", "", []byte("x"), "")
	h := s.Handler()

	world := getJSON(t, h, "/api/world")
	sets := world["settlements"].([]any)
	if len(sets) == 0 {
		t.Fatal("/api/world вернул поселения без записей")
	}
	st := sets[0].(map[string]any)
	layout := getJSON(t, h, st["layout"].(string))
	anchor := layout["anchor"].(map[string]any)
	if anchor["u"] != st["u"] {
		t.Fatalf("высота якоря в /api/layout (%v) расходится с /api/world (%v) — Terrain кэш даёт разные значения разным запросам", anchor["u"], st["u"])
	}
}
