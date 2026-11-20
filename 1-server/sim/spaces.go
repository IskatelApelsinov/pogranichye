package sim

import (
	"encoding/json"
	"fmt"
	"hash/fnv"
	"math"
	"sort"

	"pogranichye/core"
)

// Пространства с этажами (v1.25, comms 0254/0269, task_ai/022): описания зданий, подвалов, башен и
// пещер для GET /api/spaces?cx=&cz=. Это ОПИСАНИЕ геометрии для показа и звука; права ходить оно не
// даёт — движение по-прежнему решает сервер (сейчас: пол первого яруса корчмы через её дверь).
// Этаж — НЕ слой подгрузки в 64 м: у каждого яруса своя абсолютная высота elevation_m.
//
// Оси: origin/bounds/порталы — мировые метры E/N/U. Точки поверхностей и лестниц — метры от
// space.origin: x — на восток, y — ВВЕРХ, z — на север (как читает клиент). origin — юго-западный
// угол пятна здания на уровне земли.

const (
	spaceDoorH   = 2.1  // высота дверного проёма, м
	spaceSlab    = 0.15 // толщина перекрытия: потолок ниже пола верхнего яруса на столько
	spaceStairW  = 1.1  // ширина лестничного марша
	spaceStepH   = 0.2  // высота ступени
	spaceMaxRun  = 4.2  // длина марша по полу, не больше
	spaceReachM  = 420. // дальше этого от центра чанка село не проверяем (пятно села заведомо меньше)
	towerSide    = 7.0
	towerWall    = 0.8
	towerStoreyH = 3.2

	// Оконный проём (v1.35, письмо Kate 0301): детерминированное окно в стене первого яруса на
	// фасадах без двери. Размеры — от здания, не от координат мира; окна не создают порталов/переходов
	// и не дают прохода (остекление collidable, см. glazing).
	winW       = 1.2 // ширина светового проёма вдоль стены, м
	winSillH   = 0.9 // низ проёма над полом яруса, м
	winHeadH   = 2.0 // верх проёма над полом яруса, м
	winMinPier = 0.6 // минимальный глухой простенок с каждой стороны; короче — стена глухая
)

type spaceBuilder struct {
	id         string
	oe, on, g  float64 // origin: мировые метры
	W, D       float64 // размеры пятна на восток и на север
	facing     int     // куда смотрит стена с дверью: 270 юг, 90 север, 180 запад, 0 восток
	A, B       float64 // те же размеры в своих осях: a — вдоль стены с дверью, b — вглубь от неё
	surfaces   []map[string]any
	lo, hi     [3]float64 // охват точек (x,y,z) от origin
	seen       bool
	nSurf      int
	floors     []map[string]any
	rooms      []map[string]any
	portals    []map[string]any
	stairs     []map[string]any
	extraProps map[string]any
}

func newSpaceBuilder(id string, oe, on, g, W, D float64, facing int) *spaceBuilder {
	sb := &spaceBuilder{id: id, oe: oe, on: on, g: g, W: W, D: D, facing: facing, A: W, B: D}
	if facing == 180 || facing == 0 {
		sb.A, sb.B = D, W
	}
	return sb
}

// en — свои оси (a,b) → смещение на восток/север от origin.
func (sb *spaceBuilder) en(a, b float64) (float64, float64) {
	switch sb.facing {
	case 270:
		return a, b
	case 90:
		return sb.W - a, sb.D - b
	case 180:
		return b, a
	default:
		return sb.W - b, a
	}
}

func (sb *spaceBuilder) pt(a, y, b float64) map[string]float64 {
	e, n := sb.en(a, b)
	v := [3]float64{e, y, n}
	if !sb.seen {
		sb.lo, sb.hi, sb.seen = v, v, true
	}
	for i := range v {
		sb.lo[i], sb.hi[i] = math.Min(sb.lo[i], v[i]), math.Max(sb.hi[i], v[i])
	}
	return map[string]float64{"x": r3(e), "y": r3(y), "z": r3(n)}
}

func (sb *spaceBuilder) fid(name string) string { return sb.id + ":floor:" + name }

func (sb *spaceBuilder) surface(kind, floor string, walkable bool, pts ...[3]float64) {
	ps := make([]map[string]float64, 0, len(pts))
	for _, p := range pts {
		ps = append(ps, sb.pt(p[0], p[1], p[2]))
	}
	sb.nSurf++
	sb.surfaces = append(sb.surfaces, map[string]any{"id": fmt.Sprintf("%s:surf:%03d", sb.id, sb.nSurf),
		"floor_id": sb.fid(floor), "kind": kind, "points": ps, "collidable": true, "walkable": walkable})
}

