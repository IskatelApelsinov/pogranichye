using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;
using Pogranichye.Client.Presentation;
using UnityEngine;

namespace Pogranichye.Client
{
    public sealed class VerticalSliceController : MonoBehaviour
    {
        private enum WorldConnectionState
        {
            Closed,
            Connecting,
            AwaitingSnapshot,
            Active
        }

        [SerializeField] private string serverUrl = "http://127.0.0.1:8090";
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private WorldSocketClient _socket;
        private EntityViewRegistry _views;
        private PresentationCameraRig _cameraRig;
        private WorldInteractionProbe _interactionProbe;
        private bool _createdCameraRig;
        private Transform _presentationRoot;
        private string _login = "";
        private string _password = "";
        private string _status = "Введите учётные данные сервера.";
        private string _token;
        private string _goalToSend;
        private WorldFrame _frame;
        private WorldLayoutFrame _layout;
        private WorldConnectionState _connectionState = WorldConnectionState.Closed;
        private bool _busy;
        private int _tutorialStep;
        private string _tutorialPendingGoal;
        private bool _firstPerson;
        private bool _hudCollapsed;
        private bool _movementDebugVisible;
        private Vector2 _movementInputDebug;
        private Vector3 _movementDirectionDebug;
        private float _movementCameraYawDebug;
        private float _movementPlayerYawDebug;
        private float _movementServerFacingDebug;
        private Vector2 _hudScroll;
        [SerializeField] private float moveSendInterval = 0.05f;
        private float _moveSendAccumulator;
        private float _lastMoveSentAt;
        private long _moveSequence;
        private long _lastConfirmedMoveSequence;
        private bool _hasMovementBaseline;
        private bool _moveSendInFlight;
        private readonly List<PendingMoveIntent> _pendingMoveIntents = new List<PendingMoveIntent>();
        private readonly Queue<PendingMoveIntent> _moveSendQueue = new Queue<PendingMoveIntent>();
        private Vector3 _movementCorrection;
        private float _movementCorrectionRemaining;
        private float _serverStamina = 1f;
        // Jump is deliberately presentation-only until the server owns vertical movement.
        private float _jumpOffset;
        private float _jumpVelocity;
        private const float JumpImpulse = 5.2f;
        private const float JumpGravity = 15f;

        private struct PendingMoveIntent
        {
            public long Sequence;
            public float Mx;
            public float My;
            public bool Run;
            public float Face;
            public float DeltaTime;
        }

        private static readonly string[] TutorialGoals = { "socialize", "work", "eat", "rest" };

        private void Awake()
        {
            _presentationRoot = new GameObject("PresentationWorld").transform;
            _presentationRoot.SetParent(transform, false);
            var entityRoot = new GameObject("EntityViews").transform;
            entityRoot.SetParent(_presentationRoot, false);
            _views = new EntityViewRegistry(entityRoot);
            WorldPoi.Selected += OnPoiSelected;
            WorldNpc.Selected += OnNpcSelected;
            SetupPresentationScene();
            ProceduralVillageBuilder.Build(_presentationRoot);
            WorldEnvironmentBuilder.Build(_presentationRoot, _cameraRig != null ? _cameraRig.GetComponent<Camera>() : Camera.main);
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var action)) action();
            if (Input.GetKeyDown(KeyCode.F1)) _hudCollapsed = !_hudCollapsed;
            if (Input.GetKeyDown(KeyCode.F3)) _movementDebugVisible = !_movementDebugVisible;
            if (Input.GetKeyDown(KeyCode.F11)) ToggleFullscreen();
            if (_interactionProbe != null)
                _interactionProbe.enabled = _connectionState == WorldConnectionState.Active;

            if (_connectionState == WorldConnectionState.Active)
            {
                UpdateJump();
                UpdateMovement();
            }

