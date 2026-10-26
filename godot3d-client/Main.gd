extends Node3D
# Толстый клиент «Приграничье» — 3D, вид сверху (ортографическая камера).
# Оболочка поверх готового сервера: тот же WebSocket-протокол, что и у браузера
# (вход по HTTP → cookie → WS → намерения/пуш). Вся логика на сервере; клиент
# рисует присланное состояние 3D-мешами и шлёт только намерения (op + quality).
#
# Настрой host/port. Запуск: F5 в Godot (сервер поднят; в dev-режиме — гостевой вход).

@export var host: String = "127.0.0.1"
@export var port: int = 8090

var _http: HTTPRequest
var _ws := WebSocketPeer.new()
var _cookie := ""
var _token := ""
var _connected := false
var _ws_closed_reported := false
var _rid := 0
var S: Dictionary = {}

var _world: Node3D
var _cam: Camera3D
var _sun: DirectionalLight3D
var _env: WorldEnvironment
var _status: RichTextLabel
var _log: RichTextLabel
var _sel_label: Label
var _selected := ""
var _inv_panel: PanelContainer
var _inv_box: VBoxContainer
# мини-игра (тайминг): бегунок ходит, «Стоп» ближе к центру — выше качество
var _mini_layer: Control
var _mini_marker: ColorRect
var _mini_active := false
var _mini_pos := 0.0
var _mini_dir := 1.0
var _mini_activity := ""

# управляемый аватар игрока (ходим сами, камера следует)
var _player: Node3D
var _road: Node3D
var _road_props: Array = []
var _traveling := false
var _move_target: Vector3 = Vector3.ZERO
var _has_target := false
var _prompt: Label
var _verbadge: Label
var _near: Dictionary = {}            # ближайшая точка взаимодействия
const CAM_OFFSET := Vector3(14, 16, 14)
const MOVE_SPEED := 5.5
const INTERACT_RADIUS := 1.7
const BOUND := 8.0


func _ready() -> void:
	_build_scene_3d()
	_build_ui()
	# вход гостем по HTTP (нужен cookie для рукопожатия WS)
	_http = HTTPRequest.new(); add_child(_http)
	_http.request_completed.connect(_on_auth)
	var url := "http://%s:%d/auth/guest" % [host, port]
	_http.request(url, ["Content-Type: application/json"], HTTPClient.METHOD_POST, "{}")


func _build_scene_3d() -> void:
	# мир
	_world = preload("res://World3D.gd").new()
	add_child(_world)
	# ортокамера «вид сверху» с лёгким наклоном (2.5D-ощущение)
	_cam = Camera3D.new()
	_cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	_cam.size = 16.0
	_cam.position = Vector3(14, 16, 14)
	_cam.rotation_degrees = Vector3(-50, 45, 0)
	_cam.current = true
	add_child(_cam)
	# солнце (день/ночь меняем в _apply_daylight)
	_sun = DirectionalLight3D.new()
	_sun.rotation_degrees = Vector3(-55, -40, 0)
	_sun.light_energy = 1.1
	add_child(_sun)
	# окружение: мягкий ambient + туман, чтобы край сцены тонул
	_env = WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color("#141420")
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color("#404858")
	e.ambient_light_energy = 0.6
	e.fog_enabled = true
	e.fog_density = 0.015
	e.fog_light_color = Color("#141420")
	_env.environment = e
	add_child(_env)
	# аватар игрока — капсула с подписью, им ходим
	_player = Node3D.new(); add_child(_player)
	var body := MeshInstance3D.new()
	var cap := CapsuleMesh.new(); cap.radius = 0.38; cap.height = 1.3
	body.mesh = cap; body.position = Vector3(0, 0.65, 0)
	var pm := StandardMaterial3D.new()
	pm.albedo_color = Color("#e1b12c"); pm.emission_enabled = true
	pm.emission = Color("#e1b12c"); pm.emission_energy_multiplier = 0.25
	body.material_override = pm
	_player.add_child(body)
	var plbl := Label3D.new()
	plbl.text = "⭐ ты"; plbl.position = Vector3(0, 1.7, 0)
	plbl.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	plbl.pixel_size = 0.013; plbl.outline_size = 6
	_player.add_child(plbl)
	_player.position = Vector3(0, 0, 2.0)
	_cam.position = _player.position + CAM_OFFSET