// slab — горизонтальная плита [a0,a1]×[b0,b1] на высоте y; hole — проём (лестничный/люк): плита
// режется на выпуклые куски вокруг него.
func (sb *spaceBuilder) slab(kind, floor string, walkable bool, y, a0, b0, a1, b1 float64, hole *[4]float64) {
	rect := func(p0, q0, p1, q1 float64) {
		if p1-p0 < 0.02 || q1-q0 < 0.02 {
			return
		}
		sb.surface(kind, floor, walkable, [3]float64{p0, y, q0}, [3]float64{p1, y, q0}, [3]float64{p1, y, q1}, [3]float64{p0, y, q1})
	}
	if hole == nil {
		rect(a0, b0, a1, b1)
		return
	}
	h := *hole
	rect(a0, b0, h[0], b1)
	rect(h[2], b0, a1, b1)
	rect(h[0], b0, h[2], h[1])
	rect(h[0], h[3], h[2], b1)
}

// wall — вертикальная стена от (a0,b0) до (a1,b1) между высотами y0 и y1.
func (sb *spaceBuilder) wall(kind, floor string, a0, b0, a1, b1, y0, y1 float64) {
	if y1-y0 < 0.02 || math.Hypot(a1-a0, b1-b0) < 0.02 {
		return
	}
	sb.surface(kind, floor, false, [3]float64{a0, y0, b0}, [3]float64{a1, y0, b1}, [3]float64{a1, y1, b1}, [3]float64{a0, y1, b0})
}

// glazing — оконное остекление: видимый световой проём, который пропускает свет, но остаётся
// collidable, то есть сквозь него нельзя пройти. Явное поле transparent отделяет визуальную
// прозрачность (клиент рисует стекло) от блокирующей коллизии — ответ на п.4 письма 0301: окно не
// дыра в несущей геометрии, а отдельная поверхность с собственным видом "window".
func (sb *spaceBuilder) glazing(floor string, pts ...[3]float64) {
	ps := make([]map[string]float64, 0, len(pts))
	for _, p := range pts {
		ps = append(ps, sb.pt(p[0], p[1], p[2]))
	}
	sb.nSurf++
	sb.surfaces = append(sb.surfaces, map[string]any{"id": fmt.Sprintf("%s:surf:%03d", sb.id, sb.nSurf),
		"floor_id": sb.fid(floor), "kind": "window", "points": ps, "collidable": true, "walkable": false, "transparent": true})
}

// winBand — параметры центрального окна для apertureWall/ringW.
type winBand struct{ w, sill, head float64 }

// apertureWall — стена от (a0,b0) до (a1,b1) между y0..y1 с центральным оконным проёмом win:
// боковые простенки во всю высоту, нижний (подоконник) и верхний (перемычка) глухие пояса, а сам
// проём заполнен прозрачным остеклением. Если стена короче окна с простенками или по высоте проём не
// помещается — стена остаётся глухой целиком. Окно получается именно рамой вокруг остекления, а не
// сквозной дырой в коллизии.
func (sb *spaceBuilder) apertureWall(kind, floor string, a0, b0, a1, b1, y0, y1 float64, win *winBand) {
	if win == nil {
		sb.wall(kind, floor, a0, b0, a1, b1, y0, y1)
		return
	}
	L := math.Hypot(a1-a0, b1-b0)
	sill := clampf(win.sill, y0+0.3, y1-0.3)
	head := clampf(win.head, sill+0.3, y1-0.1)
	if L < win.w+2*winMinPier || head-sill < 0.3 {
		sb.wall(kind, floor, a0, b0, a1, b1, y0, y1)
		return
	}
	fw := (win.w / L) / 2
	lerp := func(f float64) (float64, float64) { return a0 + (a1-a0)*f, b0 + (b1-b0)*f }
	la, lb := lerp(0.5 - fw)
	ra, rb := lerp(0.5 + fw)
	sb.wall(kind, floor, a0, b0, la, lb, y0, y1)   // левый простенок во всю высоту
	sb.wall(kind, floor, ra, rb, a1, b1, y0, y1)   // правый простенок во всю высоту
	sb.wall(kind, floor, la, lb, ra, rb, y0, sill) // подоконник: глухой пояс под проёмом
	sb.wall(kind, floor, la, lb, ra, rb, head, y1) // перемычка: глухой пояс над проёмом
	sb.glazing(floor, [3]float64{la, sill, lb}, [3]float64{ra, sill, rb}, [3]float64{ra, head, rb}, [3]float64{la, head, lb})
}

