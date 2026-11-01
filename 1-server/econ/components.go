// Пакет econ — экономика на законе сохранения: НИЧЕГО не берётся из ниоткуда (болезнь
// прототипа, где склад раздувался до миллионов). Всё добро приходит из конечных природных
// узлов (пашня, рыбное место, шахта, лес, пастбище) и перерабатывается цепочками ремёсел.
// Опора — реальное средневековье: сев даёт ~1:3..1:6 зерна (надо резервировать семена),
// трёхполье, нерест (нельзя ловить рыбу в икромёт), шахты истощаются, десятина 10%.
package econ

import "pogranichye/core"

func init() {
	core.Register("resnode", func() core.Component { return &ResourceNodeC{} })
	core.Register("realm", func() core.Component { return &RealmC{Territory: []core.ID{}} })
}

// Виды узлов и товаров — строками, чтобы клиент и инструменты читали без магии.
const (
	NodeField   = "field"   // пашня: сеют весной, жнут осенью; нельзя собрать больше, чем выросло
	NodeGarden  = "garden"  // огород: овощи, зреют за лето, тянет землепашец; разнообразит еду
	NodeFishery = "fishery" // рыбное место: запас, нерест весной (лов закрыт), перелов убивает
	NodeMine    = "mine"    // шахта: конечная руда, НЕ возобновляется — истощается
	NodeForest  = "forest"  // лес: дрова/дичь, медленно возобновляется
	NodePasture = "pasture" // пастбище/угодья: дичь, возобновляется
	NodeHerd    = "herd"    // стадо: даёт возобновляемое (молоко/шерсть/яйца+перо) и убой (мясо/кожа)
	NodeFlax    = "flax"    // лён: сеют/жнут как поле, но на волокно (→ холст у ткача)
	NodeHerbs   = "herbs"   // травяные луга: знахарь собирает травы (→ снадобье)
	NodeApiary  = "apiary"  // пасека: знахарь-бортник берёт мёд (→ медовуха у корчмаря)

	GoodWheat = "wheat" // зерно: и на хлеб, и на сев, и каша в голод — один товар, без «священного»
	GoodFlour = "flour"
	GoodBread = "bread"
	GoodFish  = "fish"
	GoodGame  = "game"
	GoodVeg   = "veg" // овощи с огорода — еда (разнообразие сверх хлеба)
	GoodHay   = "hay" // сено — корм скоту на зиму (летом косят, зимой кормят; нет — падёж)
	GoodOre   = "ore"
	GoodMetal = "metal"
	GoodTools = "tools"
	GoodWood  = "wood"
	// животноводство: возобновляемое с живого стада
	GoodMilk    = "milk"    // молоко → сыр
	GoodCheese  = "cheese"  // сыр (еда, хранится дольше молока)
	GoodWool    = "wool"    // шерсть → сукно
	GoodCloth   = "cloth"   // сукно (ткач)
	GoodEgg     = "egg"     // яйца (еда)
	GoodFeather = "feather" // перо (перина/оперение стрел)
	// убой стада
	GoodMeat    = "meat"    // мясо (еда)
	GoodHide    = "hide"    // шкура → кожа
	GoodLeather = "leather" // кожа (ткач/кожевник)
	// промыслы и ремёсла
	GoodFlax   = "flax"   // лён-волокно → холст
	GoodLinen  = "linen"  // холст (ткач)
	GoodHerb   = "herb"   // травы → снадобье
	GoodRemedy = "remedy" // снадобье знахаря — лечит хворь (см. болезни)
	GoodHoney  = "honey"  // мёд → медовуха
	GoodMead   = "mead"   // медовуха (корчма, поднимает дух)
)

// Виды стад и их возобновляемый продукт (убой у всех даёт мясо+шкуру).
const (
	HerdCattle  = "cattle"  // скот → молоко
	HerdSheep   = "sheep"   // овцы → шерсть
	HerdPoultry = "poultry" // птица → яйца (+перо)
)

// herdProduct — что даёт живое стадо (возобновляемо, без убоя).
func herdProduct(kind string) string {
	switch kind {
	case HerdCattle:
		return GoodMilk
	case HerdSheep:
		return GoodWool
	case HerdPoultry:
		return GoodEgg
	}
	return ""
}

// ResourceNodeC — конечный природный источник у поселения. Запас реально кончается и
// восстанавливается лишь природой (regen), а не трудом. Труд лишь ПЕРЕНОСИТ запас в склад.
type ResourceNodeC struct {
	Node       string  `json:"node"`
	Good       string  `json:"good"`
	Stock      float64 `json:"stock"`      // сколько сейчас стоит «на корню» (рыбы в реке, руды в жиле)
	Cap        float64 `json:"cap"`        // природный предел запаса
	Regen      float64 `json:"regen"`      // естественное восстановление в день (0 у шахты)
	Settlement core.ID `json:"settlement"` // чьё это угодье

	// поля пашни/огорода (Node==field|garden): сезонный цикл
	Sown     bool    `json:"sown"`      // засеяно в этом году
	SownSeed float64 `json:"sown_seed"` // сколько зерна ушло в землю (от этого урожай)
	Ripe     float64 `json:"ripe"`      // спелость 0..1, растёт за лето
	Fallow   bool    `json:"fallow"`    // под паром в этом году (трёхполье) — не сеют, земля отдыхает

	// стадо (Node==herd): Stock = голов скота, Cap = предел выпаса, Regen = приплод/день
	HerdKind string `json:"herd_kind"` // cattle|sheep|poultry — определяет возобновляемый продукт
}

func (c *ResourceNodeC) Kind() string { return "resnode" }

// RealmC — государство/держава: собирает десятину с подвластных поселений в казну.
// Знает лишь СВОИ земли и казну — не лезет в чужую механику (расширяемость).
type RealmC struct {
	Name      string    `json:"name"`
	Capital   core.ID   `json:"capital"`
	Treasury  float64   `json:"treasury"`
	TaxRate   float64   `json:"tax_rate"` // доля от произведённого (десятина ≈ 0.1)
	Territory []core.ID `json:"territory"`
	WarWith   core.ID   `json:"war_with"` // с кем держава воюет сейчас (0 — мир)
	WarDays   int       `json:"war_days"` // сколько дней войне осталось идти
}

func (c *RealmC) Kind() string { return "realm" }

// Node / Realm — типизированные помощники.
func Node(e *core.Entity) *ResourceNodeC { c, _ := e.Get("resnode").(*ResourceNodeC); return c }
func Realm(e *core.Entity) *RealmC       { c, _ := e.Get("realm").(*RealmC); return c }

// RoleHarvest говорит, из какого узла и какой товар добывает ремесло (для ApplyLabor).
// Пусто — ремесло ничего не добывает из природы (перерабатывающие: cook/smith/weaver).
func RoleHarvest(role string) (node, good string) {
	switch role {
	case "woodcutter":
		return NodeForest, GoodWood
	case "fisher":
		return NodeFishery, GoodFish
	case "hunter":
		return NodePasture, GoodGame
	case "farmer":
		return NodeField, GoodWheat // пашню тянет фермер (сев/жатва — в FarmingSystem)
	case "miner":
		return NodeMine, GoodOre
	}
	return "", ""
}