func _build_ui() -> void:
	var cl := CanvasLayer.new(); add_child(cl)
	var root := VBoxContainer.new()
	root.anchor_right = 1.0
	root.offset_left = 12; root.offset_top = 10; root.offset_right = -12
	root.add_theme_constant_override("separation", 8)
	cl.add_child(root)

	var title := Label.new()
	title.text = "Приграничье — толстый клиент (3D)"
	title.add_theme_font_size_override("font_size", 20)
	root.add_child(title)

	_status = RichTextLabel.new()
	_status.bbcode_enabled = true
	_status.fit_content = true
	_status.custom_minimum_size = Vector2(0, 54)
	_status.text = "Подключение…"
	root.add_child(_status)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	root.add_child(row)
	# занятия идут через мини-игру тайминга (качество решает игрок), остальное — сразу
	_btn(row, "🪓 Дрова", func(): _start_minigame("firewood"))
	_btn(row, "🎣 Рыба", func(): _start_minigame("fishing"))
	_btn(row, "🌿 Травы", func(): _start_minigame("herbs"))
	_btn(row, "🏹 Охота", func(): _send("hunt", {}))
	_btn(row, "🍺 Выпить", func(): _send("drink", {}))
	_btn(row, "🃏 Очко", func(): _send("card", {"action": "start"}))
	_btn(row, "🂿 Дурак", func(): _send("durak", {"action": "start"}))
	_btn(row, "🎒 Вещи", func(): _toggle_inventory())
	_btn(row, "🛏 Спать", func(): _send("sleep", {}))
	_btn(row, "🐫 В путь", func(): _travel_next())
	_btn(row, "💾", func(): _send("save", {}))

	# подсказка по движению
	var movehint := Label.new()
	movehint.text = "WASD / клик по земле — ходить · подойди к месту и жми Пробел/E"
	movehint.add_theme_color_override("font_color", Color("#8a93a0"))
	movehint.add_theme_font_size_override("font_size", 12)
	root.add_child(movehint)

	# подпись выбранного объекта (клик по жителю/строению)
	_sel_label = Label.new()
	_sel_label.add_theme_color_override("font_color", Color("#d8b968"))
	root.add_child(_sel_label)

	# подсказка взаимодействия (по центру, над кнопками действий)
	_prompt = Label.new()
	_prompt.anchor_left = 0.5; _prompt.anchor_right = 0.5
	_prompt.anchor_top = 1.0; _prompt.anchor_bottom = 1.0
	_prompt.offset_left = -220; _prompt.offset_right = 220
	_prompt.offset_top = -210; _prompt.offset_bottom = -182
	_prompt.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_prompt.add_theme_color_override("font_color", Color("#ffe6a0"))
	_prompt.add_theme_font_size_override("font_size", 18)
	cl.add_child(_prompt)

	# бейдж версии (правый нижний угол)
	_verbadge = Label.new()
	_verbadge.anchor_left = 1.0; _verbadge.anchor_right = 1.0
	_verbadge.anchor_top = 1.0; _verbadge.anchor_bottom = 1.0
	_verbadge.offset_left = -120; _verbadge.offset_top = -24
	_verbadge.offset_right = -8; _verbadge.offset_bottom = -4
	_verbadge.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_verbadge.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	_verbadge.add_theme_font_size_override("font_size", 11)
	cl.add_child(_verbadge)

	_build_inventory_panel(cl)
	_build_minigame(cl)

	# лог внизу
	var bottom := Control.new()
	bottom.anchor_top = 1.0; bottom.anchor_bottom = 1.0; bottom.anchor_right = 1.0
	bottom.offset_top = -170; bottom.offset_left = 12; bottom.offset_right = -12; bottom.offset_bottom = -10
	cl.add_child(bottom)
	_log = RichTextLabel.new()
	_log.bbcode_enabled = true
	_log.scroll_following = true
	_log.anchor_right = 1.0; _log.anchor_bottom = 1.0
	bottom.add_child(_log)