// ringW — как ring, но у трёх стен без двери (левой, правой и задней) ставит центральное окно win.
// Передняя стена (b=b0) с дверью окон не получает. win==nil делает ringW тождественным ring.
func (sb *spaceBuilder) ringW(kind, floor string, a0, b0, a1, b1, y0, y1, da, dw, dh float64, win *winBand) {
	if win == nil {
		sb.ring(kind, floor, a0, b0, a1, b1, y0, y1, da, dw, dh)
		return
	}
	sb.apertureWall(kind, floor, a0, b0, a0, b1, y0, y1, win) // левая (a=a0)
	sb.apertureWall(kind, floor, a1, b0, a1, b1, y0, y1, win) // правая (a=a1)
	sb.apertureWall(kind, floor, a0, b1, a1, b1, y0, y1, win) // задняя (b=b1)
	if dw <= 0 {
		sb.wall(kind, floor, a0, b0, a1, b0, y0, y1)
		return
	}
	sb.wall(kind, floor, a0, b0, da-dw/2, b0, y0, y1)
	sb.wall(kind, floor, da+dw/2, b0, a1, b0, y0, y1)
	sb.wall(kind, floor, da-dw/2, b0, da+dw/2, b0, dh, y1)
}

// ring — четыре стены по прямоугольнику [a0,a1]×[b0,b1]; в передней (b=b0) — проём двери шириной
// dw с центром da (dw=0 — глухая), над проёмом перемычка от dh до y1.
func (sb *spaceBuilder) ring(kind, floor string, a0, b0, a1, b1, y0, y1, da, dw, dh float64) {
	sb.wall(kind, floor, a0, b0, a0, b1, y0, y1)
	sb.wall(kind, floor, a1, b0, a1, b1, y0, y1)
	sb.wall(kind, floor, a0, b1, a1, b1, y0, y1)
	if dw <= 0 {
		sb.wall(kind, floor, a0, b0, a1, b0, y0, y1)
		return
	}
	sb.wall(kind, floor, a0, b0, da-dw/2, b0, y0, y1)
	sb.wall(kind, floor, da+dw/2, b0, a1, b0, y0, y1)
	sb.wall(kind, floor, da-dw/2, b0, da+dw/2, b0, dh, y1)
}

func (sb *spaceBuilder) floor(name, kind string, y float64) {
	sb.floors = append(sb.floors, map[string]any{"id": sb.fid(name), "kind": kind, "elevation_m": r3(sb.g + y), "walkable": true})
}

func (sb *spaceBuilder) globalBounds(a0, b0, a1, b1, y0, y1 float64) map[string]float64 {
	e0, n0 := sb.en(a0, b0)
	e1, n1 := sb.en(a1, b1)
	return map[string]float64{"min_e": r3(sb.oe + math.Min(e0, e1)), "min_n": r3(sb.on + math.Min(n0, n1)), "min_u": r3(sb.g + y0),
		"max_e": r3(sb.oe + math.Max(e0, e1)), "max_n": r3(sb.on + math.Max(n0, n1)), "max_u": r3(sb.g + y1)}
}

func (sb *spaceBuilder) room(name, floor, title, kind string, a0, b0, a1, b1, y0, y1 float64) {
	sb.rooms = append(sb.rooms, map[string]any{"id": sb.id + ":room:" + name, "floor_id": sb.fid(floor), "name": title,
		"kind": kind, "bounds": sb.globalBounds(a0, b0, a1, b1, y0, y1)})
}

func (sb *spaceBuilder) portal(name, kind string, a, y, b, width, height float64) {
	e, n := sb.en(a, b)
	sb.portals = append(sb.portals, map[string]any{"id": sb.id + ":portal:" + name, "target_space_id": nil, "target_portal_id": nil,
		"kind": kind, "e": r3(sb.oe + e), "n": r3(sb.on + n), "u": r3(sb.g + y), "width_m": r3(width), "height_m": r3(height), "open": true})
}

