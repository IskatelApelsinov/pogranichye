package sim

import (
	"encoding/json"
	"testing"

	"pogranichye/core"
)

// Описание чанка с корчмой должно оставаться лёгким: это статичный ответ, но его тянут на телефон.
func TestSpacesChunkPayloadIsSmall(t *testing.T) {
	w, _ := GenerateStarter("spaces-size")
	tr := NewTerrain(w)
	var se *core.Entity
	w.Each("settlement", func(e *core.Entity) {
		if se == nil {
			se = e
		}
	})
	ae, an := Anchor(w, core.Settle(se))
	l := LayoutOf(w, se.ID)
	cx, cy, _, _ := l.TavernCounter()
	raw, _ := json.Marshal(SpacesChunk(w, tr, chunkOfM(ae+cx), chunkOfM(an+cy)))
	if len(raw) < 2000 || len(raw) > 40000 {
		t.Fatalf("описание чанка с корчмой: %d байт", len(raw))
	}
	t.Logf("чанк с корчмой: %d байт", len(raw))
}