func _btn(parent: Node, text: String, cb: Callable) -> void:
	var b := Button.new(); b.text = text; b.pressed.connect(cb); parent.add_child(b)


# ── панель вещей и экипировки (паперdoll) ──
func _build_inventory_panel(cl: CanvasLayer) -> void:
	_inv_panel = PanelContainer.new()
	_inv_panel.anchor_left = 1.0; _inv_panel.anchor_right = 1.0
	_inv_panel.offset_left = -320; _inv_panel.offset_top = 96; _inv_panel.offset_right = -12
	_inv_panel.visible = false
	cl.add_child(_inv_panel)
	var sc := ScrollContainer.new()
	sc.custom_minimum_size = Vector2(300, 420)
	_inv_panel.add_child(sc)
	_inv_box = VBoxContainer.new()
	_inv_box.add_theme_constant_override("separation", 4)
	sc.add_child(_inv_box)


func _toggle_inventory() -> void:
	_inv_panel.visible = not _inv_panel.visible
	if _inv_panel.visible:
		_refresh_inventory()


const SLOT_LABELS := {"weapon": "⚔ в руках", "head": "🎩 голова", "body": "👕 тело",
	"outfit": "🧥 верх", "feet": "🥾 ноги", "trinket": "🧿 оберег"}


func _refresh_inventory() -> void:
	if not _inv_panel.visible:
		return
	for c in _inv_box.get_children():
		c.queue_free()
	var me: Dictionary = S.get("me", {})
	_hdr("🧍 Надето · тепло %s" % me.get("warmth", 0))
	var worn: Dictionary = me.get("worn", {})
	for slot in ["weapon", "head", "body", "outfit", "feet", "trinket"]:
		var row := HBoxContainer.new()
		var lab := Label.new()
		if worn.has(slot):
			lab.text = "%s: %s" % [SLOT_LABELS[slot], worn[slot].get("label", "")]
			row.add_child(lab)
			var ub := Button.new(); ub.text = "снять"
			ub.pressed.connect(func(): _send("item", {"action": "unequip", "slot": slot}); _later_refresh())
			row.add_child(ub)
		else:
			lab.text = "%s: —" % SLOT_LABELS[slot]
			row.add_child(lab)
		_inv_box.add_child(row)
	_hdr("🎒 При себе · ⌾ %s" % me.get("coins", 0))
	for it in me.get("inventory", []):
		var row := HBoxContainer.new()
		var lab := Label.new(); lab.text = str(it.get("label", "")); lab.custom_minimum_size = Vector2(150, 0)
		row.add_child(lab)
		var iid: String = str(it.get("id", ""))
		_mini_btn(row, "надеть", func(): _send("item", {"action": "equip", "id": iid}); _later_refresh())
		_mini_btn(row, "склад", func(): _send("item", {"action": "store", "id": iid}); _later_refresh())
		_mini_btn(row, "прод.", func(): _send("item", {"action": "sell", "id": iid}); _later_refresh())
		_inv_box.add_child(row)


func _hdr(text: String) -> void:
	var l := Label.new(); l.text = text
	l.add_theme_color_override("font_color", Color("#d8b968"))
	_inv_box.add_child(l)


func _mini_btn(parent: Node, text: String, cb: Callable) -> void:
	var b := Button.new(); b.text = text
	b.add_theme_font_size_override("font_size", 11)
	b.pressed.connect(cb); parent.add_child(b)