func (sb *spaceBuilder) stair(name, kind, from, to string, width float64, steps int, pts ...[3]float64) {
	ps := make([]map[string]float64, 0, len(pts))
	for _, p := range pts {
		ps = append(ps, sb.pt(p[0], p[1], p[2]))
	}
	sb.stairs = append(sb.stairs, map[string]any{"id": sb.id + ":stair:" + name, "from_floor": sb.fid(from), "to_floor": sb.fid(to),
		"kind": kind, "width_m": width, "step_count": steps, "points": ps})
}

func (sb *spaceBuilder) build(kind, name string) map[string]any {
	out := map[string]any{"id": sb.id, "kind": kind, "parent_id": nil, "frame": 0, "name": name,
		"origin": map[string]float64{"e": r3(sb.oe), "n": r3(sb.on), "u": r3(sb.g)},
		"bounds": map[string]float64{"min_e": r3(sb.oe + sb.lo[0]), "min_n": r3(sb.on + sb.lo[2]), "min_u": r3(sb.g + sb.lo[1]),
			"max_e": r3(sb.oe + sb.hi[0]), "max_n": r3(sb.on + sb.hi[2]), "max_u": r3(sb.g + sb.hi[1])},
		"floors": sb.floors, "surfaces": sb.surfaces, "rooms": orEmpty(sb.rooms), "portals": orEmpty(sb.portals), "stairs": orEmpty(sb.stairs)}
	for k, v := range sb.extraProps {
		out[k] = v
	}
	return out
}

func orEmpty(v []map[string]any) []map[string]any {
	if v == nil {
		return []map[string]any{}
	}
	return v
}

// boxSpec — дом-коробка в несколько ярусов: стены в толщину t, лестница вдоль задней стены,
// подвал под полом (cellar > 0), двускатная либо плоская (обходная) кровля.
type boxSpec struct {
	id, name, roomKind string
	titles             []string // названия помещений по ярусам снизу вверх
	oe, on, g, W, D    float64
	facing             int
	doorA, doorW       float64
	t                  float64
	storeys            int
	storeyH            float64
	cellar             float64
	flatRoof           bool
	windows            bool // окна в стенах первого яруса на фасадах без двери (письмо 0301)
	wallKind           string
}

func upperName(k int) string {
	switch k {
	case 0:
		return "ground"
	case 1:
		return "upper"
	default:
		return fmt.Sprintf("upper%d", k)
	}
}

