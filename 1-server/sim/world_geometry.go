package sim

import "math"

// Геометрия сцены мира — ВЕРСИОНИРОВАННАЯ общая истина (сервер владеет коллизиями; Unity кладёт
// визуал на те же координаты). Метры, начало — центр деревни, +X восток, +Y север.
//
// Масштаб (v1.3): весь мир 300×300 м (коорд. −150..150), ядро деревни 100×100 м (−50..50).
// Внутри деревни — здания (прямоугольники-препятствия) и колодец; с юга течёт река (непроходима,
// кроме моста); на западе лес (проходим, но там рубят), поля открыты. Жители и игрок НЕ ходят сквозь
// стены и реку — см. Blocked/ResolveMove.

const (
	GeometryVersion = 2     // bump при любой правке координат/состава (согласуется с клиентом)
	WorldHalf       = 150.0 // полумир: мир 300×300 м
	VillageHalf     = 50.0  // полуядро: деревня 100×100 м
)

// Rect — осевой прямоугольник-препятствие (здание). Координаты углов в метрах.
//
// Door/BuildingID/Room/InteriorID — аддитивный контракт v1.23 (comms 0217/0218 п.3): дверной проём,
// через который здание становится проходимым внутри (см. Layout.Blocked), вместо того чтобы быть
// сплошным препятствием целиком. Для старых зданий без Door поведение не меняется.
type Rect struct {
	Name       string  `json:"name"`
	Kind       string  `json:"kind"`
	X0         float64 `json:"x0"`
	Y0         float64 `json:"y0"`
	X1         float64 `json:"x1"`
	Y1         float64 `json:"y1"`
	BuildingID string  `json:"building_id,omitempty"`
	Room       string  `json:"room,omitempty"`
	InteriorID string  `json:"interior_id,omitempty"`
	Door       *Door   `json:"door,omitempty"`
}

// Door — дверной проём в стене здания (comms 0217). X,Y — точка на самой стене (не порог снаружи);
// Facing — направление НАРУЖУ из здания, та же конвенция atan2, что у me.pos.facing/neighbors[].pos
// (0=восток,90=север,180=запад,270=юг); Width — полная ширина прохода в метрах.
type Door struct {
	X      float64 `json:"x"`
	Y      float64 `json:"y"`
	Facing float64 `json:"facing"`
	Width  float64 `json:"width"`
}

// tavernDoorWidth — ширина прохода в корчму (comms 0217 пример давал 1.4; берём чуть шире прохода
// игрока с запасом на коллизию).
const tavernDoorWidth = 1.6

// wallThickness — толщина "оболочки" стен здания с дверью: полоса такой ширины внутрь от каждой
// грани прямоугольника остаётся непроходимой стеной (кроме проёма), всё, что глубже — пол интерьера.
const wallThickness = 1.2

// doorOnWall строит Door на самой стене (не на пороге-подходе снаружи), выбирая грань по тому, с
// какой стороны от центра здания лежит точка подхода approachX/approachY, и выставляя Facing как
// направление НАРУЖУ в конвенции me.pos.facing. axisX — проём на южной/северной грани (варьируется
// по X), иначе — на западной/восточной (варьируется по Y). Общая геометрия для ручной стартовой
// раскладки и генератора (layout_gen.go) — обе используют тот же approach-порог в 2 м от стены.
func doorOnWall(r Rect, approachX, approachY float64, axisX bool, width float64) Door {
	if axisX {
		if approachY < r.cy() {
			return Door{X: approachX, Y: r.Y0, Facing: 270, Width: width} // южная стена, наружу — юг
		}
		return Door{X: approachX, Y: r.Y1, Facing: 90, Width: width} // северная стена, наружу — север
	}
	if approachX < r.cx() {
		return Door{X: r.X0, Y: approachY, Facing: 180, Width: width} // западная стена, наружу — запад
	}
	return Door{X: r.X1, Y: approachY, Facing: 0, Width: width} // восточная стена, наружу — восток
}

