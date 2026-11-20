package sim

import (
	"strconv"
	"testing"

	"pogranichye/battle"
	"pogranichye/core"
	"pogranichye/econ"
)

func actor(w *core.World, x, y, energy float64) *core.Entity {
	e := w.Spawn(
		&core.IdentC{Name: "Игрок", Role: "farmer", Sex: "м"},
		&core.VitalsC{Energy: energy, Health: 100, Morale: 50},
	)
	w.Attach(e, &ScenePosC{X: x, Y: y, Stam: 1})
	return e
}

// Рубка у леса даёт бревно и тратит силы; вдали от леса — отказ.
func TestChopWood(t *testing.T) {
	w := core.NewWorld("t")
	wx, wy := poiByKind("woods")
	e := actor(w, wx, wy, 100)
	ok, why := ChopWood(w, e, "c1")
	if !ok {
		t.Fatalf("у леса рубка должна пройти: %s", why)
	}
	if econ.Stash(e).Goods[econ.GoodWood] != chopYield {
		t.Fatal("бревно не попало в закрома")
	}
	if core.Vitals(e).Energy != 100-chopEnergy {
		t.Fatal("силы не потрачены")
	}
	// повтор того же reqid — без второго бревна и повторной траты сил.
	ChopWood(w, e, "c1")
	if econ.Stash(e).Goods[econ.GoodWood] != chopYield {
		t.Fatal("повтор reqid не должен давать второе бревно")
	}
	if core.Vitals(e).Energy != 100-chopEnergy {
		t.Fatal("повтор reqid не должен повторно тратить силы")
	}
	// Неудачный запрос не фиксируется как выполненный: тот же reqid можно
	// безопасно повторить после того, как игрок подойдёт к лесу.
	far := actor(w, 60, 60, 100)
	if ok, _ := ChopWood(w, far, "retry-chop"); ok {
		t.Fatal("вдали от леса рубить нельзя")
	}
	pos := ScenePos(far)
	pos.X, pos.Y = wx, wy
	if ok, why := ChopWood(w, far, "retry-chop"); !ok {
		t.Fatalf("после подхода тот же reqid должен сработать: %s", why)
	}
	if econ.Stash(far).Goods[econ.GoodWood] != chopYield {
		t.Fatal("повтор после отказа должен дать ровно одно бревно")
	}
}

// Рыбалка у реки даёт рыбу; не у воды — отказ. Без settlement (как здесь, через actor()) запас
// рыбы не завести (ensureFishery(nil)==nil) — это намеренно проверяет старое поведение без места;
// реальная завязка на запас конкретного поселения — в TestFishRequiresRealFishStock ниже.
func TestFish(t *testing.T) {
	w := core.NewWorld("t")
	rx, ry := poiByKind("river")
	e := actor(w, rx, ry, 100)
	if ok, why := FishCatch(w, e, "f1"); ok {
		t.Fatalf("без поселения (ensureFishery==nil) лов не должен проходить: %s", why)
	}
	far := actor(w, 60, 60, 100)
	if ok, _ := FishCatch(w, far, "retry-fish"); ok {
		t.Fatal("не у воды ловить нельзя")
	}
}

// Лов у реки стартового села: настоящий запас рыбы заводится при первом же лове (comms 0302/0304),
// тратится при успехе, повтор reqid идемпотентен, вдали от воды — честный отказ без расхода сил.
func TestFishRequiresRealFishStock(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "river")
	// Запас может уже существовать (фоновая экономика при генерации мира — comms 0304 п.1: один
	// источник правды, не параллельный счётчик) или завестись только сейчас — читаем "до" с любым
	// исходом, не полагаясь на конкретную величину.
	if fn := fisheryNode(w, se.ID); fn != nil && fn.Stock < fishYield {
		fn.Stock = fn.Cap // settlement уже рыбный, но случайно истощён — поднимем для этого теста
	}
	before := 0.0
	if fn := fisheryNode(w, se.ID); fn != nil {
		before = fn.Stock
	} else {
		before = riverFishCap // ещё не заведён — лов его заведёт именно с этим стартовым запасом
	}
	if ok, why := FishCatch(w, e, "f1"); !ok {
		t.Fatalf("у реки с рыбой лов должен пройти: %s", why)
	}
	if econ.Stash(e).Goods[econ.GoodFish] != fishYield {
		t.Fatal("рыба не попала в закрома")
	}
	fn := fisheryNode(w, se.ID)
	if fn == nil {
		t.Fatal("успешный лов должен завести/вернуть реальный запас рыбы места")
	}
	if fn.Stock != before-fishYield {
		t.Fatalf("запас должен убыть ровно на улов: ожидали %v, получили %v", before-fishYield, fn.Stock)
	}
	// Повтор успешного reqid не добавляет рыбу, не тратит силы и не убавляет запас второй раз.
	FishCatch(w, e, "f1")
	if econ.Stash(e).Goods[econ.GoodFish] != fishYield {
		t.Fatal("повтор reqid не должен давать вторую рыбу")
	}
	if core.Vitals(e).Energy != 100-fishEnergy {
		t.Fatal("повтор reqid не должен повторно тратить силы")
	}
	if fn.Stock != before-fishYield {
		t.Fatal("повтор reqid не должен повторно списывать запас")
	}
	// Отказ из-за расстояния не тратит силы, не блокирует повтор того же reqid после подхода.
	energyBefore := core.Vitals(e).Energy
	p := ScenePos(e)
	p.X, p.Y = 60, 60
	if ok, _ := FishCatch(w, e, "retry-fish"); ok {
		t.Fatal("не у воды ловить нельзя")
	}
	if core.Vitals(e).Energy != energyBefore {
		t.Fatal("отказ из-за расстояния не должен тратить силы")
	}
	GoTo(w, e, "river")
	if ok, why := FishCatch(w, e, "retry-fish"); !ok {
		t.Fatalf("после подхода тот же reqid должен сработать: %s", why)
	}
	if fn.Stock != before-2*fishYield {
		t.Fatal("повтор после отказа должен списать запас ровно один раз")
	}
}