func buildBox(s boxSpec) *spaceBuilder {
	sb := newSpaceBuilder(s.id, s.oe, s.on, s.g, s.W, s.D, s.facing)
	A, B, t, H := sb.A, sb.B, s.t, s.storeyH
	da := clampf(s.doorA, t+s.doorW/2+0.2, A-t-s.doorW/2-0.2)
	top := float64(s.storeys) * H // верх коробки: карниз (скатная кровля) или плоская кровля
	// лестница вверх — вдоль задней стены; проём в перекрытии — над верхними двумя третями марша
	a1 := A - t - 1.0
	a0 := math.Max(t+0.2, a1-spaceMaxRun)
	bs0, bs1 := B-t-spaceStairW, B-t
	well := &[4]float64{a0 + (a1-a0)/3, bs0, a1, bs1}
	// сход в подвал — вдоль левой стены, от задней части к двери
	cTop := bs0 - 0.2
	cRun := math.Min(spaceMaxRun*s.cellar/3.0, cTop-(t+0.8))
	hatch := &[4]float64{t, cTop - cRun*0.85, t + spaceStairW, cTop}

	if s.cellar > 0 {
		sb.floor("cellar", "cellar", -s.cellar)
	}
	for k := 0; k < s.storeys; k++ {
		kind := "upper"
		if k == 0 {
			kind = "ground"
		}
		sb.floor(upperName(k), kind, float64(k)*H)
	}
	if s.flatRoof {
		sb.floor("roof", "roof", top)
	}

	// наружная оболочка — во всю высоту; у плоской кровли выше неё парапет
	outerTop := top
	if s.flatRoof {
		outerTop = top + 1.0
	}
	var gwin *winBand // окна первого яруса — и в оболочке, и во внутреннем кольце (чтобы свет дошёл внутрь)
	if s.windows {
		gwin = &winBand{w: winW, sill: winSillH, head: winHeadH}
	}
	sb.ringW(s.wallKind, "ground", 0, 0, A, B, 0, outerTop, da, s.doorW, spaceDoorH, gwin)
	// дверной проём сквозь толщу стены: откосы, порог, притолока
	sb.wall(s.wallKind, "ground", da-s.doorW/2, 0, da-s.doorW/2, t, 0, spaceDoorH)
	sb.wall(s.wallKind, "ground", da+s.doorW/2, 0, da+s.doorW/2, t, 0, spaceDoorH)
	sb.slab("floor", "ground", true, 0, da-s.doorW/2, 0, da+s.doorW/2, t, nil)
	sb.slab("ceiling", "ground", false, spaceDoorH, da-s.doorW/2, 0, da+s.doorW/2, t, nil)
	sb.portal("door", "door", da, 0, 0, s.doorW, spaceDoorH)

	for k := 0; k < s.storeys; k++ {
		fl := upperName(k)
		y := float64(k) * H
		last := k == s.storeys-1
		ceil := y + H - spaceSlab
		var holeHere, holeAbove *[4]float64
		if k > 0 {
			holeHere = well
		} else if s.cellar > 0 {
			holeHere = hatch
		}
		if !last || s.flatRoof {
			holeAbove = well
		}
		sb.slab("floor", fl, true, y, t, t, A-t, B-t, holeHere)
		sb.slab("ceiling", fl, false, ceil, t, t, A-t, B-t, holeAbove)
		if k == 0 {
			sb.ringW(s.wallKind, fl, t, t, A-t, B-t, y, ceil, da, s.doorW, spaceDoorH, gwin)
		} else {
			sb.ring(s.wallKind, fl, t, t, A-t, B-t, y, ceil, 0, 0, 0)
		}
		title := s.name
		if k < len(s.titles) {
			title = s.titles[k]
		}
		sb.room(fl, fl, title, s.roomKind, t, t, A-t, B-t, y, ceil)
		if !last || s.flatRoof {
			to := "roof"
			if !last {
				to = upperName(k + 1)
			}
			sb.stair(to, "stairs", fl, to, spaceStairW, int(math.Round(H/spaceStepH)),
				[3]float64{a0, y, (bs0 + bs1) / 2}, [3]float64{a1, y + H, (bs0 + bs1) / 2}, [3]float64{a1 + 0.9, y + H, (bs0 + bs1) / 2})
		}
	}
	if s.flatRoof {
		sb.slab("roof", "roof", true, top, 0, 0, A, B, well)
	} else {
		ridge, ov := top+math.Min(B*0.3, 3.0), 0.5
		last := upperName(s.storeys - 1)
		sb.surface("roof", last, false, [3]float64{-ov, top, -ov}, [3]float64{A + ov, top, -ov}, [3]float64{A + ov, ridge, B / 2}, [3]float64{-ov, ridge, B / 2})
		sb.surface("roof", last, false, [3]float64{-ov, top, B + ov}, [3]float64{A + ov, top, B + ov}, [3]float64{A + ov, ridge, B / 2}, [3]float64{-ov, ridge, B / 2})
		sb.surface(s.wallKind, last, false, [3]float64{0, top, 0}, [3]float64{0, top, B}, [3]float64{0, ridge, B / 2})
		sb.surface(s.wallKind, last, false, [3]float64{A, top, 0}, [3]float64{A, top, B}, [3]float64{A, ridge, B / 2})
	}
	if s.cellar > 0 {
		c := -s.cellar
		sb.slab("floor", "cellar", true, c, t, t, A-t, B-t, nil)
		sb.slab("ceiling", "cellar", false, -spaceSlab, t, t, A-t, B-t, hatch)
		sb.ring(s.wallKind, "cellar", t, t, A-t, B-t, c, -spaceSlab, 0, 0, 0)
		sb.room("cellar", "cellar", "Погреб", "cellar", t, t, A-t, B-t, c, -spaceSlab)
		ac := t + spaceStairW/2
		sb.stair("cellar", "stairs", "ground", "cellar", spaceStairW, int(math.Round(s.cellar/spaceStepH)),
			[3]float64{ac, 0, cTop}, [3]float64{ac, c, cTop - cRun}, [3]float64{ac, c, cTop - cRun - 0.6})
	}
	return sb
}

// TavernSpaceID — устойчивое имя пространства корчмы села.
func TavernSpaceID(settlement core.ID) string { return "b:" + eidStr(settlement) + ":tavern" }

