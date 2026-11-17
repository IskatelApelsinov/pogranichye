extends Node2D
# Изометрическая сцена деревни, нарисованная примитивами (без импорта ассетов) —
# тайлы-ромбы, кубики-строения, токены-жители. Движение по времени суток:
# день — по делам, вечер — в таверну, ночь — по домам (спят). День/ночь — затемнением.
# Данные те же, что шлёт сервер (state.here + hour). Это 2.5D-каркас под Godot.

const TW := 64.0
const TH := 32.0
const COLS := 5
const ROWS := 4
const HOUSE := [Vector2(0,0), Vector2(2,0), Vector2(4,0), Vector2(0,2), Vector2(3,3)]
const TAV := Vector2(2,2)
const WALK := [Vector2(1,1), Vector2(3,1), Vector2(1,3), Vector2(2,3), Vector2(4,2), Vector2(0,3)]

var S: Dictionary = {}
var hour: int = 8
var tok_cur: Dictionary = {}     # имя -> текущая экранная позиция (для плавности)
var tok_tgt: Dictionary = {}     # имя -> целевая позиция
var tok_meta: Dictionary = {}    # имя -> {color, label}

func iso(c: float, r: float) -> Vector2:
	return Vector2((c - r) * TW / 2.0, (c + r) * TH / 2.0)

func hashi(s: String, m: int) -> int:
	var h := 0
	for i in s.length():
		h = (h * 31 + s.unicode_at(i)) % 1000000
	return abs(h) % m

func role_color(role: String) -> Color:
	match role:
		"woodcutter": return Color("#6a9a4a")
		"smith": return Color("#b0702a")
		"cook": return Color("#c0392b")
		"fisher": return Color("#2e86c1")
		"tavernkeeper": return Color("#8e44ad")
		_: return Color("#9bb0c8")

func set_state(st: Dictionary) -> void:
	S = st
	hour = int(st.get("hour", 8))
	_recompute()

func _recompute() -> void:
	var H: Dictionary = S.get("here", {})
	var tav := {}
	for n in H.get("tavern", []):
		tav[n] = true
	var want := {}
	var roster: Array = H.get("roster", [])
	for i in range(min(7, roster.size())):
		var r = roster[i]
		want[r["name"]] = {"color": role_color(r.get("role", "")), "me": false}
	for n in H.get("players_here", []):
		want[n] = {"color": Color("#cfe3ff"), "me": false}
	var me: Dictionary = S.get("me", {})
	want[me.get("name", "ты")] = {"color": Color("#e1b12c"), "me": true}
	# цели по времени суток
	tok_meta.clear()
	tok_tgt.clear()
	for name in want:
		tok_meta[name] = want[name]
		var cell: Vector2 = _target_cell(name, tav.has(name) or want[name]["me"])
		var jit := Vector2(hashi(name, 7) - 3, hashi(name + "y", 5) - 2)
		tok_tgt[name] = iso(cell.x, cell.y) + jit
		if not tok_cur.has(name):
			tok_cur[name] = tok_tgt[name]       # новый токен появляется на месте
	# убрать ушедших
	for name in tok_cur.keys():
		if not want.has(name):
			tok_cur.erase(name)

func _target_cell(name: String, in_tavern: bool) -> Vector2:
	if hour >= 23 or hour < 7:
		return HOUSE[hashi(name, HOUSE.size())]        # ночь — по домам
	if hour >= 18 and hour < 23 and in_tavern:
		return TAV                                      # вечер — в таверну
	return WALK[hashi(name + "w", WALK.size())]         # день — по делам

func _process(delta: float) -> void:
	var moved := false
	for name in tok_tgt:
		var cur: Vector2 = tok_cur.get(name, tok_tgt[name])
		tok_cur[name] = cur.lerp(tok_tgt[name], clampf(delta * 1.6, 0, 1))
		moved = true
	if moved:
		queue_redraw()

func _dark() -> float:
	if hour >= 21:
		return clampf((hour - 21) / 3.0 + 0.45, 0, 0.82)
	if hour < 7:
		return clampf((7 - hour) / 4.0 + 0.3, 0, 0.82)
	return 0.0

func _diamond(center: Vector2, w: float, h: float) -> PackedVector2Array:
	return PackedVector2Array([center + Vector2(0, -h/2), center + Vector2(w/2, 0),
							   center + Vector2(0, h/2), center + Vector2(-w/2, 0)])

func _draw() -> void:
	var H: Dictionary = S.get("here", {})
	var winter: bool = bool(H.get("cold", false)) or H.get("season", "") == "зима"
	var grass := Color("#5a8a3a") if not winter else Color("#cdd6e6")
	var edge := Color("#4a7a2e") if not winter else Color("#aebccf")
	# пол из ромб-тайлов (сзади вперёд)
	for r in range(ROWS):
		for c in range(COLS):
			var p := iso(c, r) + Vector2(TW/2, TH/2)
			var dia := _diamond(p, TW, TH)
			draw_colored_polygon(dia, grass)
			var outline := dia.duplicate()
			outline.append(dia[0])
			draw_polyline(outline, edge, 1.0)
	# строения
	var alive := int(H.get("alive", 8))
	var nb: int = clampi(int(round(alive / 4.0)), 2, HOUSE.size())
	for i in range(nb):
		_building(HOUSE[i], Color("#7a5a3a"), Color("#8a3a2a"))
	if H.get("tavern", null) != null:
		_building(TAV, Color("#6a4a7a"), Color("#c0392b"))
	# токены
	var font := ThemeDB.fallback_font
	var sleeping := hour >= 23 or hour < 7
	for name in tok_cur:
		var pos: Vector2 = tok_cur[name] + Vector2(TW/2, TH/2)
		var meta: Dictionary = tok_meta.get(name, {"color": Color.WHITE, "me": false})
		draw_circle(pos + Vector2(0, -2), 5.0, Color(0, 0, 0, 0.3))     # тень
		draw_circle(pos + Vector2(0, -16), 7.0, meta["color"])          # голова-токен
		var tag := ("zzz " if sleeping else ("* " if meta.get("me", false) else "")) + name
		draw_string(font, pos + Vector2(-14, 6), tag, HORIZONTAL_ALIGNMENT_LEFT, -1, 9, Color("#e8e6e3"))
	# затемнение ночи
	var d := _dark()
	if d > 0.0:
		draw_rect(Rect2(Vector2(-400, -80), Vector2(800, 400)), Color(0.02, 0.03, 0.08, d))
		# «горящие окна»
		for i in range(nb):
			var wp := iso(HOUSE[i].x, HOUSE[i].y) + Vector2(TW/2, TH/2 - 14)
			draw_circle(wp, 3.0, Color("#ffd36b"))

func _building(cell: Vector2, wall: Color, roof: Color) -> void:
	var base := iso(cell.x, cell.y) + Vector2(TW/2, TH/2)
	var top := base + Vector2(0, -20)
	var hw := TW/2 - 6
	var hh := TH/2 - 4
	var wallh := 20.0
	# две грани
	draw_colored_polygon(PackedVector2Array([top + Vector2(-hw,0), top + Vector2(0,hh),
		top + Vector2(0,hh+wallh), top + Vector2(-hw,wallh)]), wall.darkened(0.15))
	draw_colored_polygon(PackedVector2Array([top + Vector2(0,hh), top + Vector2(hw,0),
		top + Vector2(hw,wallh), top + Vector2(0,hh+wallh)]), wall)
	# крыша-ромб
	draw_colored_polygon(_diamond(top, hw*2, hh*2), roof)