func _later_refresh() -> void:
	# дать серверу ответить, затем перерисовать (следующий кадр достаточно)
	await get_tree().create_timer(0.25).timeout
	_refresh_inventory()


# ── мини-игра тайминга (качество занятий решает игрок) ──
func _build_minigame(cl: CanvasLayer) -> void:
	_mini_layer = Control.new()
	_mini_layer.anchor_right = 1.0; _mini_layer.anchor_bottom = 1.0
	_mini_layer.visible = false
	_mini_layer.mouse_filter = Control.MOUSE_FILTER_STOP
	cl.add_child(_mini_layer)
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.5); dim.anchor_right = 1.0; dim.anchor_bottom = 1.0
	_mini_layer.add_child(dim)
	var panel := PanelContainer.new()
	panel.anchor_left = 0.5; panel.anchor_top = 0.5; panel.anchor_right = 0.5; panel.anchor_bottom = 0.5
	panel.offset_left = -220; panel.offset_top = -80; panel.offset_right = 220; panel.offset_bottom = 80
	_mini_layer.add_child(panel)
	var vb := VBoxContainer.new(); panel.add_child(vb)
	var title := Label.new(); title.name = "title"; title.text = "Поймай момент — пробел/Стоп ближе к центру"
	vb.add_child(title)
	var bar := ColorRect.new(); bar.name = "bar"; bar.color = Color("#2a2a34")
	bar.custom_minimum_size = Vector2(400, 40)
	vb.add_child(bar)
	var zone := ColorRect.new(); zone.color = Color("#3a5a3a")
	zone.position = Vector2(170, 0); zone.size = Vector2(60, 40)
	bar.add_child(zone)
	_mini_marker = ColorRect.new(); _mini_marker.color = Color("#d8b968")
	_mini_marker.size = Vector2(8, 40)
	bar.add_child(_mini_marker)
	var stop := Button.new(); stop.text = "Стоп"
	stop.pressed.connect(_finish_minigame)
	vb.add_child(stop)


func _start_minigame(activity: String) -> void:
	_mini_activity = activity
	_mini_pos = 0.0; _mini_dir = 1.0
	_mini_active = true
	_mini_layer.visible = true


func _finish_minigame() -> void:
	if not _mini_active:
		return
	_mini_active = false
	_mini_layer.visible = false
	var quality := clampf(1.0 - absf(_mini_pos - 0.5) * 2.0, 0.0, 1.0)
	_log_line("[%s] точность %d%%" % [_mini_activity, int(quality * 100)])
	_send("do", {"activity": _mini_activity, "quality": quality})


func _on_auth(_result: int, code: int, headers: PackedStringArray, body: PackedByteArray) -> void:
	if code != 200:
		_status.text = "Вход не удался (код %d). Поднят ли сервер в dev-режиме?" % code
		return
	for h in headers:
		if h.to_lower().begins_with("set-cookie:"):
			_cookie = h.substr(11).strip_edges().split(";")[0]
	var resp = JSON.parse_string(body.get_string_from_utf8())
	if typeof(resp) == TYPE_DICTIONARY:
		_token = String(resp.get("token", ""))
		_log_line("[вход] %s" % resp.get("status", "гость"))
	if _token == "":
		_status.text = "Сервер не вернул токен сессии — обнови сервер (нужна версия с ?sid)."
		_log_line("[WS] нет токена в ответе /auth/guest — старый сервер?")
		return
	# Токен уходит в URL (/ws?sid=…) — сервер это принимает, cookie-заголовок не нужен.
	# handshake_headers ставим мягко через set(): если свойства нет (разные версии
	# Godot) — просто игнорируется и не роняет подключение.
	if _cookie != "":
		_ws.set("handshake_headers", PackedStringArray(["Cookie: " + _cookie]))
	var url := "ws://%s:%d/ws?sid=%s" % [host, port, _token]
	_log_line("[WS] подключаюсь: %s" % url)
	var err := _ws.connect_to_url(url)
	if err != OK:
		_status.text = "Не удалось начать подключение к WebSocket (ошибка %d)." % err
		_log_line("[WS] connect_to_url вернул ошибку %d" % err)