            if (_connectionState == WorldConnectionState.Active && !string.IsNullOrEmpty(_goalToSend))
            {
                var goal = _goalToSend;
                _goalToSend = null;
                _ = SendGoalAsync(goal);
            }
        }

        private void OnGUI()
        {
            if (_hudCollapsed)
            {
                if (GUI.Button(new Rect(12, 12, 150, 30), "Показать меню [F1]"))
                    _hudCollapsed = false;
            }
            else
            {
            GUI.Box(new Rect(12, 12, 430, 510), "ПОГРАНИЧЬЕ — первая деревня");
            GUILayout.BeginArea(new Rect(24, 42, 406, 470));
            _hudScroll = GUILayout.BeginScrollView(_hudScroll, GUILayout.Width(406), GUILayout.Height(470));
            if (GUILayout.Button("Свернуть панель [F1]", GUILayout.Height(26))) _hudCollapsed = true;
            if (GUILayout.Button(Screen.fullScreen ? "Оконный режим [F11]" : "Полный экран [F11]", GUILayout.Height(24))) ToggleFullscreen();
            GUILayout.Label("Сервер");
            GUI.enabled = _connectionState == WorldConnectionState.Closed && !_busy;
            serverUrl = GUILayout.TextField(serverUrl);
            GUI.enabled = true;

            if (string.IsNullOrEmpty(_token))
            {
                GUILayout.Space(8);
                GUILayout.Label("Логин");
                _login = GUILayout.TextField(_login);
                GUILayout.Label("Пароль");
                _password = GUILayout.PasswordField(_password, '*');
                GUI.enabled = !_busy;
                if (GUILayout.Button("Войти")) _ = LoginAsync();
                if (GUILayout.Button("Зарегистрировать")) _ = RegisterAsync();
                GUI.enabled = true;
            }
            else if (_connectionState != WorldConnectionState.Active || _frame == null || !_frame.Alive)
            {
                GUILayout.Space(10);
                GUILayout.Label(ConnectionStateLabel());
                if (_connectionState == WorldConnectionState.Closed)
                {
                    GUI.enabled = !_busy;
                    if (GUILayout.Button("Подключиться к миру")) _ = ConnectAsync();
                    GUI.enabled = true;
                }
                else if (GUILayout.Button("Отменить подключение"))
                {
                    DisconnectWorld("Подключение отменено.");
                }

                if (GUILayout.Button("Выйти из аккаунта")) Logout();
            }
            else
            {
                GUILayout.Label($"День {_frame.Day}, год {_frame.Year}, {_frame.Season}");
                if (_frame.Place != null)
                {
                    GUILayout.Label($"{_frame.Place.Name} ({_frame.Place.Tier})");
                    GUILayout.Label($"Еда: {_frame.Place.Food:0} | Хлеб: {_frame.Place.Bread:0} | Цена: {_frame.Place.BreadPrice:0.0}");
                }
                GUILayout.Space(6);
                GUILayout.Label($"{_frame.Me.Name} {_frame.Me.Surname} — {_frame.Me.Role}");
                GUILayout.Label($"Здоровье {_frame.Me.Health:0} | Голод {_frame.Me.Hunger:0} | Энергия {_frame.Me.Energy:0} | Настрой {_frame.Me.Morale:0}");
                GUILayout.Label($"Цель: {_frame.Me.Goal} | Действие: {_frame.Me.Last}");
                GUILayout.Label($"Монеты {_frame.Me.Coins:0.0} | Долг {_frame.Me.Debt:0.0}");
                GUILayout.Label($"Запал бега (сервер): {_serverStamina * 100f:0}% | WASD — движение, Shift — бег, Space — прыжок");
                DrawInventorySummary();
                if (_frame.Me.Position != null)
                    GUILayout.Label($"Сцена X {_frame.Me.Position.X:0.00} / Z {_frame.Me.Position.Y:0.00} | взгляд {_frame.Me.Position.Facing:0}° | seq {_frame.Me.Position.Sequence}/{_moveSequence}");
                if (_frame.Me.Position != null)
                    GUILayout.Label($"Сеть движения: очередь {_moveSendQueue.Count} | неподтверждено {_pendingMoveIntents.Count} | отправка {(_moveSendInFlight ? "да" : "нет")}");
                else
                    GUILayout.Label("Движение недоступно: серверный кадр без me.pos (нужен v1.1).");
                GUILayout.Space(6);
                GUILayout.Space(8);
                if (GUILayout.Button(_firstPerson ? "Переключить на вид от третьего лица" : "Переключить на вид от первого лица"))
                    SetCameraMode(!_firstPerson);
                GUILayout.Label(_firstPerson
                    ? "WASD — движение; Shift — бег; Space — прыжок; мышь — осмотр; ПКМ — альтернативный режим предмета/инструмента."
                    : "WASD — движение; Shift — бег; Space — прыжок; мышь — обзор камеры; колесо — зум; ПКМ — альтернативный режим предмета/инструмента.");
                GUILayout.Space(4);
                GUILayout.Label("ПЕРВЫЕ ШАГИ");
                GUILayout.Label(TutorialHint(), GUILayout.Height(38));
                GUILayout.Label("Выберите занятие — сервер выполнит действие по правилам мира:");
                if (_tutorialStep == 0 && !IsNearTavern())
                    GUILayout.Label("Подойдите к входу в таверну и нажмите E у указателя. Сейчас вы ещё далеко.");
                else if (GUILayout.Button("🍺 Таверна — поговорить"))
                    RequestTutorialGoal("socialize");
                if (GUILayout.Button("🔨 Кузница / огород — работать")) RequestTutorialGoal("work");
                if (GUILayout.Button("🍞 Рынок — поесть")) RequestTutorialGoal("eat");
                if (GUILayout.Button("🏠 Дом — отдохнуть")) RequestTutorialGoal("rest");
                GUILayout.Space(5);
                GUILayout.Label("🍺 РАЗГОВОР И ПИТЬЁ");
                GUILayout.Label("Заговорите с жителем клавишей E: сервер даст варианты реплик, знакомство откроет имя. У корчмы можно угостить знакомого; входящее приглашение игрока появится здесь.", GUILayout.Height(54));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🪓 Рубить")) _ = SendWorldActionAsync("chop");
                if (GUILayout.Button("🎣 Рыбачить")) _ = SendWorldActionAsync("fish");
                if (GUILayout.Button("✨ Разряд")) _ = SendWorldActionAsync("cast");
                GUILayout.EndHorizontal();
                GUILayout.Label("Мана: " + _frame.Me.Mana.ToString("0.0"));
                if (GUILayout.Button("Отключиться от мира")) DisconnectWorld("Отключено от мира. Можно подключиться снова.");
                if (GUILayout.Button("Выйти из аккаунта")) Logout();
            }

            GUILayout.Space(8);
            GUILayout.Label(_status, GUILayout.Height(56));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            }

            if (_firstPerson && _connectionState == WorldConnectionState.Active)
            {
                var previousColor = GUI.color;
                GUI.color = Color.white;
                GUI.Label(new Rect(Screen.width * 0.5f - 8f, Screen.height * 0.5f - 12f, 16f, 24f), "·");
                GUI.color = previousColor;
            }
            DrawMovementDebugOverlay();
            DrawSocialOverlay();
        }

        private static void ToggleFullscreen()
        {
            var enable = !Screen.fullScreen;
            Screen.fullScreenMode = enable ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.fullScreen = enable;
        }

        private void DrawMovementDebugOverlay()
        {
            if (!_movementDebugVisible) return;

            var rect = new Rect(Screen.width - 326f, 12f, 314f, 142f);
            GUI.Box(rect, "ДВИЖЕНИЕ / КАМЕРА [F3]");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, 20f),
                "Режим: " + (_firstPerson ? "первое лицо" : "третье лицо"));
            GUI.Label(new Rect(rect.x + 10f, rect.y + 48f, rect.width - 20f, 20f),
                $"WASD input: X {_movementInputDebug.x:0.00} / Y {_movementInputDebug.y:0.00}");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 68f, rect.width - 20f, 20f),
                $"Направление мира: X {_movementDirectionDebug.x:0.00} / Z {_movementDirectionDebug.z:0.00}");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 88f, rect.width - 20f, 20f),
                $"Yaw камеры {_movementCameraYawDebug:0.0}° | yaw игрока {_movementPlayerYawDebug:0.0}°");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 108f, rect.width - 20f, 20f),
                $"Facing сервера {_movementServerFacingDebug:0.0}°");
        }

        private void DrawInventorySummary()
        {
            var inventory = _frame?.Me?.Inventory;
            if (inventory == null) return;

            GUILayout.Space(5);
            GUILayout.Label("ИНВЕНТАРЬ И ЭКИПИРОВКА");
            GUILayout.Label("В руке: " + (string.IsNullOrEmpty(inventory.Hand) ? "пусто" : inventory.Hand));
            if (inventory.Items != null && inventory.Items.Count > 0)
            {
                var shown = 0;
                foreach (var item in inventory.Items)
                {
                    if (item.Value <= 0) continue;
                    GUILayout.Label(item.Key + " × " + item.Value);
                    shown++;
                    if (shown >= 5) break;
                }
                if (inventory.Items.Count > shown)
                    GUILayout.Label("…и другие предметы");
            }
            else
            {
                GUILayout.Label("Предметов в сумке пока нет.");
            }

            if (inventory.Worn != null && inventory.Worn.Count > 0)
            {
                foreach (var worn in inventory.Worn)
                    GUILayout.Label("Надето: " + worn.Key + " — " + worn.Value);
            }
            if (!string.IsNullOrEmpty(_frame.Me.Desire))
                GUILayout.Label("Самочувствие: " + _frame.Me.Desire);
        }

        private void DrawSocialOverlay()
        {
            if (_frame?.Me == null) return;
            var talk = _frame.Me.Talk;
            if (talk != null)
            {
                var rect = new Rect(Screen.width - 430f, Screen.height - 260f, 410f, 244f);
                GUI.Box(rect, "Разговор");
                GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 38f));
                GUILayout.Label(talk.Who ?? "Собеседник");
                GUILayout.Label(talk.Line ?? "…", GUILayout.Height(54f));
                if (talk.Options != null)
                {
                    foreach (var option in talk.Options)
                    {
                        if (option != null && GUILayout.Button(option.Text ?? option.Id, GUILayout.Height(28f)))
                            _ = SendSayAsync(option.Id);
                    }
                }
                if (GUILayout.Button("Закончить разговор")) _ = SendEndTalkAsync();
                GUILayout.EndArea();
            }

            var invite = _frame.Me.InviteFrom;
            if (invite != null)
            {
                var rect = new Rect(Screen.width - 430f, Screen.height - 150f, 410f, 128f);
                GUI.Box(rect, "Приглашение выпить");
                GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 38f));
                GUILayout.Label((invite.Who ?? "Игрок") + " зовёт к стойке");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Принять")) _ = SendDrinkResponseAsync(true);
                if (GUILayout.Button("Отказаться")) _ = SendDrinkResponseAsync(false);
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }

            // Outgoing co-drink is offered only for neighbors the server marks as controlled players.
            var neighbors = _frame.Place?.Neighbors;
            if (neighbors != null)
            {
                var inviteableCount = 0;
                foreach (var neighbor in neighbors)
                    if (neighbor != null && neighbor.Controlled) inviteableCount++;
                if (inviteableCount > 0)
                {
                    var height = 56f + inviteableCount * 34f;
                    var rect = new Rect(Screen.width - 330f, 18f, 310f, height);
                    GUI.Box(rect, "Игроки рядом");
                    GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 25f, rect.width - 20f, rect.height - 32f));
                    foreach (var neighbor in neighbors)
                    {
                        if (neighbor == null || !neighbor.Controlled) continue;
                        var label = neighbor.Known && !string.IsNullOrEmpty(neighbor.Name)
                            ? neighbor.Name : (neighbor.Description ?? "Игрок");
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(label);
                        if (GUILayout.Button("Позвать выпить", GUILayout.Width(130f)))
                            _ = SendInviteDrinkAsync(neighbor.Id);
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndArea();
                }
            }
        }

        private async Task SendWorldActionAsync(string action)
        {
            var socket = _socket;
            try
            {
                if (_connectionState != WorldConnectionState.Active || socket == null)
                    throw new InvalidOperationException("Мир ещё не подключён.");
                await socket.SendActAsync(action, _lifetime.Token);
                Enqueue(() => _status = "Действие отправлено: " + action + ". Результат подтвердит сервер.");
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось выполнить действие: " + ex.Message); }
        }

        private async Task SendTalkAsync(string target)
        {
            var socket = _socket;
            try
            {
                if (_connectionState != WorldConnectionState.Active || socket == null)
                    throw new InvalidOperationException("Мир ещё не подключён.");
                await socket.SendTalkAsync(target, _lifetime.Token);
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось начать разговор: " + ex.Message); }
        }

        private async Task SendSayAsync(string line)
        {
            var socket = _socket;
            try
            {
                if (_connectionState != WorldConnectionState.Active || socket == null)
                    throw new InvalidOperationException("Мир ещё не подключён.");
                await socket.SendSayAsync(line, _lifetime.Token);
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось отправить реплику: " + ex.Message); }
        }

        private async Task SendEndTalkAsync()
        {
            var socket = _socket;
            try
            {
                if (socket != null) await socket.SendEndTalkAsync(_lifetime.Token);
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось закончить разговор: " + ex.Message); }
        }

        private async Task SendInviteDrinkAsync(string target)
        {
            var socket = _socket;
            try
            {
                if (_connectionState != WorldConnectionState.Active || socket == null)
                    throw new InvalidOperationException("Мир ещё не подключён.");
                await socket.SendInviteDrinkAsync(target, _lifetime.Token);
                Enqueue(() => _status = "Приглашение выпить отправлено серверу.");
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось пригласить игрока: " + ex.Message); }
        }

        private async Task SendDrinkResponseAsync(bool accept)
        {
            var socket = _socket;
            try
            {
                if (socket != null) await socket.SendAcceptDrinkAsync(accept, _lifetime.Token);
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось ответить на приглашение: " + ex.Message); }
        }

        private string ConnectionStateLabel()
        {
            switch (_connectionState)
            {
                case WorldConnectionState.Connecting:
                    return "Устанавливаю WebSocket-соединение…";
                case WorldConnectionState.AwaitingSnapshot:
                    return "Соединение установлено. Ожидаю первый полный кадр мира…";
                case WorldConnectionState.Active:
                    return "Мир подключён.";
                default:
                    return "Нет подключения к миру.";
            }
        }

        private string TutorialHint()
        {
            if (_frame == null || _frame.Me == null)
                return "Подключитесь к миру, чтобы начать знакомство с деревней.";
            if (_tutorialStep >= TutorialGoals.Length)
                return "Основные действия освоены. Разговоры, знакомство и совместное питьё теперь подтверждаются сервером.";
            if (!string.IsNullOrEmpty(_tutorialPendingGoal))
                return "Ожидаем подтверждение сервера. Последнее действие: " + _frame.Me.Last;

            switch (_tutorialStep)
            {
                case 0: return "Шаг 1/4 — подойдите к жителю и нажмите E. Выберите реплику «Будем знакомы».";
                case 1: return "Шаг 2/4 — попробуйте работу. Профессия персонажа влияет на результат.";
                case 2: return "Шаг 3/4 — выберите еду. Следите за голодом и запасами поселения.";
                default: return "Шаг 4/4 — отдохните. Энергия восстанавливается по правилам сервера.";
            }
        }

        private void SetCameraMode(bool firstPerson)
        {
            _firstPerson = firstPerson;
            if (_cameraRig != null) _cameraRig.SetFirstPerson(firstPerson);
            if (_views != null) _views.SetPlayerFirstPerson(firstPerson, _cameraRig != null ? _cameraRig.transform : null);
            _status = firstPerson
                ? "Первое лицо: мышь управляет взглядом; ПКМ — контекстный альтернативный режим (в разработке)."
                : "Третье лицо: мышь вращает камеру; ПКМ — контекстный альтернативный режим.";
        }

        private void OnPoiSelected(WorldPoi poi)
        {
            if (poi == null) return;
            if (_connectionState != WorldConnectionState.Active)
            {
                _status = "Сначала подключитесь к миру, затем выбирайте точки деревни.";
                return;
            }

            if (!string.IsNullOrEmpty(poi.ActionIntent))
            {
                _ = SendWorldActionAsync(poi.ActionIntent);
                _status = "Выбрана точка: " + poi.Title + ". Действие отправлено серверу на проверку.";
                return;
            }

            RequestTutorialGoal(poi.Goal);
            _status = "Выбрана точка: " + poi.Title + ". Отправляю намерение серверу…";
        }

        private void OnNpcSelected(WorldNpc npc)
        {
            if (npc == null) return;
            if (_connectionState != WorldConnectionState.Active)
            {
                _status = "Подключитесь к миру, чтобы обратиться к жителю.";
                return;
            }

            _ = SendTalkAsync(npc.EntityId.ToString());
            _status = "Обращаюсь к жителю. Жду ответ сервера и доступные реплики.";
        }

        private bool IsNearTavern()
        {
            var player = _views != null ? _views.PlayerTransform : null;
            if (player == null) return false;

            if (_layout?.Pois == null || !_layout.Pois.TryGetValue("tavern", out var tavern)) return false;
            var offset = new Vector2(player.position.x - (float)tavern.X, player.position.z - (float)tavern.Y);
            return offset.sqrMagnitude <= 8f * 8f;
        }

        private void RequestTutorialGoal(string goal)
        {
            if (goal == "socialize" && _tutorialStep == 0 && !IsNearTavern())
            {
                _status = "Сначала подойдите к входу в таверну. Наведите камеру на указатель и нажмите E.";
                return;
            }

            _tutorialPendingGoal = goal;
            _goalToSend = goal;
        }

        private async Task RegisterAsync()
        {
            if (!ValidateCredentials()) return;
            _busy = true;
            try
            {
                var response = await new HttpAuthClient(serverUrl).RegisterAsync(_login, _password);
                Enqueue(() => _status = response.Ok ? "Учётная запись создана. Теперь войдите." : response.Message);
            }
            catch (Exception ex) { Enqueue(() => _status = ex.Message); }
            finally { Enqueue(() => _busy = false); }
        }

        private async Task LoginAsync()
        {
            if (!ValidateCredentials()) return;
            _busy = true;
            try
            {
                var response = await new HttpAuthClient(serverUrl).LoginAsync(_login, _password);
                if (!response.Ok || string.IsNullOrEmpty(response.Token))
                    throw new InvalidOperationException(response.Message ?? "Вход не выполнен.");
                Enqueue(() => { _token = response.Token; _status = "Вход выполнен. Подключитесь к миру."; });
            }
            catch (Exception ex) { Enqueue(() => _status = ex.Message); }
            finally { Enqueue(() => _busy = false); }
        }

        private async Task ConnectAsync()
        {
            if (_connectionState != WorldConnectionState.Closed || _busy || string.IsNullOrEmpty(_token))
                return;

            _busy = true;
            _connectionState = WorldConnectionState.Connecting;
            _frame = null;
            _views.Clear();
            _hasMovementBaseline = false;
            _moveSequence = 0;
            _lastConfirmedMoveSequence = 0;
            _lastMoveSentAt = 0f;
            _moveSendAccumulator = 0f;
            _pendingMoveIntents.Clear();
            _moveSendQueue.Clear();
            _moveSendInFlight = false;

            var socket = new WorldSocketClient();
            _socket = socket;
            socket.MessageReceived += json => OnSocketMessage(socket, json);
            socket.Closed += reason => Enqueue(() => HandleSocketClosed(socket, reason));

            try
            {
                // Layout v2 is required before enabling prediction: local geometry must match the server.
                var layout = await new HttpAuthClient(serverUrl).GetLayoutAsync();
                _layout = layout;
                WorldLayoutPresentationBuilder.Build(_presentationRoot, layout);
                var presentationCamera = _cameraRig != null
                    ? _cameraRig.GetComponent<Camera>() : Camera.main;
                RuralHorizonBackdrop.Build(_presentationRoot, presentationCamera, (float)layout.WorldHalf);
                await socket.ConnectAsync(serverUrl, _token, _lifetime.Token);
                Enqueue(() =>
                {
                    if (!ReferenceEquals(_socket, socket) || _connectionState != WorldConnectionState.Connecting)
                        return;

                    _connectionState = WorldConnectionState.AwaitingSnapshot;
                    _status = "WebSocket подключён. Ожидаю первый полный кадр…";
                });
            }
            catch (Exception ex)
            {
                Enqueue(() =>
                {
                    if (!ReferenceEquals(_socket, socket)) return;
                    _socket = null;
                    _connectionState = WorldConnectionState.Closed;
                    socket.Dispose();
                    _status = "Не удалось подключиться: " + ex.Message;
                });
            }
            finally { Enqueue(() => _busy = false); }
        }

        private void HandleSocketClosed(WorldSocketClient socket, string reason)
        {
            // A delayed close from an older socket must not tear down a newer session.
            if (!ReferenceEquals(_socket, socket)) return;

            _socket = null;
            _connectionState = WorldConnectionState.Closed;
            _frame = null;
            _moveSendInFlight = false;
            _pendingMoveIntents.Clear();
            _moveSendQueue.Clear();
            _moveSendInFlight = false;
            _views.Clear();
            if (_cameraRig != null) _cameraRig.SetTarget(null);
            socket.Dispose();
            _status = "Связь закрыта: " + reason;
        }

        private void OnSocketMessage(WorldSocketClient socket, string json)
        {
            try
            {
                var frame = WorldFrameParser.Parse(json);
                Enqueue(() =>
                {
                    // Ignore frames queued by a socket that has already been replaced or closed.
                    if (!ReferenceEquals(_socket, socket) || _connectionState == WorldConnectionState.Closed)
                        return;

                    _frame = frame;
                    if (frame.Alive && frame.Me != null && _tutorialPendingGoal == frame.Me.Goal)
                    {
                        if (_tutorialStep < TutorialGoals.Length && _tutorialPendingGoal == TutorialGoals[_tutorialStep])
                            _tutorialStep++;
                        _tutorialPendingGoal = null;
                    }
                    if (!frame.Alive)
                    {
                        _moveSendInFlight = false;
                        _pendingMoveIntents.Clear();
                        _moveSendQueue.Clear();
                        _moveSendAccumulator = 0f;
                        _views.Clear();
                        if (_cameraRig != null) _cameraRig.SetTarget(null);
                        _socket = null;
                        _connectionState = WorldConnectionState.Closed;
                        socket.Dispose();
                        _status = frame.Message ?? "Персонаж покинул мир.";
                    }
                    else
                    {
                        _connectionState = WorldConnectionState.Active;
                        _views.Apply(frame);
                        if (frame.Me.Position != null) ReconcileMovement(frame.Me.Position);
                        if (_cameraRig != null)
                        {
                            _cameraRig.SetTarget(_views.PlayerTransform);
                            _cameraRig.SetFirstPerson(_firstPerson);
                        }
                        _views.SetPlayerFirstPerson(_firstPerson, _cameraRig != null ? _cameraRig.transform : null);
                        _status = frame.Me.Position != null
                    ? "Подключены Layout v2 и кадр мира v1 (расширения v1.7); движение сверяется с серверной геометрией."
                    : "Получен кадр без позиции; движение отключено.";
                    }
                });
            }
            catch (Exception ex)
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                        _status = "Ошибка кадра: " + ex.Message;
                });
            }
        }



        private void UpdateJump()
        {
            // This only offsets the rendered player and camera. The authoritative root,
            // ScenePos, collision resolution and server move packets remain on the ground.
            if (Input.GetKeyDown(KeyCode.Space) && _jumpOffset <= 0.001f)
                _jumpVelocity = JumpImpulse;

            var deltaTime = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            if (_jumpOffset > 0f || _jumpVelocity > 0f)
            {
                _jumpVelocity -= JumpGravity * deltaTime;
                _jumpOffset += _jumpVelocity * deltaTime;
                if (_jumpOffset <= 0f)
                {
                    _jumpOffset = 0f;
                    _jumpVelocity = 0f;
                }
            }

            if (_views != null) _views.SetPlayerPresentationOffset(Vector3.up * _jumpOffset);
            if (_cameraRig != null) _cameraRig.SetJumpOffset(_jumpOffset);
        }

        private void UpdateMovement()
        {
            var player = _views != null ? _views.PlayerTransform : null;
            if (player == null || _socket == null || !_socket.IsConnected || _frame?.Me?.Position == null) return;

            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            input = Time.timeScale <= 0f ? Vector2.zero : Vector2.ClampMagnitude(input, 1f);

            // Derive movement from the camera's actual planar view basis. Projecting the
            // basis onto the ground avoids Euler-angle decomposition surprises when the
            // camera is pitched, while preserving camera-relative WASD in both view modes.
            var viewTransform = _cameraRig != null ? _cameraRig.transform : Camera.main != null ? Camera.main.transform : player;
            var forward = Vector3.ProjectOnPlane(viewTransform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Quaternion.Euler(0f, viewTransform.eulerAngles.y, 0f) * Vector3.forward;
            forward.Normalize();
            var right = Vector3.ProjectOnPlane(viewTransform.right, Vector3.up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(Vector3.up, forward);
            right.Normalize();

            var worldMove = forward * input.y + right * input.x;
            if (worldMove.sqrMagnitude > 1f) worldMove.Normalize();

            _movementInputDebug = input;
            _movementDirectionDebug = worldMove;
            _movementCameraYawDebug = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            _movementPlayerYawDebug = player.eulerAngles.y;
            _movementServerFacingDebug = _frame.Me.Position.Facing;

            var moving = worldMove.sqrMagnitude > 0.001f;
            var runRequested = moving && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            var canRun = runRequested && GetPredictedStamina() > 0f;
            var speed = canRun ? 3.2f : 1.4f;

            if (moving)
            {
                var movementDelta = Mathf.Clamp(Time.deltaTime, 0f, 0.25f);
                var next = player.position + worldMove.normalized * speed * movementDelta;
                var origin = new Vector2(player.position.x, player.position.z);
                var desired = new Vector2(next.x, next.z);
                var flat = _layout != null
                    ? _layout.ResolveMove(origin, desired)
                    : Vector2.ClampMagnitude(desired, 29.5f);
                player.position = new Vector3(flat.x, 0f, flat.y);

                if (!_firstPerson)
                {
                    var facing = Mathf.Atan2(worldMove.z, worldMove.x) * Mathf.Rad2Deg;
                    var targetRotation = Quaternion.Euler(0f, EntityViewRegistry.FacingToUnityYaw(facing), 0f);
                    player.rotation = Quaternion.RotateTowards(player.rotation, targetRotation, 720f * Time.deltaTime);
                }
            }

            var motion = player.GetComponent<EntityMotion>();
            if (motion != null) motion.SetLocomotion(moving ? (canRun ? 1f : 0.58f) : 0f);

            ApplyMovementCorrection(player);

            _moveSendAccumulator += Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
            if (_moveSendAccumulator < Mathf.Max(0.02f, moveSendInterval))
                return;

            var elapsed = _lastMoveSentAt <= 0f
                ? Mathf.Max(0.001f, _moveSendAccumulator)
                : Mathf.Clamp(Time.unscaledTime - _lastMoveSentAt, 0.001f, 0.25f);
            _moveSendAccumulator = 0f;
            _lastMoveSentAt = Time.unscaledTime;

            var facingDegrees = Mathf.Repeat(90f - player.eulerAngles.y, 360f);
            _moveSequence++;
            var intent = new PendingMoveIntent
            {
                Sequence = _moveSequence,
                Mx = worldMove.x,
                My = worldMove.z,
                Run = runRequested,
                Face = facingDegrees,
                DeltaTime = elapsed
            };
            _pendingMoveIntents.Add(intent);
            _moveSendQueue.Enqueue(intent);
            StartNextMoveSend();
        }

        private void ApplyMovementCorrection(Transform player)
        {
            if (_movementCorrectionRemaining <= 0f) return;
            var portion = Mathf.Clamp01(Time.deltaTime / _movementCorrectionRemaining);
            var correction = _movementCorrection * portion;
            player.position += correction;
            _movementCorrection -= correction;
            _movementCorrectionRemaining = Mathf.Max(0f, _movementCorrectionRemaining - Time.deltaTime);
        }

        private void ReconcileMovement(ScenePositionFrame position)
        {
            if (position == null || _views?.PlayerTransform == null) return;
            _serverStamina = Mathf.Clamp01((float)position.Stamina);
            if (!_hasMovementBaseline)
            {
                _moveSequence = position.Sequence;
                _lastConfirmedMoveSequence = position.Sequence;
                _hasMovementBaseline = true;
                _views.PlayerTransform.position = EntityViewRegistry.ToUnityPosition(position);
                _views.PlayerTransform.rotation = Quaternion.Euler(0f,
                    EntityViewRegistry.FacingToUnityYaw(position.Facing), 0f);
                return;
            }
            if (position.Sequence <= _lastConfirmedMoveSequence) return;
            _lastConfirmedMoveSequence = position.Sequence;

            // Drop inputs already represented by the authoritative snapshot, then replay
            // newer local intents over that canonical position before smoothing the correction.
            for (var i = _pendingMoveIntents.Count - 1; i >= 0; i--)
                if (_pendingMoveIntents[i].Sequence <= position.Sequence)
                    _pendingMoveIntents.RemoveAt(i);

            var replayedPosition = EntityViewRegistry.ToUnityPosition(position);
            var replayedStamina = _serverStamina;
            var replayedFacing = position.Facing;
            for (var i = 0; i < _pendingMoveIntents.Count; i++)
            {
                var intent = _pendingMoveIntents[i];
                PredictServerMove(ref replayedPosition, ref replayedStamina, intent, _layout);
                replayedFacing = intent.Face;
            }

            var error = replayedPosition - _views.PlayerTransform.position;
            if (error.sqrMagnitude > 0.0001f)
            {
                _movementCorrection = error;
                _movementCorrectionRemaining = 0.18f;
            }
            _views.PlayerTransform.rotation = Quaternion.Euler(
                0f, EntityViewRegistry.FacingToUnityYaw(replayedFacing), 0f);
        }


        private float GetPredictedStamina()
        {
            var stamina = _serverStamina;
            var unusedPosition = Vector3.zero;
            for (var i = 0; i < _pendingMoveIntents.Count; i++)
                PredictServerMove(ref unusedPosition, ref stamina, _pendingMoveIntents[i], _layout);
            return stamina;
        }

        private static void PredictServerMove(ref Vector3 position, ref float stamina, PendingMoveIntent intent, WorldLayoutFrame layout)
        {
            var dt = Mathf.Clamp(intent.DeltaTime, 0f, 0.25f);
            var direction = new Vector2(intent.Mx, intent.My);
            var length = direction.magnitude;
            if (length > 0.000001f)
            {
                direction /= length;
                var canRun = intent.Run && stamina > 0f;
                var speed = canRun ? 3.2f : 1.4f;
                if (canRun) stamina = Mathf.Max(0f, stamina - 0.18f * dt);
                else stamina = Mathf.Min(1f, stamina + 0.08f * dt);

                var origin = new Vector2(position.x, position.z);
                var desired = origin + direction * speed * dt;
                var flat = layout != null
                    ? layout.ResolveMove(origin, desired)
                    : Vector2.ClampMagnitude(desired, 29.5f);
                position.x = flat.x;
                position.z = flat.y;
            }
            else
            {
                stamina = Mathf.Min(1f, stamina + 0.08f * dt);
            }
        }

        private void StartNextMoveSend()
        {
            if (_moveSendInFlight || _moveSendQueue.Count == 0 || _socket == null ||
                !_socket.IsConnected || _connectionState != WorldConnectionState.Active)
                return;

            var intent = _moveSendQueue.Dequeue();
            _moveSendInFlight = true;
            _ = SendMoveAsync(intent, _socket);
        }

        private async Task SendMoveAsync(PendingMoveIntent intent, WorldSocketClient socket)
        {
            Exception failure = null;
            try
            {
                await socket.SendMoveAsync(intent.Mx, intent.My, intent.Run, intent.Face,
                    intent.DeltaTime, intent.Sequence, _lifetime.Token);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Enqueue(() =>
            {
                if (!ReferenceEquals(_socket, socket)) return;

                if (failure != null)
                {
                    // The failed packet and any later queued inputs were never confirmed by
                    // the transport. Drop that tail and let the next server snapshot correct prediction.
                    _pendingMoveIntents.RemoveAll(item => item.Sequence >= intent.Sequence);
                    _moveSendQueue.Clear();
                    _status = "Ошибка отправки движения: " + failure.Message;
                }

                _moveSendInFlight = false;
                StartNextMoveSend();
            });
        }

        private async Task SendGoalAsync(string goal)
        {
            var socket = _socket;
            try
            {
                if (_connectionState != WorldConnectionState.Active || socket == null)
                    throw new InvalidOperationException("Мир ещё не подключён.");
                await socket.SendGoalAsync(goal, _lifetime.Token);
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                        _status = "Намерение отправлено: " + goal;
                });
            }
            catch (Exception ex)
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                        _status = "Не удалось отправить намерение: " + ex.Message;
                });
            }
        }

        private void DisconnectWorld(string status)
        {
            var socket = _socket;
            _socket = null;
            _connectionState = WorldConnectionState.Closed;
            _frame = null;
            _goalToSend = null;
            _moveSendInFlight = false;
            _pendingMoveIntents.Clear();
            _moveSendQueue.Clear();
            _moveSendAccumulator = 0f;
            _movementCorrection = Vector3.zero;
            _movementCorrectionRemaining = 0f;
            _jumpOffset = 0f;
            _jumpVelocity = 0f;
            _cameraRig?.SetJumpOffset(0f);
            _views.Clear();
            _tutorialPendingGoal = null;
            if (_cameraRig != null) _cameraRig.SetTarget(null);
            if (_firstPerson) SetCameraMode(false);
            socket?.Dispose();
            _status = status;
        }

        private void Logout()
        {
            DisconnectWorld("Вы вышли из аккаунта.");
            _token = null;
        }

        private bool ValidateCredentials()
        {
            if (string.IsNullOrWhiteSpace(_login) || string.IsNullOrEmpty(_password) || _password.Length < 4)
            {
                _status = "Введите логин и пароль не короче 4 символов.";
                return false;
            }
            return true;
        }

        private void Enqueue(Action action) => _mainThread.Enqueue(action);

        private void SetupPresentationScene()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var go = new GameObject("Main Camera");
                go.transform.SetParent(transform, false);
                go.tag = "MainCamera";
                camera = go.AddComponent<Camera>();
            }

            camera.transform.position = new Vector3(0, 22, -28);
            camera.transform.rotation = Quaternion.Euler(35, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.58f, 0.70f, 0.78f);
            camera.fieldOfView = 48f;
            _cameraRig = camera.GetComponent<PresentationCameraRig>();
            _interactionProbe = camera.GetComponent<WorldInteractionProbe>();
            if (_interactionProbe == null)
                _interactionProbe = camera.gameObject.AddComponent<WorldInteractionProbe>();
            if (_cameraRig == null)
            {
                _cameraRig = camera.gameObject.AddComponent<PresentationCameraRig>();
                _createdCameraRig = true;
            }

            if (FindObjectOfType<Light>() == null)
            {
                var lightObject = new GameObject("Directional Light");
                lightObject.transform.SetParent(transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;
                light.color = new Color(1f, 0.91f, 0.76f);
                light.transform.rotation = Quaternion.Euler(48, -32, 0);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.58f, 0.68f, 0.70f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 72f;
            RenderSettings.fogEndDistance = 235f;
        }

        private void OnDestroy()
        {
            WorldPoi.Selected -= OnPoiSelected;
            WorldNpc.Selected -= OnNpcSelected;
            _lifetime.Cancel();
            if (_cameraRig != null) _cameraRig.SetTarget(null);
            if (_createdCameraRig && _cameraRig != null)
                Destroy(_cameraRig);
            _socket?.Dispose();
            _socket = null;
            _connectionState = WorldConnectionState.Closed;
            _lifetime.Dispose();
            WorldEnvironmentBuilder.Cleanup();
            WorldLayoutPresentationBuilder.Cleanup();
            ProceduralVillageBuilder.Cleanup();
        }
    }
}
