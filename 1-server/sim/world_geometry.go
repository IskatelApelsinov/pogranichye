package sim

// Геометрия сцены мира — ВЕРСИОНИРОВАННАЯ общая истина (сервер владеет коллизиями; Unity кладёт
// визуал на те же координаты). Метры, начало — центр деревни, +X восток, +Y север.
//
// Масштаб (v1.3): весь мир 300×300 м (коорд. −150..150), ядро деревни 100×100 м (−50..50).
// Внутри деревни — здания (прямоугольники-препятствия) и колодец; с юга течёт река (непроходима,
// кроме моста); на западе лес (проходим, но там рубят), поля открыты. Жители и игрок НЕ ходят сквозь
// стены и реку — см. Blocked/ResolveMove.

const (
	GeometryVersion = 2   // bump при любой правке координат/состава (согласуется с клиентом)
	WorldHalf       = 150.0 // полумир: мир 300×300 м
	VillageHalf     = 50.0  // полуядро: деревня 100×100 м
)

// Rect — осевой прямоугольник-препятствие (здание). Координаты углов в метрах.
type Rect struct {
	Name string  `json:"name"`
	Kind string  `json:"kind"`
	X0   float64 `json:"x0"`
	Y0   float64 `json:"y0"`
	X1   float64 `json:"x1"`
	Y1   float64 `json:"y1"`
}

func (r Rect) contains(x, y float64) bool { return x >= r.X0 && x <= r.X1 && y >= r.Y0 && y <= r.Y1 }
func (r Rect) cx() float64                { return (r.X0 + r.X1) / 2 }
func (r Rect) cy() float64                { return (r.Y0 + r.Y1) / 2 }

// Buildings — здания деревни (препятствия). Между ними — улицы/площадь.
var Buildings = []Rect{
	{"Корчма", "tavern", -40, 14, -26, 26},
	{"Кузница", "smithy", 22, 16, 36, 28},
	{"Амбар", "granary", -10, 30, 6, 42},
	{"Изба старосты", "house", 26, -30, 40, -18},
	{"Изба", "house", -40, -30, -28, -20},
	{"Изба", "house", -14, -34, -2, -24},
	{"Изба", "house", 6, -36, 18, -26},
	{"Лавка", "market", -6, 6, 8, 16},
}

// River — река-полоса с юга (непроходима), в ней мост-проём (проходим).
var (
	riverY0, riverY1 = -90.0, -78.0 // полоса реки по Y
	bridgeX0, bridgeX1 = -6.0, 6.0  // проём моста по X
)

// POINamed — именованная точка интереса (двери зданий, колодец, лес, река, поля) для целей НПС и
// действий игрока. Это ДОСТУПНЫЕ точки (в проходимом месте, у входа), не центры зданий.
var POINamed = map[string][2]float64{
	"tavern":  {-33, 12},  // у входа в корчму (южная стена)
	"smithy":  {29, 14},   // перед кузницей
	"granary": {-2, 28},   // перед амбаром
	"market":  {1, 4},     // у лавки/площади
	"well":    {0, -4},    // колодец (центр площади)
	"hearth":  {33, -32},  // у избы старосты (очаг/дом по умолчанию)
	"garden":  {70, -20},  // поля на востоке (открыто)
	"river":   {0, -74},   // берег у моста (рыбалка)
	"woods":   {-95, 10},  // опушка на западе (рубка)
	"mine":    {90, 40},   // рудник на северо-востоке
}

// Blocked — непроходима ли точка: за краем мира, внутри здания или в реке (кроме моста).
func Blocked(x, y float64) bool {
	if x < -WorldHalf || x > WorldHalf || y < -WorldHalf || y > WorldHalf {
		return true
	}
	for _, b := range Buildings {
		if b.contains(x, y) {
			return true
		}
	}
	if y >= riverY0 && y <= riverY1 {
		if x < bridgeX0 || x > bridgeX1 { // мост — единственный проход
			return true
		}
	}
	return false
}

// ResolveMove — коллизия скольжением: из (ox,oy) в желаемое (nx,ny). Если цель свободна — туда; иначе
// пробуем двигаться только по X, затем только по Y (скользим вдоль стены); совсем некуда — остаёмся.
func ResolveMove(ox, oy, nx, ny float64) (float64, float64) {
	if !Blocked(nx, ny) {
		return nx, ny
	}
	if !Blocked(nx, oy) {
		return nx, oy
	}
	if !Blocked(ox, ny) {
		return ox, ny
	}
	return ox, oy
}

// LayoutJSON — геометрия мира для клиента (эндпойнт /api/layout): всё в метрах, оси как в кадре.
func LayoutJSON() map[string]any {
	pois := map[string]map[string]float64{}
	for k, p := range POINamed {
		pois[k] = map[string]float64{"x": p[0], "y": p[1]}
	}
	return map[string]any{
		"version":      GeometryVersion,
		"world_half":   WorldHalf,   // мир [-world_half, world_half]^2
		"village_half": VillageHalf, // ядро деревни
		"buildings":    Buildings,   // прямоугольники-препятствия
		"river":        map[string]float64{"y0": riverY0, "y1": riverY1, "bridge_x0": bridgeX0, "bridge_x1": bridgeX1},
		"pois":         pois,
	}
}

// inWoods / nearRiver — зоны действий (рубка у леса, рыбалка у реки).
func inWoods(x, y float64) bool { _ = y; return x <= -70 }           // западный лес
func nearRiver(x, y float64) bool {
	return y >= riverY1-6 && y <= riverY1+6 // у северного берега реки (где стоят, не в воде)
}