// tavernSpace — корчма села: зал, комнаты над ним (их и сдают постояльцам) и погреб.
func tavernSpace(w *core.World, t *Terrain, se *core.Entity) map[string]any {
	s := core.Settle(se)
	if s == nil {
		return nil
	}
	l := LayoutOf(w, se.ID)
	ae, an := Anchor(w, s)
	for i := range l.Buildings {
		b := &l.Buildings[i]
		if b.BuildingID != "tavern" || b.Door == nil {
			continue
		}
		spec := boxSpec{id: TavernSpaceID(se.ID), name: b.Name, roomKind: "tavern", titles: []string{"Зал корчмы", "Комнаты над корчмой"},
			oe: ae + b.X0, on: an + b.Y0, g: t.Height(ae+b.cx(), an+b.cy()), W: b.X1 - b.X0, D: b.Y1 - b.Y0,
			facing: int(math.Round(b.Door.Facing)), doorW: b.Door.Width, t: wallThickness, storeys: 2, storeyH: 3.0, cellar: 2.4, windows: true, wallKind: "wall"}
		switch spec.facing {
		case 270:
			spec.doorA = b.Door.X - b.X0
		case 90:
			spec.doorA = b.X1 - b.Door.X
		default: // 180 и 0: своя ось a идёт на север
			spec.doorA = b.Door.Y - b.Y0
		}
		sb := buildBox(spec)
		sb.extraProps = map[string]any{"settlement": eidStr(se.ID), "building_id": b.BuildingID}
		return sb.build("building", b.Name)
	}
	return nil
}

func wildSiteXY(w *core.World, ws *WildSiteC) (float64, float64) {
	p := EnsurePlanet(w)
	return (ws.X - 500) * p.KmPerUnit * 1000, (ws.Y - 500) * p.KmPerUnit * 1000
}

// towerSpace — дозорная башня в диком месте: каменные ярусы, обходная кровля и подземелье под полом.
func towerSpace(t *Terrain, id core.ID, ws *WildSiteC, ex, ny float64) map[string]any {
	storeys := int(clampf(math.Round(ws.Z/5), 3, 6))
	sb := buildBox(boxSpec{id: "w:" + eidStr(id) + ":tower", name: ws.Name, roomKind: "tower",
		titles: []string{"Нижний ярус", "Караульня", "Верхний ярус"},
		oe:     ex - towerSide/2, on: ny - towerSide/2, g: t.Height(ex, ny), W: towerSide, D: towerSide, facing: 270,
		doorA: towerSide / 2, doorW: 1.2, t: towerWall, storeys: storeys, storeyH: towerStoreyH, cellar: 3.0, flatRoof: true, wallKind: "wall"})
	sb.extraProps = map[string]any{"wildsite": eidStr(id)}
	return sb.build("building", ws.Name)
}

// caveSpace — пещера: от устья на поверхности вниз идёт ход, в глубине — зал.
func caveSpace(t *Terrain, id core.ID, ws *WildSiteC, ex, ny float64) map[string]any {
	const A, hallB, hallH, tunW, tunH = 9.0, 7.0, 3.2, 3.0, 2.4
	d := clampf(-ws.Z, 4, 12) // глубина зала под устьем
	L := d * 2.5              // длина хода: спуск примерно 1 к 2,5 — идти можно
	sb := newSpaceBuilder("w:"+eidStr(id)+":cave", ex-A/2, ny, t.Height(ex, ny), A, L+hallB, 270)
	m0, m1 := A/2-tunW/2, A/2+tunW/2
	sb.floor("mouth", "entrance", 0)
	sb.floor("hall", "cave", -d)
	// ход: пол-скат, две стены, свод
	sb.surface("floor", "mouth", true, [3]float64{m0, 0, 0}, [3]float64{m1, 0, 0}, [3]float64{m1, -d, L}, [3]float64{m0, -d, L})
	sb.surface("ceiling", "mouth", false, [3]float64{m0, tunH, 0}, [3]float64{m1, tunH, 0}, [3]float64{m1, -d + tunH, L}, [3]float64{m0, -d + tunH, L})
	for _, a := range []float64{m0, m1} {
		sb.surface("cave_wall", "mouth", false, [3]float64{a, 0, 0}, [3]float64{a, -d, L}, [3]float64{a, -d + tunH, L}, [3]float64{a, tunH, 0})
	}
	// зал
	sb.slab("floor", "hall", true, -d, 0, L, A, L+hallB, nil)
	sb.slab("ceiling", "hall", false, -d+hallH, 0, L, A, L+hallB, nil)
	sb.ring("cave_wall", "hall", 0, L, A, L+hallB, -d, -d+hallH, A/2, tunW, -d+tunH)
	sb.room("hall", "hall", ws.Name, "cave", 0, L, A, L+hallB, -d, -d+hallH)
	sb.room("tunnel", "mouth", "Ход", "tunnel", m0, 0, m1, L, -d, tunH)
	sb.portal("mouth", "cave_mouth", A/2, 0, 0, tunW, tunH)
	sb.stair("descent", "ramp", "mouth", "hall", tunW, 0, [3]float64{A / 2, 0, 0}, [3]float64{A / 2, -d, L}, [3]float64{A / 2, -d, L + 1})
	sb.extraProps = map[string]any{"wildsite": eidStr(id)}
	return sb.build("cave", ws.Name)
}

