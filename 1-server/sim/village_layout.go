package sim

import "math"

// Раскладка стартовой деревни: точки интереса (POI) в локальной сцене (метры от центра площадки,
// +X восток, +Y север; та же система, что в контракте движения v1.1). Это ВЕРСИОНИРОВАННАЯ общая
// геометрия: сервер водит по ней НПС, Unity-презентация должна класть здания на те же координаты.
// Первая вертикаль — открытая площадка без графа препятствий: НПС идёт к POI своей текущей цели по
// прямой. Граф проходимых рёбер/коллизии зданий/ручья — следующий слой (см. comms к 0031).
//
// Любое изменение координат/состава POI — подъём LayoutVersion и согласование с клиентом.

const LayoutVersion = 1

// POI — точка интереса в сцене.
type POI struct {
	Name string
	Kind string // tavern|smithy|garden|river|woods|mine|well|hearth
	X, Y float64
}

// StarterLayout — POI «Починка». Держим в радиусе ~18 м, чтобы житель с личным смещением вокруг точки
// всё равно оставался внутри площадки R=30.
var StarterLayout = []POI{
	{"Корчма", "tavern", -12, 8},
	{"Кузница", "smithy", 10, 11},
	{"Огород", "garden", 14, -8},
	{"Река", "river", -6, -16},
	{"Опушка", "woods", -16, -6},
	{"Рудник", "mine", 18, 2},
	{"Колодец", "well", 0, -1},
	{"Очаг", "hearth", 4, 16},
}

// poiByKind возвращает координаты POI данного вида (или колодец как разумный центр по умолчанию).
func poiByKind(kind string) (x, y float64) {
	for _, p := range StarterLayout {
		if p.Kind == kind {
			return p.X, p.Y
		}
	}
	return 0, -1 // колодец/центр
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
			return poiByKind("hearth")
		default:
			return poiByKind("well")
		}
	default:
		return poiByKind("well")
	}
}

// idOffset — устойчивое личное смещение жителя вокруг целевой точки (чтобы НПС не слипались в POI).
// Детерминировано по id: один и тот же житель всегда встаёт на «своё» место у точки.
func idOffset(id uint64) (dx, dy float64) {
	h := id*2654435761 + 1013904223
	ang := float64(h%360) * math.Pi / 180
	rad := 0.6 + float64((h/360)%140)/100.0 // 0.6..2.0 м
	return rad * math.Cos(ang), rad * math.Sin(ang)
}

// spawnSpot — детерминированное НАЧАЛЬНОЕ место жителя: разложены кольцом по id, чтобы деревня
// выглядела населённой с первого кадра (а не все в центре). Не RNG — стабильно между запусками.
func SpawnSpot(id uint64) (x, y float64) {
	h := id*40503 + 12345
	ang := float64(h%360) * math.Pi / 180
	rad := 4 + float64((h/360)%120)/10.0 // 4..16 м от центра
	return rad * math.Cos(ang), rad * math.Sin(ang)
}