func _process(dt: float) -> void:
	# бегунок мини-игры
	if _mini_active and _mini_marker:
		_mini_pos += _mini_dir * dt * 1.1
		if _mini_pos >= 1.0:
			_mini_pos = 1.0; _mini_dir = -1.0
		elif _mini_pos <= 0.0:
			_mini_pos = 0.0; _mini_dir = 1.0
		var bar := _mini_marker.get_parent() as Control
		if bar:
			_mini_marker.position = Vector2(_mini_pos * (bar.size.x - 8.0), 0)
	else:
		_update_movement(dt)         # ходьба отключена, пока открыта мини-игра
	_ws.poll()
	var st := _ws.get_ready_state()
	if st == WebSocketPeer.STATE_OPEN:
		if not _connected:
			_connected = true
			_status.text = "Подключено. Загружаю мир…"
			_log_line("[WS] соединение открыто")
		while _ws.get_available_packet_count() > 0:
			_on_packet(_ws.get_packet().get_string_from_utf8())
	elif st == WebSocketPeer.STATE_CLOSED:
		# закрылось: показать код/причину один раз (диагностика)
		if _connected or not _ws_closed_reported:
			_ws_closed_reported = true
			_connected = false
			var cc := _ws.get_close_code()
			var cr := _ws.get_close_reason()
			if cc == -1:
				_log_line("[WS] не удалось открыть соединение (handshake отклонён сервером). Обнови сервер и поставь GAME_ENV=dev.")
				_status.text = "WebSocket не открылся — проверь версию/режим сервера (см. лог)."
			else:
				_log_line("[WS] закрыто (код %d) %s" % [cc, cr])
				_status.text = "WebSocket закрыт (код %d)." % cc


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		# пробел/E: в мини-игре — завершить; иначе — взаимодействовать с ближайшей точкой
		if event.keycode == KEY_SPACE or event.keycode == KEY_E:
			if _mini_active:
				_finish_minigame()
			else:
				_do_interact()
			get_viewport().set_input_as_handled()
			return
	# клик левой кнопкой по земле — идти туда; рядом с жителем — выбрать его
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if not _pick_at(event.position):        # не попали по жителю — ставим цель движения
			_set_move_target(event.position)


func _set_move_target(screen_pos: Vector2) -> void:
	if _cam == null:
		return
	# пересечём луч из камеры с землёй (плоскость y=0)
	var origin := _cam.project_ray_origin(screen_pos)
	var dirn := _cam.project_ray_normal(screen_pos)
	var plane := Plane(Vector3.UP, 0.0)
	var hit = plane.intersects_ray(origin, dirn)
	if hit != null:
		_move_target = hit
		_has_target = true


func _pick_at(screen_pos: Vector2) -> bool:
	# вернёт true, если кликнули по жителю (и выбрали его); иначе false (клик по земле)
	if not _world or not _cam:
		return false
	var best := ""
	var best_d := 44.0        # порог в пикселях
	var positions: Dictionary = _world.token_positions()
	for name in positions.keys():
		var sp := _cam.unproject_position(positions[name])
		var d := sp.distance_to(screen_pos)
		if d < best_d:
			best_d = d; best = name
	if best == "":
		return false
	_selected = best
	_world.highlight(best)
	var h: Dictionary = S.get("here", {})
	var role := ""
	for r in h.get("roster", []):
		if r.get("name", "") == best:
			role = str(r.get("role", "")); break
	_sel_label.text = "🧍 %s%s" % [best, (" · " + role) if role != "" else ""]
	return true