// Истощённый запас (река или пруд) честно отказывает без выдачи рыбы и без расхода сил, даже
// если игрок стоит у самой воды. Запас — ЕДИНЫЙ (какой уже есть или только что лениво заведённый),
// а не второй параллельный узел для того же поселения.
func TestFishRefusesWhenStockExhausted(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "river")
	fn := ensureFisheryFor(w, se, layoutFor(w, e))
	if fn == nil {
		t.Fatal("у стартового села с рекой запас должен заводиться")
	}
	fn.Stock = 0
	energyBefore := core.Vitals(e).Energy
	if ok, why := FishCatch(w, e, "empty-river"); ok {
		t.Fatal("истощённый запас не должен давать улов")
	} else if why == "" {
		t.Fatal("отказ должен называть причину")
	}
	if core.Vitals(e).Energy != energyBefore {
		t.Fatal("отказ из-за пустого запаса не должен тратить силы")
	}
	if s := econ.Stash(e); s != nil && s.Goods[econ.GoodFish] != 0 {
		t.Fatal("истощённый запас не должен выдавать рыбу")
	}
}

// synthWaterLayout — минимальная раскладка только с одной точкой воды "river" (POI-имя точки воды
// в раскладке не меняется от Land.Water — см. layout_gen.go) для прямого теста fishCatchAt без
// кеша LayoutOf/процедурной генерации.
func synthWaterLayout(water string, hasRiver bool) *Layout {
	return &Layout{HasRiver: hasRiver, Land: LandStyle{Water: water},
		POIs: map[string][2]float64{"river": {10, 10}, "well": {10, 10}}}
}

// Колодец (land.water=="well") никогда не место для лова — даже если игрок стоит ровно у точки
// воды.
func TestFishRefusesAtWell(t *testing.T) {
	w, e, se := starterPlayer(t)
	l := synthWaterLayout("well", false)
	p := ScenePos(e)
	p.X, p.Y = l.POI("river")
	if ok, why := fishCatchAt(w, e, l, se, "well-try"); ok {
		t.Fatalf("у колодца лов должен быть недоступен, а не %q", why)
	}
}

// bareSettlement — голое поселение БЕЗ фоновой экономики/ресурсов (в отличие от starterPlayer),
// чтобы тест пруда проверял именно fisheryExpectationFor/ensureFisheryFor, а не случайно
// унаследованный от worldgen фоновый рыбный узел (comms 0304: один источник правды).
func bareSettlement(w *core.World, name string) *core.Entity {
	return w.Spawn(&core.SettlementC{Name: name, Store: map[string]float64{}})
}

