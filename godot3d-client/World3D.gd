extends Node3D
# 3D-деревня «Приграничья»: пол-плитки, строения-боксы, токены-жители.
# Примитивы (без импорта ассетов) — тот же принцип, что в 2.5D тонкого клиента,
# но честные 3D-меши под ортокамерой. Данные те же, что шлёт сервер (state.here + hour).
# Движение по времени суток: день — по делам, вечер — в таверну, ночь — по домам.

const SP := 2.4                      # шаг сетки в мире
const COLS := 7
const ROWS := 6
# деревня-улица: площадь в центре (таверна+колодец), дома вдоль улиц, кузница/амбар
const HOUSE := [Vector2i(1,0), Vector2i(3,0), Vector2i(5,0), Vector2i(0,2), Vector2i(6,2),
                Vector2i(1,5), Vector2i(3,5), Vector2i(5,5), Vector2i(0,4), Vector2i(6,4)]
const TAV := Vector2i(3,2)
const WELL := Vector2i(3,3)
const FORGE := Vector2i(5,1)
const BARN := Vector2i(1,1)
const INN := Vector2i(4,2)            # у таверны — снять комнату/спать
# дороги (клетки, по которым идёт мощёная улица): крест через площадь + выезды
const ROADS := [Vector2i(0,2),Vector2i(1,2),Vector2i(2,2),Vector2i(3,2),Vector2i(4,2),Vector2i(5,2),Vector2i(6,2),
                Vector2i(3,0),Vector2i(3,1),Vector2i(3,3),Vector2i(3,4),Vector2i(3,5)]
# столы у таверны (смещения от клетки таверны, в метрах)
const TABLES := [Vector2(-1.4,1.3), Vector2(1.4,1.3), Vector2(-1.4,-1.2), Vector2(1.4,-1.2)]
const WALK := [Vector2i(2,1), Vector2i(4,1), Vector2i(2,4), Vector2i(4,4), Vector2i(1,3), Vector2i(5,3)]

var S: Dictionary = {}
var hour: int = 8
var _static_sig := ""
var _static_root: Node3D
var _tokens: Dictionary = {}         # имя -> {node, label, target:Vector3}
var _tok_root: Node3D
var _spots: Array = []               # точки взаимодействия: {type,label,action,pos,emoji}

# постоянные места занятий на карте (клетка, эмодзи, подпись, намерение серверу)
const SPOTS := [
	{"cell": Vector2(0, 0), "emoji": "🪵", "label": "Поленница", "action": "firewood"},
	{"cell": Vector2(6, 0), "emoji": "🎣", "label": "Рыбное место", "action": "fishing"},
	{"cell": Vector2(0, 5), "emoji": "🌿", "label": "Травы", "action": "herbs"},
	{"cell": Vector2(6, 5), "emoji": "🏹", "label": "Угодья", "action": "hunt"},
]

func _ready() -> void:
	_static_root = Node3D.new(); add_child(_static_root)
	_tok_root = Node3D.new(); add_child(_tok_root)

func cell_pos(c: float, r: float, y := 0.0) -> Vector3:
	# центрируем деревню вокруг начала координат
	var ox := (COLS - 1) * SP * 0.5
	var oz := (ROWS - 1) * SP * 0.5
	return Vector3(c * SP - ox, y, r * SP - oz)

func hashi(s: String, m: int) -> int:
	var h := 0
	for i in s.length():
		h = (h * 31 + s.unicode_at(i)) % 1000003
	return abs(h) % max(1, m)

func role_color(role: String) -> Color:
	match role:
		"woodcutter": return Color("#8a6a3a")
		"smith": return Color("#6a6a72")
		"cook": return Color("#c86a4a")
		"fisher": return Color("#4a8aa0")
		"hunter": return Color("#5a7a4a")
		"herbalist": return Color("#6a9a5a")
		"tavernkeeper": return Color("#9a6a8a")
		_: return Color("#9bb0c8")

var _tex_cache := {}

func _tex(name: String):
	# текстура из textures/<name>, если положена в проект; иначе null (откат на цвет)
	if _tex_cache.has(name):
		return _tex_cache[name]
	var path := "res://textures/" + name
	var t = load(path) if ResourceLoader.exists(path) else null
	_tex_cache[name] = t
	return t

func _mat(col: Color, emission := 0.0, texname := "") -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = col
	if texname != "":
		var t = _tex(texname)
		if t != null:
			m.albedo_texture = t
			m.uv1_triplanar = true          # аккуратно натягивает текстуру на бокс
	if emission > 0.0:
		m.emission_enabled = true
		m.emission = col
		m.emission_energy_multiplier = emission
	return m

func _box(parent: Node3D, pos: Vector3, size: Vector3, col: Color, texname := "") -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new(); bm.size = size
	mi.mesh = bm
	mi.material_override = _mat(col, 0.0, texname)
	mi.position = pos + Vector3(0, size.y * 0.5, 0)
	parent.add_child(mi)
	return mi