func spaceTouchesChunk(sp map[string]any, oe, on float64) bool {
	b := sp["bounds"].(map[string]float64)
	return b["max_e"] >= oe && b["min_e"] < oe+ChunkMeters && b["max_n"] >= on && b["min_n"] < on+ChunkMeters
}

// SpacesChunk — пространства, чьи объёмы задевают чанк (cx,cz), для GET /api/spaces. Одно и то же
// пространство отдаётся каждому задетому чанку целиком и с одним id. revision — отпечаток содержимого:
// не изменилось описание — не изменился и он.
func SpacesChunk(w *core.World, t *Terrain, cx, cz int64) map[string]any {
	oe, on := ChunkOrigin(cx, cz)
	ce, cn := oe+ChunkMeters/2, on+ChunkMeters/2
	spaces := []map[string]any{}
	add := func(sp map[string]any) {
		if sp != nil && spaceTouchesChunk(sp, oe, on) {
			spaces = append(spaces, sp)
		}
	}
	w.Each("settlement", func(se *core.Entity) {
		s := core.Settle(se)
		if s == nil {
			return
		}
		ae, an := Anchor(w, s)
		if math.Abs(ae-ce) > spaceReachM || math.Abs(an-cn) > spaceReachM {
			return
		}
		add(tavernSpace(w, t, se))
	})
	w.Each("wildsite", func(e *core.Entity) {
		ws := WildSite(e)
		if ws == nil || (ws.Type != "tower" && ws.Type != "cave") {
			return
		}
		ex, ny := wildSiteXY(w, ws)
		if math.Abs(ex-ce) > spaceReachM || math.Abs(ny-cn) > spaceReachM {
			return
		}
		if ws.Type == "tower" {
			add(towerSpace(t, e.ID, ws, ex, ny))
		} else {
			add(caveSpace(t, e.ID, ws, ex, ny))
		}
	})
	sort.Slice(spaces, func(i, j int) bool { return spaces[i]["id"].(string) < spaces[j]["id"].(string) })
	h := fnv.New64a()
	raw, _ := json.Marshal(spaces)
	h.Write(raw)
	return map[string]any{"scheme": WorldScheme, "v": WorldSchemeVersion, "frame": 0, "chunk": []int64{cx, cz},
		"revision": int64(h.Sum64() & (1<<52 - 1)), "spaces": spaces}
}

// SpaceAt — в каком пространстве и на каком ярусе стоит тело (me.wpos.space_id/level_id). Пока сервер
// пускает внутрь только на пол первого яруса корчмы — его и называем; под открытым небом — пусто.
func SpaceAt(w *core.World, e *core.Entity) (space, level any) {
	p := ScenePos(e)
	if p == nil || OnRoad(e) {
		return nil, nil
	}
	if pl := core.Place(e); pl != nil && pl.Settlement != 0 && LayoutOf(w, pl.Settlement).insideTavernBuilding(p.X, p.Y) {
		id := TavernSpaceID(pl.Settlement)
		return id, id + ":floor:ground"
	}
	return nil, nil
}

// MeWorldPosView — me.wpos игрока: мировая точка плюс space_id/level_id (comms 0254, этап C).
func MeWorldPosView(t *Terrain, w *core.World, e *core.Entity) any {
	m, ok := WorldPosViewUsing(t, w, e).(map[string]any)
	if !ok {
		return nil
	}
	m["space_id"], m["level_id"] = SpaceAt(w, e)
	return m
}