// Пруд с рыбой даёт улов тем же путём, что и река (общий ensureFisheryFor/fisheryExpectationFor),
// а пруд, которому по честной монете рыба не выпала, отказывает так же, как истощённый — без
// различимой для игрока разницы между "рыбы никогда не было" и "запас кончился" (comms 0304 п.6).
func TestFishAtPond(t *testing.T) {
	l := synthWaterLayout("pond", false)

	rx, ry := l.POI("river")
	w := core.NewWorld("pond-fish")
	se := bareSettlement(w, "seed0")
	e := actor(w, rx, ry, 100)
	w.Attach(e, &core.PlaceC{Settlement: se.ID})
	// находим пруд, которому по детерминированной монете честно выпала рыба (монета стабильна по
	// имени поселения — подбираем имя, пока не найдём такой расклад).
	found := false
	for i := 0; i < 64; i++ {
		core.Settle(se).Name = "ПрудС" + strconv.Itoa(i)
		if _, has := fisheryExpectationFor(w, se, l); has {
			found = true
			break
		}
	}
	if !found {
		t.Fatal("не нашлось пруда с рыбой за 64 попытки — проверь pondFishChance")
	}
	if ok, why := fishCatchAt(w, e, l, se, "pond-fish"); !ok {
		t.Fatalf("у пруда с рыбой лов должен пройти: %s", why)
	}
	if econ.Stash(e).Goods[econ.GoodFish] != fishYield {
		t.Fatal("рыба из пруда не попала в закрома")
	}
	// (place.site.can_fish/FishAvailable читает РЕАЛЬНУЮ, кешированную LayoutOf поселения, а не
	// синтетическую l этого теста — честно проверено отдельно, на реальном стартовом селе, в
	// TestFishRequiresRealFishStock; здесь это было бы расхождением тестового стенда, не кода.)

	// теперь пруд, которому рыба честно не выпала.
	w2 := core.NewWorld("pond-no-fish")
	se2 := bareSettlement(w2, "seed0")
	e2 := actor(w2, rx, ry, 100)
	w2.Attach(e2, &core.PlaceC{Settlement: se2.ID})
	found = false
	for i := 0; i < 64; i++ {
		core.Settle(se2).Name = "ПрудБ" + strconv.Itoa(i)
		if _, has := fisheryExpectationFor(w2, se2, l); !has {
			found = true
			break
		}
	}
	if !found {
		t.Fatal("не нашлось пруда без рыбы за 64 попытки — проверь pondFishChance")
	}
	if ok, why := fishCatchAt(w2, e2, l, se2, "pond-no-fish"); ok {
		t.Fatalf("у пруда без рыбы лов должен быть недоступен, а не %q", why)
	}
	if s := econ.Stash(e2); s != nil && s.Goods[econ.GoodFish] != 0 {
		t.Fatal("пруд без рыбы не должен выдавать рыбу")
	}
}

// FishAvailable (place.site.can_fish) честно отражает реальный запас на настоящем, не
// синтетическом поселении: доступен сразу после первого успешного лова, пропадает при истощении.
func TestFishAvailableReflectsRealStock(t *testing.T) {
	w, e, se := starterPlayer(t)
	GoTo(w, e, "river")
	if !FishAvailable(w, se) {
		t.Fatal("у реки со свежим запасом can_fish должен быть true")
	}
	fn := ensureFisheryFor(w, se, layoutFor(w, e))
	fn.Stock = 0
	if FishAvailable(w, se) {
		t.Fatal("при истощённом запасе can_fish должен быть false")
	}
}

// waterKind — чистая функция типа воды: река (в т.ч. стартовая раскладка без Land.Water), пруд,
// колодец/отсутствие воды различаются верно.
func TestWaterKind(t *testing.T) {
	cases := []struct {
		name string
		l    *Layout
		want string
	}{
		{"starter HasRiver без Land.Water", &Layout{HasRiver: true}, "river"},
		{"Land.Water river", &Layout{Land: LandStyle{Water: "river"}}, "river"},
		{"pond", &Layout{Land: LandStyle{Water: "pond"}}, "pond"},
		{"well", &Layout{Land: LandStyle{Water: "well"}}, ""},
		{"ничего", &Layout{}, ""},
	}
	for _, c := range cases {
		if got := waterKind(c.l); got != c.want {
			t.Errorf("%s: waterKind=%q, ожидали %q", c.name, got, c.want)
		}
	}
}

// Магический разряд тратит ману и прореживает опасного зверя; без маны — отказ.
func TestCastSpark(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Store: map[string]float64{}})
	f := &battle.FaunaC{Pop: map[string]float64{"wolf": 5}, Cap: map[string]float64{}, Base: map[string]float64{}}
	w.Attach(se, f)
	e := actor(w, 0, 0, 100)
	w.Attach(e, &core.PlaceC{Settlement: se.ID})
	EnsurePlayer(w, e)
	before := f.Pop["wolf"]
	if ok, why := CastSpark(w, e, "s1"); !ok {
		t.Fatalf("разряд должен пройти при полной мане: %s", why)
	}
	if Player(e).Mana != sparkManaMax-sparkCost {
		t.Fatalf("мана не списана: %.0f", Player(e).Mana)
	}
	if f.Pop["wolf"] >= before {
		t.Fatal("разряд должен прореживать опасного зверя")
	}
	// высадим ману — откажет
	Player(e).Mana = 1
	if ok, _ := CastSpark(w, e, "s2"); ok {
		t.Fatal("без маны разряда нет")
	}
}