func set_state(st: Dictionary) -> void:
	S = st
	hour = int(S.get("hour", 8))
	var h: Dictionary = S.get("here", {})
	var winter: bool = bool(h.get("cold", false)) or String(h.get("season", "")) == "зима"
	var alive := int(h.get("alive", 8))
	var sig := "%s|%s|%d|%s" % [h.get("name", "?"), winter, int(round(alive / 4.0)), str(h.get("tavern", true))]
	if sig != _static_sig:
		_static_sig = sig
		_build_static(h, winter, alive)
		_build_spots(h)
	_sync_tokens(h)

func _build_spots(h: Dictionary) -> void:
	# точки взаимодействия на карте: места занятий (тумба+подпись) и таверна
	_spots.clear()
	for sp in SPOTS:
		var cell: Vector2 = sp["cell"]
		var pos := cell_pos(cell.x, cell.y)
		_box(_static_root, pos, Vector3(0.5, 0.5, 0.5), Color("#caa15a"))
		var lbl := Label3D.new()
		lbl.text = sp["emoji"] + " " + sp["label"]
		lbl.position = pos + Vector3(0, 1.1, 0)
		lbl.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		lbl.pixel_size = 0.011; lbl.outline_size = 6
		_static_root.add_child(lbl)
		_spots.append({"type": "work", "label": sp["label"], "action": sp["action"],
			"pos": pos, "emoji": sp["emoji"]})
	if h.get("tavern", true):
		_spots.append({"type": "tavern", "label": "Таверна", "action": "tavern",
			"pos": cell_pos(TAV.x, TAV.y), "emoji": "🍯"})
		_spots.append({"type": "inn", "label": "Комната (аренда/сон)", "action": "inn",
			"pos": cell_pos(INN.x, INN.y), "emoji": "🛏"})

func interactables() -> Array:
	return _spots

func table_positions() -> Array:
	# мировые позиции столов у таверны (чтобы рассаживать посетителей вечером)
	var base := cell_pos(TAV.x, TAV.y)
	var out := []
	for off in TABLES:
		out.append(base + Vector3(off.x, 0, off.y))
	return out

func _build_static(h: Dictionary, winter: bool, alive: int) -> void:
	for c in _static_root.get_children():
		c.queue_free()
	# пол-плитки
	var grass := Color("#dce6f0") if winter else Color("#6a9a4a")
	for r in range(ROWS):
		for c in range(COLS):
			var shade := 0.94 + 0.06 * ((c + r) % 2)
			_box(_static_root, cell_pos(c, r, -0.2), Vector3(SP * 0.98, 0.2, SP * 0.98), grass * shade, "snow.png" if winter else "grass.png")
	# дороги — мощёная улица (крест через площадь + выезды)
	for rc in ROADS:
		_box(_static_root, cell_pos(rc.x, rc.y, -0.12), Vector3(SP * 0.7, 0.16, SP * 0.7), Color("#6a5236"))
	# дома
	var nb: int = clampi(int(round(alive / 3.0)), 3, HOUSE.size())
	for i in range(nb):
		var p: Vector2i = HOUSE[i]
		_box(_static_root, cell_pos(p.x, p.y), Vector3(1.5, 1.4, 1.5), Color("#7a5a3a"), "wood.png")
		_box(_static_root, cell_pos(p.x, p.y, 1.4), Vector3(1.7, 0.5, 1.7), Color("#8a3a2a"), "roof.png")
	# кузница (с «огнём») и амбар
	_box(_static_root, cell_pos(FORGE.x, FORGE.y), Vector3(1.5, 1.3, 1.5), Color("#4a4a52"), "wood.png")
	_box(_static_root, cell_pos(FORGE.x, FORGE.y, 1.3), Vector3(1.7, 0.4, 1.7), Color("#2a2a30"), "roof.png")
	_sign(cell_pos(FORGE.x, FORGE.y, 2.0), "🔨 Кузница")
	_box(_static_root, cell_pos(BARN.x, BARN.y), Vector3(1.6, 1.2, 1.6), Color("#8a6a3a"), "wood.png")
	_box(_static_root, cell_pos(BARN.x, BARN.y, 1.2), Vector3(1.8, 0.5, 1.8), Color("#6a4a2a"), "roof.png")
	_sign(cell_pos(BARN.x, BARN.y, 1.9), "🌾 Амбар")
	# колодец на площади
	_box(_static_root, cell_pos(WELL.x, WELL.y), Vector3(0.9, 0.8, 0.9), Color("#6e5234"))
	_box(_static_root, cell_pos(WELL.x, WELL.y, 0.8), Vector3(0.3, 0.9, 0.3), Color("#5e4630"))
	# таверна + вывеска + столы
	if h.get("tavern", true):
		_box(_static_root, cell_pos(TAV.x, TAV.y), Vector3(2.0, 2.0, 2.0), Color("#6a4a7a"), "wood.png")
		_box(_static_root, cell_pos(TAV.x, TAV.y, 2.0), Vector3(2.2, 0.5, 2.2), Color("#c0392b"), "roof.png")
		_sign(cell_pos(TAV.x, TAV.y, 2.7), "🍯 Таверна")
		for tp in table_positions():
			_box(_static_root, tp, Vector3(0.9, 0.5, 0.9), Color("#7a5a3a"), "wood.png")   # стол
	# деревья по краям
	var tree_col := Color("#2f6b39") if not winter else Color("#bcd4c0")
	for spot in [Vector2(-1, 1), Vector2(7, 1), Vector2(-1, 4), Vector2(7, 4), Vector2(3, -1)]:
		_box(_static_root, cell_pos(spot.x, spot.y), Vector3(0.4, 1.5, 0.4), Color("#5e4630"))
		_box(_static_root, cell_pos(spot.x, spot.y, 1.5), Vector3(1.3, 1.3, 1.3), tree_col)

