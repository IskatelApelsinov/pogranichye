// Пакет memory — память мира (блок 1.2). Главный принцип, выстраданный разбором
// прототипа: НИЧЕГО НЕ ТЕРЯЕМ. В старом мире холодные деревни схлопывались до числа
// жителей, а архив лежал в /tmp (пропадал при перезапуске) — оттого на 5910-й день
// вместо людей появлялись безликие idler'ы. Здесь снимок сохраняет КАЖДУЮ сущность и
// КАЖДЫЙ компонент плюс состояние костей и часы — и возвращает мир точь-в-точь.
//
// Масштаб (сотни деревень) решается позже выгрузкой ХОЛОДНЫХ регионов на диск — но
// всегда полными сущностями, а не числом. Этот пакет даёт лоссless-ядро такой схемы.
package memory

import (
	"encoding/json"

	"pogranichye/core"
	"pogranichye/dice"
)

// Snapshot — полный снимок мира на диск/в БД. Версионируем схему, чтобы будущие миграции
// были явными (разработчику видно, какой формат он читает).
type Snapshot struct {
	Version int            `json:"version"`
	Clock   core.Clock     `json:"clock"`
	World   core.WorldWire `json:"world"`
}

const schemaVersion = 1

// Save сворачивает мир, часы и кости в один снимок без потерь.
func Save(w *core.World, clock core.Clock, hub *dice.Hub) ([]byte, error) {
	hubRaw, err := json.Marshal(hub.Snapshot())
	if err != nil {
		return nil, err
	}
	wire, err := w.Dump(hubRaw) // снимок костей кладётся в Extra рядом с сущностями
	if err != nil {
		return nil, err
	}
	return json.MarshalIndent(Snapshot{Version: schemaVersion, Clock: clock, World: wire}, "", " ")
}

// Load восстанавливает мир, часы и кости из снимка. Возвращает всё, что нужно, чтобы
// продолжить прокрутку ровно с того места (та же последовательность бросков).
func Load(data []byte) (*core.World, core.Clock, *dice.Hub, error) {
	var snap Snapshot
	if err := json.Unmarshal(data, &snap); err != nil {
		return nil, core.Clock{}, nil, err
	}
	w := core.LoadWorld(snap.World)
	hub := dice.NewHub(snap.World.Seed)
	if len(snap.World.Extra) > 0 {
		var hs dice.HubState
		if err := json.Unmarshal(snap.World.Extra, &hs); err == nil {
			hub = dice.LoadHub(hs)
		}
	}
	return w, snap.Clock, hub, nil
}