// Мана восстанавливается в реальном времени.
func TestManaRegen(t *testing.T) {
	w := core.NewWorld("t")
	e := actor(w, 0, 0, 100)
	p := EnsurePlayer(w, e)
	p.Mana = 0
	RegenMana(w, 2.0)
	if p.Mana <= 0 || p.Mana > sparkManaMax {
		t.Fatalf("мана должна восстанавливаться: %.1f", p.Mana)
	}
}

// Охота не должна выбирать deer: этот вид пока отсутствует в серверной модели FaunaC.
func TestHuntIgnoresUnsupportedDeerPopulation(t *testing.T) {
	w := core.NewWorld("t")
	se := w.Spawn(&core.SettlementC{Name: "С", Store: map[string]float64{}})
	f := &battle.FaunaC{
		Pop:  map[string]float64{"deer": 10},
		Cap:  map[string]float64{"deer": 10},
		Base: map[string]float64{"deer": 10},
	}
	w.Attach(se, f)
	e := actor(w, 0, 0, 100)
	w.Attach(e, &core.PlaceC{Settlement: se.ID})
	EnsurePlayer(w, e)

	ok, why := Hunt(w, e, "hunt-deer-only")
	if ok {
		t.Fatal("охота не должна выдавать добычу из неподдерживаемого вида deer")
	}
	if why != "подходящего зверя рядом нет" {
		t.Fatalf("ожидался честный отказ при отсутствии поддерживаемой дичи, получено: %q", why)
	}
	if stash := econ.Stash(e); stash != nil &&
		(stash.Goods["meat"] != 0 || stash.Goods["hide"] != 0) {
		t.Fatal("неподдерживаемый deer не должен давать добычу")
	}
}

// TestGatherBrushwood — село без настоящего леса (feature "woods" не стоит) всё равно даёт рощицу
// (copse); хворост из неё — отдельный честный товар, не бревно (comms 0215/0218, v1.23). У колодца
// (вне рощицы) — отказ; полноценная рубка (ChopWood) там же по-прежнему недоступна.
func TestGatherBrushwood(t *testing.T) {
	w := core.NewWorld("t-brush")
	// первое село всегда получает StarterLayout (rank==0, см. LayoutOf) — нужно второе
	w.Spawn(&core.SettlementC{Name: "Первое", Tier: "village"})
	se := w.Spawn(&core.SettlementC{Name: "Рощица", Tier: "village"})
	w.Attach(se, &SiteC{Terrain: "plain", Features: map[string]bool{}}) // явно БЕЗ "woods"

	l := LayoutOf(w, se.ID)
	var copseZ Zone
	found := false
	for _, z := range l.Zones {
		if z.Kind == "copse" {
			copseZ, found = z, true
			break
		}
		if z.Kind == "forest" {
			t.Fatal("без feature woods раскладка не должна получить полноценный forest")
		}
	}
	if !found {
		t.Fatal("без feature woods раскладка обязана содержать copse")
	}
	cx, cy := (copseZ.X0+copseZ.X1)/2, (copseZ.Y0+copseZ.Y1)/2

	e := actor(w, cx, cy, 100)
	w.Attach(e, &core.PlaceC{Settlement: se.ID})

	if ok, why := ChopWood(w, e, "chop-in-copse"); ok {
		t.Fatalf("полноценной рубки в роще без feature woods быть не должно: %v/%s", ok, why)
	}
	ok, why := GatherBrushwood(w, e, "b1")
	if !ok {
		t.Fatalf("сбор хвороста в роще должен пройти: %s", why)
	}
	if econ.Stash(e).Goods[econ.GoodBrushwood] != brushYield {
		t.Fatal("хворост не попал в закрома")
	}
	if econ.Stash(e).Goods[econ.GoodWood] != 0 {
		t.Fatal("хворост не должен засчитываться как бревно (GoodWood)")
	}
	if core.Vitals(e).Energy != 100-brushEnergy {
		t.Fatal("силы на сбор хвороста не потрачены верно")
	}
	// повтор того же reqid — идемпотентно
	GatherBrushwood(w, e, "b1")
	if econ.Stash(e).Goods[econ.GoodBrushwood] != brushYield {
		t.Fatal("повтор reqid не должен давать второй хворост")
	}
	// у колодца (вне рощицы) — отказ
	far := actor(w, 0, 0, 100)
	w.Attach(far, &core.PlaceC{Settlement: se.ID})
	if ok, _ := GatherBrushwood(w, far, "b2"); ok {
		t.Fatal("вдали от рощицы хворост не собрать")
	}
}