func _build_road() -> void:
	if _road != null:
		return
	_road = Node3D.new(); add_child(_road)
	# длинная дорога (тёмная полоса) вдоль -Z
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new(); pm.size = Vector2(8, 120)
	ground.mesh = pm
	var gm := StandardMaterial3D.new(); gm.albedo_color = Color("#4a3f2e")
	ground.material_override = gm
	ground.position = Vector3(0, -0.05, -40)
	_road.add_child(ground)
	# придорожные деревья по обе стороны — их будем «прокручивать» к игроку
	for i in range(14):
		for side in [-3.2, 3.2]:
			var t := MeshInstance3D.new()
			var bm := BoxMesh.new(); bm.size = Vector3(0.9, 1.6, 0.9)
			t.mesh = bm
			var m := StandardMaterial3D.new(); m.albedo_color = Color("#2f6b39")
			t.material_override = m
			t.position = Vector3(side, 0.8, -i * 9.0 - 4.0)
			_road.add_child(t); _road_props.append(t)

func _enter_travel() -> void:
	if _traveling:
		return
	_traveling = true
	_build_road()
	_road.visible = true
	if _world:
		_world.visible = false
	# игрок «шагает на месте» в начале дороги, камера за ним
	_player.position = Vector3(0, 0, 0)
	_has_target = false
	_prompt.text = ""

func _exit_travel() -> void:
	_traveling = false
	if _road:
		_road.visible = false
	if _world:
		_world.visible = true
	_player.position = Vector3(0, 0, 2.0)

func _update_movement(dt: float) -> void:
	if _player == null or _cam == null:
		return
	if _traveling:
		# «беговая дорожка»: деревья едут к игроку, создавая ощущение пути
		for t in _road_props:
			t.position.z += 6.0 * dt
			if t.position.z > 12.0:
				t.position.z -= 126.0
		# лёгкое покачивание аватара
		_player.position.y = 0.08 * sin(Time.get_ticks_msec() * 0.006)
		_cam.position = _cam.position.lerp(_player.position + CAM_OFFSET, clampf(dt * 4.0, 0, 1))
		return
	# направление от клавиш относительно камеры (чтобы W = «от камеры вперёд»)
	var fwd := -_cam.global_transform.basis.z; fwd.y = 0; fwd = fwd.normalized()
	var rgt := _cam.global_transform.basis.x; rgt.y = 0; rgt = rgt.normalized()
	var dir := Vector3.ZERO
	if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP): dir += fwd
	if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN): dir -= fwd
	if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT): dir += rgt
	if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT): dir -= rgt
	if dir.length() > 0.01:
		_has_target = false
		_player.position += dir.normalized() * MOVE_SPEED * dt
	elif _has_target:
		var to := _move_target - _player.position; to.y = 0
		if to.length() < 0.15:
			_has_target = false
		else:
			_player.position += to.normalized() * MOVE_SPEED * dt
	# границы сцены
	_player.position.x = clampf(_player.position.x, -BOUND, BOUND)
	_player.position.z = clampf(_player.position.z, -BOUND, BOUND)
	# камера следует за игроком, сохраняя угол
	_cam.position = _cam.position.lerp(_player.position + CAM_OFFSET, clampf(dt * 4.0, 0, 1))
	_update_nearest()

func _update_nearest() -> void:
	_near = {}
	if _world == null:
		return
	var best := INTERACT_RADIUS
	var mypos := _player.position
	for sp in _world.interactables():
		var spos: Vector3 = sp["pos"]
		var d: float = Vector2(spos.x - mypos.x, spos.z - mypos.z).length()
		if d < best:
			best = d; _near = sp
	if _near.is_empty():
		_prompt.text = ""
	else:
		var verb: String = "В таверну" if _near.get("type") == "tavern" else str(_near.get("label", ""))
		_prompt.text = "[Пробел] %s %s" % [str(_near.get("emoji", "")), verb]

