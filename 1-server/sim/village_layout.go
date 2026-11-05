package sim

import "math"

// Раскладка деревни: точки интереса (двери зданий, колодец, лес, река, поля) и куда идут жители.
// Координаты берутся из world_geometry.go (POINamed) — единая версионированная истина для сервера и
// клиента. Жители идут к ДОСТУПНЫМ точкам (не в стены) и обходят здания (ResolveMove).
//
// LayoutVersion держим равным GeometryVersion — любая правка координат бьётся об обе стороны.

const LayoutVersion = GeometryVersion

// poiByKind — доступная точка POI данного вида (или колодец/площадь по умолчанию).
func poiByKind(kind string) (x, y float64) {
	if p, ok := POINamed[kind]; ok {
		return p[0], p[1]
	}
	return POINamed["well"][0], POINamed["well"][1]
}

// targetForGoal переводит ТЕКУЩУЮ цель жителя (из mind.Decide — не дублируем ИИ) и его ремесло в POI,
// к которому он идёт. Цель решает «зачем», ремесло уточняет «где работает».
func targetForGoal(role, goal string) (x, y float64) {
	switch goal {
	case "eat", "socialize":
		return poiByKind("tavern")
	case "rest", "seek_home":
		return poiByKind("hearth")
	case "wander":
		return poiByKind("well")
	case "leave":
		return poiByKind("woods")
	case "work":
		switch role {
		case "smith":
			return poiByKind("smithy")
		case "farmer", "herder", "herbalist":
			return poiByKind("garden")
		case "fisher":
			return poiByKind("river")
		case "hunter", "woodcutter":
			return poiByKind("woods")
		case "miner":
			return poiByKind("mine")
		case "tavernkeeper", "cook":
			return poiByKind("tavern")
		case "weaver":
			return poiByKind("granary")
		default:
			return poiByKind("well")
		}
	default:
		return poiByKind("well")
	}
}

// idOffset — устойчивое личное смещение жителя вокруг целевой точки (чтобы НПС не слипались в POI).
func idOffset(id uint64) (dx, dy float64) {
	h := id*2654435761 + 1013904223
	ang := float64(h%360) * math.Pi / 180
	rad := 1.0 + float64((h/360)%250)/100.0 // 1..3.5 м
	return rad * math.Cos(ang), rad * math.Sin(ang)
}

// SpawnSpot — детерминированное НАЧАЛЬНОЕ место жителя: разложены по ядру деревни (в проходимом месте,
// не в стене). Стабильно между запусками (не RNG). Если выпало в здание — чуть толкаем наружу.
func SpawnSpot(id uint64) (x, y float64) {
	h := id*40503 + 12345
	ang := float64(h%360) * math.Pi / 180
	rad := 8 + float64((h/360)%380)/10.0 // 8..46 м от центра — по всей деревне
	x, y = rad*math.Cos(ang), rad*math.Sin(ang)
	for i := 0; i < 8 && Blocked(x, y); i++ { // не в стене/реке
		rad -= 5
		if rad < 2 {
			x, y = 0, -4 // к колодцу как крайний случай
			break
		}
		x, y = rad*math.Cos(ang), rad*math.Sin(ang)
	}
	return x, y
}
