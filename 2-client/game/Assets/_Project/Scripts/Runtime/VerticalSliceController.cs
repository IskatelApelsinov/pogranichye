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
        private WorldConnectionState _connectionState = WorldConnectionState.Closed;
        private bool _busy;
        private int _tutorialStep;
        private string _tutorialPendingGoal;
        private bool _firstPerson;
        private Vector2 _hudScroll;
        [SerializeField] private float moveSendInterval = 0.05f;
        private float _moveSendAccumulator;
        private float _lastMoveSentAt;
        private long _moveSequence;
        private long _lastConfirmedMoveSequence;
        private bool _hasMovementBaseline;
        private bool _moveSendInFlight;
        private readonly List<PendingMoveIntent> _pendingMoveIntents = new List<PendingMoveIntent>();
        private Vector3 _movementCorrection;
        private float _movementCorrectionRemaining;
        private float _serverStamina = 1f;

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
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var action)) action();
            if (_interactionProbe != null)
                _interactionProbe.enabled = _connectionState == WorldConnectionState.Active;

            if (_connectionState == WorldConnectionState.Active)
                UpdateMovement();

            if (_connectionState == WorldConnectionState.Active && !string.IsNullOrEmpty(_goalToSend))
            {
                var goal = _goalToSend;
                _goalToSend = null;
                _ = SendGoalAsync(goal);
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 430, 510), "ПОГРАНИЧЬЕ — первая деревня");
            GUILayout.BeginArea(new Rect(24, 42, 406, 470));
            _hudScroll = GUILayout.BeginScrollView(_hudScroll, GUILayout.Width(406), GUILayout.Height(470));
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
                GUILayout.Label($"Запал бега: {_serverStamina * 100f:0}% | WASD — движение, Shift — бег");
                GUILayout.Space(6);
                GUILayout.Space(8);
                if (GUILayout.Button(_firstPerson ? "Переключить на вид от третьего лица" : "Переключить на вид от первого лица"))
                    SetCameraMode(!_firstPerson);
                GUILayout.Label(_firstPerson
                    ? "WASD — движение; Shift — бег; мышь — осмотр; ПКМ — альтернативный режим предмета/инструмента."
                    : "Мышь — обзор камеры; колесо — зум; ПКМ — альтернативный режим предмета/инструмента.");
                GUILayout.Space(4);
                GUILayout.Label("ПЕРВЫЕ ШАГИ");
                GUILayout.Label(TutorialHint(), GUILayout.Height(38));
                GUILayout.Label("Выберите занятие — сервер выполнит действие по правилам мира:");
                if (GUILayout.Button("🍺 Таверна — поговорить")) RequestTutorialGoal("socialize");
                if (GUILayout.Button("🔨 Кузница / огород — работать")) RequestTutorialGoal("work");
                if (GUILayout.Button("🍞 Рынок — поесть")) RequestTutorialGoal("eat");
                if (GUILayout.Button("🏠 Дом — отдохнуть")) RequestTutorialGoal("rest");
                if (GUILayout.Button("Отключиться от мира")) DisconnectWorld("Отключено от мира. Можно подключиться снова.");
                if (GUILayout.Button("Выйти из аккаунта")) Logout();
            }

            GUILayout.Space(8);
            GUILayout.Label(_status, GUILayout.Height(56));
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (_firstPerson && _connectionState == WorldConnectionState.Active)
            {
                var previousColor = GUI.color;
                GUI.color = Color.white;
                GUI.Label(new Rect(Screen.width * 0.5f - 8f, Screen.height * 0.5f - 12f, 16f, 24f), "·");
                GUI.color = previousColor;
            }
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
                return "Знакомство завершено. Теперь наблюдайте за жителями и выбирайте занятия самостоятельно.";
            if (!string.IsNullOrEmpty(_tutorialPendingGoal))
                return "Ожидаем подтверждение сервера. Последнее действие: " + _frame.Me.Last;

            switch (_tutorialStep)
            {
                case 0: return "Шаг 1/4 — зайдите в таверну и выберите общение. Познакомьтесь с жителями.";
                case 1: return "Шаг 2/4 — попробуйте работу. Профессия персонажа влияет на результат.";
                case 2: return "Шаг 3/4 — выберите еду. Следите за голодом и запасами поселения.";
                default: return "Шаг 4/4 — отдохните. Энергия восстанавливается по правилам сервера.";
            }
        }

        private void SetCameraMode(bool firstPerson)
        {
            _firstPerson = firstPerson;
            if (_cameraRig != null) _cameraRig.SetFirstPerson(firstPerson);
            if (_views != null) _views.SetPlayerFirstPerson(firstPerson);
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

            RequestTutorialGoal("socialize");
            _status = "Вы обратились к " + npc.DisplayName +
                ". Отправляю общую цель общения; адресный диалог пока не входит в v1.";
        }

        private void RequestTutorialGoal(string goal)
        {
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

            var socket = new WorldSocketClient();
            _socket = socket;
            socket.MessageReceived += json => OnSocketMessage(socket, json);
            socket.Closed += reason => Enqueue(() => HandleSocketClosed(socket, reason));

            try
            {
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
                        _views.SetPlayerFirstPerson(_firstPerson);
                        _status = "Получен полный кадр v1.";
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


        private void UpdateMovement()
        {
            var player = _views != null ? _views.PlayerTransform : null;
            if (player == null || _socket == null || !_socket.IsConnected || _frame?.Me?.Position == null) return;

            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            input = Time.timeScale <= 0f ? Vector2.zero : Vector2.ClampMagnitude(input, 1f);

            var camera = Camera.main;
            var forward = camera != null ? camera.transform.forward : player.forward;
            var right = camera != null ? camera.transform.right : player.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            var worldMove = forward * input.y + right * input.x;
            if (worldMove.sqrMagnitude > 1f) worldMove.Normalize();

            var moving = worldMove.sqrMagnitude > 0.001f;
            var runRequested = moving && Input.GetKey(KeyCode.LeftShift);
            var canRun = runRequested && GetPredictedStamina() > 0f;
            var speed = canRun ? 3.2f : 1.4f;

            if (moving)
            {
                var next = player.position + worldMove.normalized * speed * Time.deltaTime;
                var flat = new Vector2(next.x, next.z);
                if (flat.magnitude > 29.5f) flat = flat.normalized * 29.5f;
                player.position = new Vector3(flat.x, 0f, flat.y);

                var facing = Mathf.Atan2(worldMove.z, worldMove.x) * Mathf.Rad2Deg;
                player.rotation = Quaternion.Euler(0f, EntityViewRegistry.FacingToUnityYaw(facing), 0f);
            }

            var motion = player.GetComponent<EntityMotion>();
            if (motion != null) motion.SetLocomotion(moving ? (canRun ? 1f : 0.58f) : 0f);

            ApplyMovementCorrection(player);

            _moveSendAccumulator += Time.unscaledDeltaTime;
            if (_moveSendAccumulator < Mathf.Max(0.02f, moveSendInterval) || _moveSendInFlight)
                return;

            _moveSendAccumulator = 0f;
            var elapsed = _lastMoveSentAt <= 0f
                ? Mathf.Max(0.001f, _moveSendAccumulator)
                : Mathf.Max(0.001f, Time.unscaledTime - _lastMoveSentAt);
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
            _moveSendInFlight = true;
            _ = SendMoveAsync(intent);
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
                PredictServerMove(ref replayedPosition, ref replayedStamina, intent);
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
                PredictServerMove(ref unusedPosition, ref stamina, _pendingMoveIntents[i]);
            return stamina;
        }

        private static void PredictServerMove(ref Vector3 position, ref float stamina, PendingMoveIntent intent)
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

                position.x += direction.x * speed * dt;
                position.z += direction.y * speed * dt;
                var flat = new Vector2(position.x, position.z);
                if (flat.magnitude > 29.5f) flat = flat.normalized * 29.5f;
                position.x = flat.x;
                position.z = flat.y;
            }
            else
            {
                stamina = Mathf.Min(1f, stamina + 0.08f * dt);
            }
        }

        private async Task SendMoveAsync(PendingMoveIntent intent)
        {
            var socket = _socket;
            try
            {
                await socket.SendMoveAsync(intent.Mx, intent.My, intent.Run, intent.Face,
                    intent.DeltaTime, intent.Sequence, _lifetime.Token);
            }
            catch (Exception ex)
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                    {
                        _pendingMoveIntents.RemoveAll(item => item.Sequence == intent.Sequence);
                        _status = "Ошибка отправки движения: " + ex.Message;
                    }
                });
            }
            finally
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                        _moveSendInFlight = false;
                });
            }
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
            _moveSendAccumulator = 0f;
            _movementCorrection = Vector3.zero;
            _movementCorrectionRemaining = 0f;
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

            camera.transform.position = new Vector3(0, 8, -10);
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
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 65f;
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
            ProceduralVillageBuilder.Cleanup();
        }
    }
}
