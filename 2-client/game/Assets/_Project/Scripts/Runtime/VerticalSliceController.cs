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
        [SerializeField] private string serverUrl = "http://127.0.0.1:8090";
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private HttpAuthClient _auth;
        private WorldSocketClient _socket;
        private EntityViewRegistry _views;
        private string _login = "";
        private string _password = "";
        private string _status = "Введите учётные данные сервера.";
        private string _token;
        private string _goalToSend;
        private WorldFrame _frame;
        private bool _busy;
        private bool _registered;

        private void Awake()
        {
            _auth = new HttpAuthClient(serverUrl);
            var root = new GameObject("EntityViews").transform;
            _views = new EntityViewRegistry(root);
            SetupCamera();
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var action)) action();
            if (!string.IsNullOrEmpty(_goalToSend))
            {
                var goal = _goalToSend;
                _goalToSend = null;
                _ = SendGoalAsync(goal);
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 360, 350), "ПОГРАНИЧЬЕ — Unity v1 Vertical Slice");
            GUILayout.BeginArea(new Rect(24, 42, 336, 310));
            GUILayout.Label("Сервер");
            serverUrl = GUILayout.TextField(serverUrl);
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
            else if (_frame == null || !_frame.Alive)
            {
                if (GUILayout.Button("Подключиться к миру") && !_busy) _ = ConnectAsync();
                if (GUILayout.Button("Выйти")) Disconnect();
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
                GUILayout.Label("Отправить намерение (сервер решает результат):");
                GUILayout.BeginHorizontal();
                GoalButton("work"); GoalButton("eat"); GoalButton("rest");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GoalButton("socialize"); GoalButton("seek_home"); GoalButton("wander");
                GUILayout.EndHorizontal();
                if (GUILayout.Button("leave")) _goalToSend = "leave";
                if (GUILayout.Button("Отключиться")) Disconnect();
            }
            GUILayout.Space(8);
            GUILayout.Label(_status, GUILayout.Height(56));
            GUILayout.EndArea();
        }

        private void GoalButton(string goal)
        {
            if (GUILayout.Button(goal)) _goalToSend = goal;
        }

        private async Task RegisterAsync()
        {
            if (!ValidateCredentials()) return;
            _busy = true;
            try
            {
                var response = await _auth.RegisterAsync(_login, _password);
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
                var response = await _auth.LoginAsync(_login, _password);
                if (!response.Ok || string.IsNullOrEmpty(response.Token))
                    throw new InvalidOperationException(response.Message ?? "Вход не выполнен.");
                Enqueue(() => { _token = response.Token; _status = "Вход выполнен. Подключитесь к миру."; });
            }
            catch (Exception ex) { Enqueue(() => _status = ex.Message); }
            finally { Enqueue(() => _busy = false); }
        }

        private async Task ConnectAsync()
        {
            _busy = true;
            try
            {
                _socket?.Dispose();
                _socket = new WorldSocketClient();
                _socket.MessageReceived += OnSocketMessage;
                _socket.Closed += reason => Enqueue(() => _status = "Связь закрыта: " + reason);
                await _socket.ConnectAsync(serverUrl, _token, _lifetime.Token);
                Enqueue(() => _status = "WebSocket подключён. Ожидаю первый полный кадр…");
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось подключиться: " + ex.Message); }
            finally { Enqueue(() => _busy = false); }
        }

        private void OnSocketMessage(string json)
        {
            try
            {
                var frame = WorldFrameParser.Parse(json);
                Enqueue(() =>
                {
                    _frame = frame;
                    if (!frame.Alive)
                    {
                        _views.Clear();
                        _status = frame.Message ?? "Персонаж покинул мир.";
                    }
                    else
                    {
                        _views.Apply(frame);
                        _status = "Получен полный кадр v1.";
                    }
                });
            }
            catch (Exception ex) { Enqueue(() => _status = "Ошибка кадра: " + ex.Message); }
        }

        private async Task SendGoalAsync(string goal)
        {
            try
            {
                if (_socket == null) throw new InvalidOperationException("Нет подключения к миру.");
                await _socket.SendGoalAsync(goal, _lifetime.Token);
                Enqueue(() => _status = "Намерение отправлено: " + goal);
            }
            catch (Exception ex) { Enqueue(() => _status = "Не удалось отправить намерение: " + ex.Message); }
        }

        private void Disconnect()
        {
            _socket?.Dispose();
            _socket = null;
            _token = null;
            _frame = null;
            _views.Clear();
            _status = "Отключено.";
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

        private void SetupCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var go = new GameObject("Main Camera");
                camera = go.AddComponent<Camera>();
                go.tag = "MainCamera";
            }
            camera.transform.position = new Vector3(0, 8, -12);
            camera.transform.rotation = Quaternion.Euler(28, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.17f);
            if (FindObjectOfType<Light>() == null)
            {
                var light = new GameObject("Directional Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "TemporaryPresentationGround";
            floor.transform.localScale = new Vector3(2, 1, 2);
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _socket?.Dispose();
            _lifetime.Dispose();
        }
    }
}
