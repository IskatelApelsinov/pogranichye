extends Control
# Прототип толстого клиента «Приграничье» на Godot 4.
# Доказывает: толстый клиент — это оболочка поверх готового сервера. Тот же
# WebSocket-протокол, что и у браузера (вход по HTTP → cookie → WS → намерения/пуш).
# Логика — на сервере; клиент только рисует состояние и шлёт намерения.
#
# Настрой адрес сервера в HOST/PORT. Запуск: F5 в Godot (сервер должен быть поднят,
# в dev-режиме, чтобы работал гостевой вход).

@export var host: String = "127.0.0.1"
@export var port: int = 8090

var _http: HTTPRequest
var _ws := WebSocketPeer.new()
var _cookie := ""
var _connected := false
var _rid := 0
var S: Dictionary = {}          # текущее состояние мира (как в браузере)

# ── UI (строим кодом, без возни со сценой) ──
var _status: RichTextLabel
var _log: RichTextLabel
var _world: Node2D      # изо-сцена (World2D.gd)


func _ready() -> void:
	var root := VBoxContainer.new()
	root.anchor_right = 1.0
	root.anchor_bottom = 1.0
	root.add_theme_constant_override("separation", 10)
	add_child(root)

	var title := Label.new()
	title.text = "Приграничье — прототип (Godot)"
	title.add_theme_font_size_override("font_size", 22)
	root.add_child(title)

	# изо-сцена деревни (рисуется примитивами в World2D.gd)
	var stage := Control.new()
	stage.custom_minimum_size = Vector2(0, 240)
	root.add_child(stage)
	_world = preload("res://World2D.gd").new()
	_world.position = Vector2(440, 60)      # центр сцены (сдвиг под изометрию)
	stage.add_child(_world)

	_status = RichTextLabel.new()
	_status.bbcode_enabled = true
	_status.fit_content = true
	_status.custom_minimum_size = Vector2(0, 120)
	_status.text = "Подключение…"
	root.add_child(_status)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	root.add_child(row)
	_add_button(row, "🪓 Работать (дрова)", func(): _send("do", {"activity": "firewood", "quality": 0.8}))
	_add_button(row, "🏹 Охота", func(): _hunt())
	_add_button(row, "🍺 Выпить", func(): _send("drink", {}))
	_add_button(row, "🛏 В кровать", func(): _send("sleep", {}))
	_add_button(row, "🐫 В путь (к соседу)", func(): _travel_next())
	_add_button(row, "💾 Сохранить", func(): _send("save", {}))

	_log = RichTextLabel.new()
	_log.bbcode_enabled = true
	_log.scroll_following = true
	_log.custom_minimum_size = Vector2(0, 320)
	root.add_child(_log)

	# вход гостем по HTTP (нужен cookie для рукопожатия WS)
	_http = HTTPRequest.new()
	add_child(_http)
	_http.request_completed.connect(_on_auth)
	var url := "http://%s:%d/auth/guest" % [host, port]
	_http.request(url, ["Content-Type: application/json"], HTTPClient.METHOD_POST, "{}")


func _add_button(parent: Node, text: String, cb: Callable) -> void:
	var b := Button.new()
	b.text = text
	b.pressed.connect(cb)
	parent.add_child(b)


func _on_auth(_result: int, code: int, headers: PackedStringArray, body: PackedByteArray) -> void:
	if code != 200:
		_status.text = "Вход не удался (код %d). Запущен ли сервер в dev-режиме?" % code
		return
	for h in headers:
		if h.to_lower().begins_with("set-cookie:"):
			_cookie = h.substr(11).strip_edges().split(";")[0]
	var resp := JSON.parse_string(body.get_string_from_utf8())
	_log_line("[вход] %s" % (resp.get("status", "?") if resp else "?"))
	# открыть WebSocket с cookie сессии
	_ws.handshake_headers = PackedStringArray(["Cookie: " + _cookie])
	_ws.connect_to_url("ws://%s:%d/ws" % [host, port])


func _process(_dt: float) -> void:
	_ws.poll()
	var st := _ws.get_ready_state()
	if st == WebSocketPeer.STATE_OPEN:
		if not _connected:
			_connected = true
			_log_line("[WS] соединение открыто")
		while _ws.get_available_packet_count() > 0:
			_on_packet(_ws.get_packet().get_string_from_utf8())
	elif st == WebSocketPeer.STATE_CLOSED and _connected:
		_connected = false
		_log_line("[WS] соединение закрыто")


func _on_packet(text: String) -> void:
	var m = JSON.parse_string(text)
	if typeof(m) != TYPE_DICTIONARY:
		return
	match m.get("type", ""):
		"state":
			S = m["state"]
			_render()
		"delta":
			for k in m["changed"]:
				if m["changed"][k] == null:
					S.erase(k)
				else:
					S[k] = m["changed"][k]
			_render()
		"ack":
			var r = m.get("res", {})
			_log_line("< " + str(r.get("msg", r)))
			if r.get("encounter"):
				pass


func _render() -> void:
	if _world:
		_world.set_state(S)
	var me: Dictionary = S.get("me", {})
	if S.has("transit"):
		_status.text = "🐫 В пути → %s (осталось %s ч)" % [S["transit"].get("to"), S["transit"].get("left")]
		return
	var h: Dictionary = S.get("here", {})
	var near := ""
	if h.get("players_here", []).size() > 0:
		near = "  рядом: " + ", ".join(h["players_here"])
	_status.text = "[b]%s[/b]  день %s, %02d:00  %s %s\n★%s  ⌾%s  усталость %s  голод %s%s" % [
		h.get("name", "?"), S.get("day"), int(S.get("hour", 0)),
		h.get("season", ""), h.get("weather", ""),
		me.get("reputation"), me.get("coins"), me.get("fatigue"), me.get("hunger"), near]
	# если энкаунтер — автоматически бьём (прототип): показываем и наносим удар
	if S.has("encounter"):
		var e: Dictionary = S["encounter"]
		_log_line("⚔ %s %s — бьём!" % [e.get("icon"), e.get("name")])
		_send("strike", {"quality": 0.8})
		S.erase("encounter")


func _hunt() -> void:
	_send("hunt", {})


func _travel_next() -> void:
	for v in S.get("settlements", []):
		if not v.get("current", false):
			_send("travel", {"to": int(v.get("idx"))})
			return


func _send(op: String, extra: Dictionary) -> void:
	if _ws.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return
	_rid += 1
	var msg := {"op": op, "rid": _rid}
	msg.merge(extra)
	_ws.send_text(JSON.stringify(msg))


func _log_line(s: String) -> void:
	_log.append_text(s + "\n")