func _do_interact() -> void:
	if _near.is_empty():
		return
	var act: String = _near.get("action", "")
	if act in ["firewood", "fishing", "herbs"]:
		_start_minigame(act)
	elif act == "hunt":
		_send("hunt", {})
	elif act == "tavern":
		_send("drink", {})
		_log_line("🍯 В таверне: Выпить — здесь, Очко и Дурак — на верхней панели.")

func _on_packet(text: String) -> void:
	var m = JSON.parse_string(text)
	if typeof(m) != TYPE_DICTIONARY:
		return
	match m.get("type", ""):
		"state":
			S = m["state"]; _render()
		"delta":
			for k in m["changed"]:
				if m["changed"][k] == null:
					S.erase(k)
				else:
					S[k] = m["changed"][k]
			_render()
		"ack":
			var r = m.get("res", {})
			if typeof(r) == TYPE_DICTIONARY:
				_log_line("< " + str(r.get("msg", r)))


func _render() -> void:
	if _world:
		_world.set_state(S)
	_apply_daylight(int(S.get("hour", 8)))
	if _inv_panel and _inv_panel.visible:
		_refresh_inventory()
	var me: Dictionary = S.get("me", {})
	if S.has("transit"):
		_enter_travel()
		_status.text = "🐫 В пути → %s (осталось %s ч)" % [S["transit"].get("to"), S["transit"].get("left")]
		_prompt.text = "🐫 в пути…"
		return
	if _traveling:
		_exit_travel()
	if _verbadge:
		_verbadge.text = "v" + str(S.get("version", "?"))
	var h: Dictionary = S.get("here", {})
	var near := ""
	if h.get("players_here", []).size() > 0:
		near = "  рядом: " + ", ".join(h["players_here"])
	_status.text = "[b]%s[/b]  день %s, %02d:00  %s %s\n★%s  ⌾%s  тепло %s  усталость %s  голод %s%s" % [
		h.get("name", "?"), S.get("day"), int(S.get("hour", 0)),
		h.get("season", ""), h.get("weather", ""),
		me.get("reputation"), me.get("coins"), me.get("warmth"),
		me.get("fatigue"), me.get("hunger"), near]
	# энкаунтер — в прототипе бьём сразу
	if S.has("encounter"):
		var e: Dictionary = S["encounter"]
		_log_line("⚔ %s %s — бьём!" % [e.get("icon", ""), e.get("name", "зверь")])
		_send("strike", {"quality": 0.8})
		S.erase("encounter")


func _apply_daylight(hour: int) -> void:
	# простая кривая день/ночь: энергия и цвет солнца + фон/туман
	var day := 1.0
	if hour >= 21: day = clampf(1.0 - (hour - 21) / 4.0, 0.08, 1.0)
	elif hour < 7: day = clampf(hour / 7.0, 0.08, 1.0)
	_sun.light_energy = lerpf(0.15, 1.2, day)
	_sun.light_color = Color("#ffe6c0").lerp(Color("#3a4a80"), 1.0 - day)
	if _env and _env.environment:
		var sky := Color("#0b0e1a").lerp(Color("#8db0d8"), day)
		_env.environment.background_color = sky
		_env.environment.fog_light_color = sky
		_env.environment.ambient_light_energy = lerpf(0.25, 0.7, day)


func _travel_next() -> void:
	# идём в ближайшую ИЗВЕСТНУЮ деревню (сервер всё равно не пустит в неизвестную)
	for v in S.get("settlements", []):
		if not v.get("current", false) and int(v.get("know", 0)) >= 1:
			_send("travel", {"to": int(v.get("idx"))})
			_log_line("[путь] → %s" % v.get("name", "?"))
			return
	_log_line("[путь] некуда — ещё не знаешь других деревень")


func _send(op: String, extra: Dictionary) -> void:
	if _ws.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return
	_rid += 1
	var msg := {"op": op, "rid": _rid}
	msg.merge(extra)
	_ws.send_text(JSON.stringify(msg))


func _log_line(s: String) -> void:
	_log.append_text(s + "\n")