// blockedAt — непроходима ли точка (x,y), про которую уже известно, что она внутри границ r
// (r.contains(x,y) == true). Без двери — всё здание сплошное (старое поведение, без изменений).
// С дверью — пол интерьера (инсет на wallThickness от каждой грани) проходим, стены непроходимы,
// кроме полосы шириной Door.Width на той грани, где стоит дверь.
func (r *Rect) blockedAt(x, y float64) bool {
	d := r.Door
	if d == nil {
		return true
	}
	t := wallThickness
	if x > r.X0+t && x < r.X1-t && y > r.Y0+t && y < r.Y1-t {
		return false // пол интерьера
	}
	half := d.Width / 2
	switch {
	case y <= r.Y0+t: // полоса южной стены
		return !(math.Abs(d.Y-r.Y0) < 0.5 && math.Abs(x-d.X) <= half)
	case y >= r.Y1-t: // полоса северной стены
		return !(math.Abs(d.Y-r.Y1) < 0.5 && math.Abs(x-d.X) <= half)
	case x <= r.X0+t: // полоса западной стены
		return !(math.Abs(d.X-r.X0) < 0.5 && math.Abs(y-d.Y) <= half)
	case x >= r.X1-t: // полоса восточной стены
		return !(math.Abs(d.X-r.X1) < 0.5 && math.Abs(y-d.Y) <= half)
	}
	return false
}

func (r Rect) contains(x, y float64) bool { return x >= r.X0 && x <= r.X1 && y >= r.Y0 && y <= r.Y1 }
func (r Rect) cx() float64                { return (r.X0 + r.X1) / 2 }
func (r Rect) cy() float64                { return (r.Y0 + r.Y1) / 2 }

// Buildings — здания деревни (препятствия). Между ними — улицы/площадь.
var Buildings = []Rect{
	{Name: "Корчма", Kind: "tavern", X0: -40, Y0: 14, X1: -26, Y1: 26},
	{Name: "Кузница", Kind: "smithy", X0: 22, Y0: 16, X1: 36, Y1: 28},
	{Name: "Амбар", Kind: "granary", X0: -10, Y0: 30, X1: 6, Y1: 42},
	{Name: "Изба старосты", Kind: "house", X0: 26, Y0: -30, X1: 40, Y1: -18},
	{Name: "Изба", Kind: "house", X0: -40, Y0: -30, X1: -28, Y1: -20},
	{Name: "Изба", Kind: "house", X0: -14, Y0: -34, X1: -2, Y1: -24},
	{Name: "Изба", Kind: "house", X0: 6, Y0: -36, X1: 18, Y1: -26},
	{Name: "Лавка", Kind: "market", X0: -6, Y0: 6, X1: 8, Y1: 16},
}

// River — река-полоса с юга (непроходима), в ней мост-проём (проходим).
var (
	riverY0, riverY1   = -90.0, -78.0 // полоса реки по Y
	bridgeX0, bridgeX1 = -6.0, 6.0    // проём моста по X
)

// POINamed — именованная точка интереса (двери зданий, колодец, лес, река, поля) для целей НПС и
// действий игрока. Это ДОСТУПНЫЕ точки (в проходимом месте, у входа), не центры зданий.
var POINamed = map[string][2]float64{
	"tavern":  {-33, 12}, // у входа в корчму (южная стена)
	"smithy":  {29, 14},  // перед кузницей
	"granary": {-2, 28},  // перед амбаром
	"market":  {1, 4},    // у лавки/площади
	"well":    {0, -4},   // колодец (центр площади)
	"hearth":  {33, -32}, // у избы старосты (очаг/дом по умолчанию)
	"garden":  {70, -20}, // поля на востоке (открыто)
	"river":   {0, -74},  // берег у моста (рыбалка)
	"woods":   {-95, 10}, // опушка на западе (рубка)
	"mine":    {90, 40},  // рудник на северо-востоке
}

// Blocked/ResolveMove/LayoutJSON без раскладки — геометрия СТАРТОВОГО села (как до v1.19). Игровой код
// берёт раскладку своего поселения (LayoutOf/layoutFor) и зовёт её методы; эти обёртки оставлены
// для инструментов и тестов стартовой сцены.
func Blocked(x, y float64) bool { return StarterLayout().Blocked(x, y) }

func ResolveMove(ox, oy, nx, ny float64) (float64, float64) {
	return StarterLayout().ResolveMove(ox, oy, nx, ny)
}

// LayoutJSON — геометрия стартового села для клиента.
func LayoutJSON() map[string]any { return StarterLayout().JSON() }