func _sign(pos: Vector3, text: String) -> void:
	var lbl := Label3D.new()
	lbl.text = text; lbl.position = pos
	lbl.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	lbl.pixel_size = 0.012; lbl.outline_size = 6
	_static_root.add_child(lbl)

func target_cell(name: String, in_tavern: bool, is_me: bool) -> Vector2i:
	var sleep := hour >= 23 or hour < 7
	var evening := hour >= 18 and hour < 23
	if sleep:
		return HOUSE[hashi(name, HOUSE.size())]
	if evening and (in_tavern or is_me):
		return TAV
	return WALK[hashi(name + "w", WALK.size())]

func _sync_tokens(h: Dictionary) -> void:
	var me: Dictionary = S.get("me", {})
	var tav := {}
	for n in h.get("tavern", []):
		tav[n] = true
	var want := {}
	var roster: Array = h.get("roster", [])
	for i in range(min(7, roster.size())):
		var rr: Dictionary = roster[i]
		want[rr.get("name", "?")] = {"color": role_color(rr.get("role", "")), "me": false}
	for n in h.get("players_here", []):
		want[n] = {"color": Color("#cfe0ff"), "me": false}
	# «меня» среди токенов НЕ рисуем — игрока представляет управляемый аватар (в Main)
	var myname: String = me.get("name", "")
	want.erase(myname)
	# убрать исчезнувших
	for n in _tokens.keys():
		if not want.has(n):
			_tokens[n].node.queue_free()
			_tokens.erase(n)
	# создать/обновить
	for n in want.keys():
		var meta: Dictionary = want[n]
		if not _tokens.has(n):
			var node := Node3D.new(); _tok_root.add_child(node)
			var body := MeshInstance3D.new()
			var cap := CapsuleMesh.new(); cap.radius = 0.35; cap.height = 1.2
			body.mesh = cap; body.position = Vector3(0, 0.6, 0)
			body.material_override = _mat(meta.color, 0.15 if meta.me else 0.0)
			node.add_child(body)
			var lbl := Label3D.new()
			lbl.text = n; lbl.position = Vector3(0, 1.6, 0)
			lbl.billboard = BaseMaterial3D.BILLBOARD_ENABLED
			lbl.pixel_size = 0.012; lbl.modulate = Color(1, 1, 1)
			lbl.outline_size = 6
			node.add_child(lbl)
			var start := target_cell(n, tav.has(n), meta.me)
			node.position = cell_pos(start.x, start.y)
			_tokens[n] = {"node": node, "label": lbl, "body": body, "me": meta.me}
		var t: Dictionary = _tokens[n]
		var evening := hour >= 18 and hour < 23
		if evening and tav.has(n):
			# вечером посетитель сидит ЗА СТОЛОМ у таверны (а не кучей на клетке)
			var tbls := table_positions()
			t.target = tbls[hashi(n, tbls.size())] + Vector3(0, 0, 0)
		else:
			var cell := target_cell(n, tav.has(n), t.me)
			var jx := float(hashi(n, 7) - 3) * 0.12
			var jz := float(hashi(n + "y", 5) - 2) * 0.12
			t.target = cell_pos(cell.x, cell.y) + Vector3(jx, 0, jz)
		var sleeping := hour >= 23 or hour < 7
		t.label.text = ("💤 " if sleeping else "") + n

func _process(dt: float) -> void:
	# плавное движение токенов к целям
	for n in _tokens.keys():
		var t: Dictionary = _tokens[n]
		if t.has("target"):
			t.node.position = t.node.position.lerp(t.target, clampf(dt * 1.6, 0, 1))

func token_positions() -> Dictionary:
	# имя -> мировая позиция (для выбора кликом из Main)
	var out := {}
	for n in _tokens.keys():
		out[n] = _tokens[n].node.position + Vector3(0, 0.8, 0)
	return out

func tavern_position() -> Vector3:
	return cell_pos(TAV.x, TAV.y, 1.0)

func highlight(name: String) -> void:
	# подсветить выбранный токен (чуть ярче), снять с остальных
	for n in _tokens.keys():
		var b: MeshInstance3D = _tokens[n].body
		var m := b.material_override as StandardMaterial3D
		if m:
			m.emission_enabled = (n == name) or bool(_tokens[n].me)
			m.emission_energy_multiplier = (0.5 if n == name else (0.15 if _tokens[n].me else 0.0))
