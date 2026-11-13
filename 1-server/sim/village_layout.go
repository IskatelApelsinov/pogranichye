package sim

import (
	"math"

	"pogranichye/core"
)

// Раскладка деревни: точки интереса (двери зданий, колодец, лес, река, поля) и куда идут жители.
// Координаты берутся из world_geometry.go (POINamed) — единая версионированная истина для сервера и
// клиента. Жители идут к ДОСТУПНЫМ точкам (не в стены) и обходят здания (ResolveMove).
//
// LayoutVersion держим равным GeometryVersion — любая правка координат бьётся об обе стороны.

const LayoutVersion = GeometryVersion

// poiByKind — точка POI стартовой раскладки (для тестов и инструментов стартовой сцены).
func poiByKind(kind string) (x, y float64) { return StarterLayout().POI(kind) }

// targetFor — куда идёт житель: цель (из mind.Decide — ИИ не дублируем) и ремесло дают POI, а сон и
// отдых — порог СВОЕЙ избы. Раньше «отдых» вёл всех к одному дому старосты, а ночью все стояли у
// колодца — деревня сбивалась в кучу. Второй результат — ключ точки для раздачи мест вокруг неё.
func targetFor(l *Layout, e *core.Entity, role, goal, act string) (x, y float64) {
	home := func() (float64, float64) { return l.HomeOf(uint64(e.ID)) }
	if act == "sleep" {
		return home()
	}
	switch goal {
	case "eat", "socialize":
		return l.POI("tavern")
	case "rest", "seek_home":
		return home()
	case "wander":
		// гуляют кто где: у колодца, у лавки, у своего двора, у околицы
		switch (uint64(e.ID) * 2654435761 >> 11) % 4 {
		case 0:
			return l.POI("well")
		case 1:
			return l.POI("market")
		case 2:
			return home()
		}
		return l.POI("granary")
	case "leave":
		return l.POI("woods")
	case "work":
		switch role {
		case "smith":
			return l.POI("smithy")
		case "farmer", "herder", "herbalist":
			return l.POI("garden")
		case "fisher":
			return l.POI("river")
		case "hunter", "woodcutter":
			return l.POI("woods")
		case "miner":
			return l.POI("mine")
		case "tavernkeeper", "cook":
			return l.POI("tavern")
		case "weaver":
			return l.POI("granary")
		}
	}
	return l.POI("well")
}

// targetForGoal — прежняя форма для стартовой сцены без жителя (тесты).
func targetForGoal(role, goal string) (x, y float64) {
	l := StarterLayout()
	switch goal {
	case "rest", "seek_home":
		return l.POI("hearth")
	case "wander":
		return l.POI("well")
	}
	return targetFor(l, &core.Entity{}, role, goal, "")
}

// GoTo — ДИСКРЕТНАЯ серверно-авторитетная навигация для менеджмент-клиента (тонкий web/mobile):
// выбор локации тапом ставит тело игрока в точку этой локации. Это не фальшивое перемещение — сервер
// остаётся владельцем позиции, просто навигация дискретная (в отличие от непрерывной ходьбы Unity).
// Возвращает (ок, причина). Локации — ключи POINamed (tavern/smithy/river/woods/...).
func GoTo(w *core.World, e *core.Entity, location string) (bool, string) {
	l := layoutFor(w, e)
	p, ok := l.POIs[location]
	if !ok {
		return false, "нет такой локации"
	}
	sp := EnsureScenePos(w, e)
	sp.X, sp.Y = l.Free(p[0], p[1]) // точка POI проходима; если вдруг нет — ближайшее свободное место
	return true, ""
}

// Параметры парковочных слотов у POI (см. parkingSlot).
const (
	slotsPerRing = 12  // слотов на кольцо
	slotRing0    = 2.0 // радиус первого кольца, м
	slotRingStep = 1.5 // прирост радиуса на каждое следующее кольцо, м
)

// parkingSlot — детерминированное НЕПЕРЕСЕКАЮЩЕЕСЯ место (слот) вокруг POI по РАНГУ жителя в очереди к
// этой точке. В отличие от idOffset (смещение по хэшу id, где два жителя могли попасть почти в одну
// точку), слоты разложены по концентрическим кольцам, по slotsPerRing на кольцо, с фазовым сдвигом
// нечётных колец — жители у одного POI занимают РАЗНЫЕ места и не слипаются (P0-навигация).
func parkingSlot(rank int) (dx, dy float64) {
	if rank < 0 {
		rank = 0
	}
	ring := rank / slotsPerRing
	idx := rank % slotsPerRing
	rad := slotRing0 + float64(ring)*slotRingStep
	phase := 0.0
	if ring%2 == 1 { // смещаем нечётные кольца на полслота, чтобы слоты не ложились на один луч
		phase = math.Pi / slotsPerRing
	}
	ang := float64(idx)*(2*math.Pi/slotsPerRing) + phase
	return rad * math.Cos(ang), rad * math.Sin(ang)
}

// idOffset — устойчивое личное смещение жителя вокруг целевой точки (чтобы НПС не слипались в POI).
// Оставлено как резерв; штатное распределение у POI теперь делает parkingSlot по рангу.
func idOffset(id uint64) (dx, dy float64) {
	h := id*2654435761 + 1013904223
	ang := float64(h%360) * math.Pi / 180
	rad := 1.0 + float64((h/360)%250)/100.0 // 1..3.5 м
	return rad * math.Cos(ang), rad * math.Sin(ang)
}

// SpawnSpot — начальное место жителя в стартовой раскладке (см. Layout.SpawnSpot).
func SpawnSpot(id uint64) (x, y float64) { return StarterLayout().SpawnSpot(id) }
