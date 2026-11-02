using System;
using System.Collections.Concurrent;
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
        private Transform _presentationRoot;
        private string _login = "";
        private string _password = "";
        private string _status = "Введите учётные данные сервера.";
        private string _token;
        private string _goalToSend;
        private WorldFrame _frame;
        private WorldConnectionState _connectionState = WorldConnectionState.Closed;
        private bool _busy;

        private void Awake()
        {
            _presentationRoot = new GameObject("PresentationWorld").transform;
            var entityRoot = new GameObject("EntityViews").transform;
            entityRoot.SetParent(_presentationRoot, false);
            _views = new EntityViewRegistry(entityRoot);
            SetupPresentationScene();
            ProceduralVillageBuilder.Build(_presentationRoot);
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var action)) action();

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
                GUILayout.Space(6);
                GUILayout.Space(8);
                GUILayout.Label("ПЕРВЫЕ ШАГИ");
                GUILayout.Label(TutorialHint(), GUILayout.Height(38));
                GUILayout.Label("Выберите занятие — сервер выполнит действие по правилам мира:");
                if (GUILayout.Button("🍺 Таверна — поговорить")) _goalToSend = "socialize";
                if (GUILayout.Button("🔨 Кузница / огород — работать")) _goalToSend = "work";
                if (GUILayout.Button("🍞 Рынок — поесть")) _goalToSend = "eat";
                if (GUILayout.Button("🏠 Дом — отдохнуть")) _goalToSend = "rest";
                if (GUILayout.Button("Отключиться от мира")) DisconnectWorld("Отключено от мира. Можно подключиться снова.");
                if (GUILayout.Button("Выйти из аккаунта")) Logout();
            }

            GUILayout.Space(8);
            GUILayout.Label(_status, GUILayout.Height(56));
            GUILayout.EndArea();
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

            switch (_frame.Me.Goal)
            {
                case "socialize":
                    return "Вы выбрали общение. Посмотрите, как меняются настроение и последнее действие персонажа.";
                case "work":
                    return "Вы выбрали работу. Результат зависит от профессии, запасов и правил симуляции.";
                case "eat":
                    return "Вы выбрали еду. Следите за показателем голода и запасами поселения.";
                case "rest":
                    return "Вы выбрали отдых. Энергия персонажа восстанавливается по правилам сервера.";
                default:
                    return "Начните с таверны: выберите общение, затем попробуйте работу, еду и отдых.";
            }
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
                        if (_cameraRig != null) _cameraRig.SetTarget(_views.PlayerTransform);
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
            _views.Clear();
            if (_cameraRig != null) _cameraRig.SetTarget(null);
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
                go.tag = "MainCamera";
                camera = go.AddComponent<Camera>();
            }

            camera.transform.position = new Vector3(0, 8, -10);
            camera.transform.rotation = Quaternion.Euler(35, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.58f, 0.70f, 0.78f);
            camera.fieldOfView = 48f;
            _cameraRig = camera.GetComponent<PresentationCameraRig>();
            if (_cameraRig == null) _cameraRig = camera.gameObject.AddComponent<PresentationCameraRig>();

            if (FindObjectOfType<Light>() == null)
            {
                var light = new GameObject("Directional Light").AddComponent<Light>();
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
            _lifetime.Cancel();
            _socket?.Dispose();
            _socket = null;
            _connectionState = WorldConnectionState.Closed;
            _lifetime.Dispose();
            ProceduralVillageBuilder.Cleanup();
        }
    }
}
