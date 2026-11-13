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

        private enum HudTab
        {
            Character,
            Inventory,
            Journal,
            Map,
            Settings
        }

        [SerializeField] private string serverUrl = "http://127.0.0.1:8090";
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private WorldSocketClient _socket;
        private CoreGameplay _core; // protocol v1.10 gameplay: combat, weaves, requests, crafting, journeys, party
        private EntityViewRegistry _views;
        private PresentationCameraRig _cameraRig;
        private ClientInput _input;
        private WorldInteractionProbe _interactionProbe;
        private bool _createdCameraRig;
        private Transform _presentationRoot;
        private string _login = "";
        private string _password = "";
        private string _status = "Введите учётные данные сервера.";
        private bool _showMainSettings;
        private Vector2 _mainMenuScroll;
        private float _masterVolume = 0.85f;
        private float _sfxVolume = 0.9f;
        private float _ambienceVolume = 0.75f;
        private float _mouseSensitivity = 0.035f;
        private bool _invertMouseY;
        private float _shadowDistance = 120f;
        private float _lodBias = 1f;
        private int _antiAliasing = 2;
        private bool _vSync = true;
        private int _targetFrameRate = 60;
        private WorldAudioDirector _audioDirector;
        private VillageSoundscape _villageSoundscape;
        [Serializable] private sealed class ChatEventData
        {
            public string type;
            public long seq;
            public long sent_at;
            public string channel;
            public string from_id;
            public string from_name;
            public string from_desc;
            public string text;
        }
        private readonly List<string> _chatHistory = new List<string>();
        private string _chatInput = "";
        private bool _chatOpen;
        private bool _chatFocusPending;
        private Vector2 _chatScroll;
        private VoiceChatController _voiceChat;
        private string _token;
        private string _goalToSend;
        private WorldFrame _frame;
        private WorldLayoutFrame _layout;
        // v1.19 world-streaming foundation. The manifest is metadata; terrain is cached one chunk at a time.
        private WorldManifestFrame _worldManifest;
        private TerrainChunkFrame _terrainChunk;
        private string _terrainChunkRequestedId;
        private string _terrainAppliedSettlementId;
        private double _terrainAppliedAnchorEast = double.NaN;
        private double _terrainAppliedAnchorNorth = double.NaN;
        private double _terrainAppliedAnchorUp = double.NaN;
        // v1.19: id of the settlement whose layout is currently built; null means the tutorial default.
        private string _layoutSettlementId;
        private string _layoutRequestedFor;
        private WorldConnectionState _connectionState = WorldConnectionState.Closed;
        private bool _busy;
        private int _tutorialStep;
        private string _tutorialNpcTargetId;
        private string _pendingTalkTargetId;
        private string _confirmedTalkTargetId;
        private int _talkRequestSerial;
        private int _pendingTalkRequestSerial;
        private float _pendingTalkDeadline;
        private string _talkFeedback;
        private bool _talkConfirmationTimedOut;
        private bool _drinkRequestPending;
        private const string DrinkRequestLabelPrefix = "Совместная кружка:";
        private const string InventoryRequestLabelPrefix = "Инвентарь:";
        private const string WorldActionLabelPrefix = "Действие мира:";
        private enum ActivityMiniGame { None, Woodcutting, Fishing }
        private ActivityMiniGame _activityMiniGame;
        private float _activityMiniGameStartedAt;
        private int _woodcuttingGoodHits;
        private string _activityMiniGameFeedback;
        private Vector2 _dialogueScroll;
        private GUIStyle _dialogueWhoStyle;
        private GUIStyle _dialogueLineStyle;
        private GUIStyle _dialogueOptionStyle;
        private GUIStyle _dialogueHintStyle;
        private const float TalkConfirmationTimeoutSeconds = 48f;
        private const string DialogueRequestLabel = "Реплика NPC";
        private const string TalkRequestLabelPrefix = "Разговор NPC:";
        private bool _firstPerson;
        private bool _hudCollapsed;
        private HudTab _hudTab = HudTab.Character;
        private bool _movementDebugVisible;
        private Vector2 _movementInputDebug;
        private Vector2 _movementKeyboardDebug;
        private Vector2 _movementRawAxesDebug;
        private string _pressedKeysDebug = "none";
        private Vector3 _movementDirectionDebug;
        private float _movementCameraYawDebug;
        private float _movementPlayerYawDebug;
        private float _movementServerFacingDebug;
        private Vector2 _hudScroll;
        private GUIStyle _mapPoiStyle;
        private GUIStyle _mapPoiLabelStyle;
        private GUIStyle _mapNeighborStyle;
        private GUIStyle _mapPlayerStyle;
        private readonly List<string> _sessionJournal = new List<string>();
        private string _lastJournalAction;
        private string _lastJournalTaskSignature;
        private string _lastJournalResultId;
        [SerializeField] private float moveSendInterval = 0.05f;
        private float _moveSendAccumulator;
        private float _lastMoveSentAt;
        private long _moveSequence;
        private long _lastConfirmedMoveSequence;
        private bool _hasMovementBaseline;
        private bool _moveSendInFlight;
        private const int MaxQueuedMoveIntents = 8;
        private int _discardedMoveIntentCount;
        private static readonly Func<PendingMoveIntent, long> MoveIntentSequence = intent => intent.Sequence;
        private static readonly Func<PendingMoveIntent, bool> MoveIntentHasMovement =
            intent => intent.Mx * intent.Mx + intent.My * intent.My > 0.000001f;
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
            public bool Fast;
            public float Face;
            public float DeltaTime;
        }

        private void Awake()
        {
            _voiceChat = GetComponent<VoiceChatController>();
            if (_voiceChat == null) _voiceChat = gameObject.AddComponent<VoiceChatController>();
            _input = GetComponent<ClientInput>();
            if (_input == null) _input = gameObject.AddComponent<ClientInput>();
            _presentationRoot = new GameObject("PresentationWorld").transform;
            _presentationRoot.SetParent(transform, false);
            var entityRoot = new GameObject("EntityViews").transform;
            entityRoot.SetParent(_presentationRoot, false);
            _views = new EntityViewRegistry(entityRoot);
            WorldPoi.Selected += OnPoiSelected;
            WorldNpc.Selected += OnNpcSelected;
            SetupPresentationScene();
            ProceduralVillageBuilder.Build(_presentationRoot);
            _villageSoundscape = _presentationRoot.GetComponentInChildren<VillageSoundscape>();
            WorldEnvironmentBuilder.Build(_presentationRoot, _cameraRig != null ? _cameraRig.GetComponent<Camera>() : Camera.main);
            _audioDirector = _presentationRoot.gameObject.AddComponent<WorldAudioDirector>();
            _audioDirector.Initialize();
            _masterVolume = PlayerPrefs.GetFloat("Pogranichye.MasterVolume", 0.85f);
            _sfxVolume = PlayerPrefs.GetFloat("Pogranichye.SfxVolume", 0.9f);
            _ambienceVolume = PlayerPrefs.GetFloat("Pogranichye.AmbienceVolume", 0.75f);
            _mouseSensitivity = PlayerPrefs.GetFloat("Pogranichye.MouseSensitivity", 0.035f);
            _invertMouseY = PlayerPrefs.GetInt("Pogranichye.InvertMouseY", 0) != 0;
            _shadowDistance = PlayerPrefs.GetFloat("Pogranichye.ShadowDistance", 120f);
            _lodBias = PlayerPrefs.GetFloat("Pogranichye.LodBias", 1f);
            _antiAliasing = PlayerPrefs.GetInt("Pogranichye.AntiAliasing", 2);
            _vSync = PlayerPrefs.GetInt("Pogranichye.VSync", 1) != 0;
            _targetFrameRate = PlayerPrefs.GetInt("Pogranichye.TargetFrameRate", 60);
            ApplyGraphicsSettings();
            _cameraRig?.SetLookSettings(_mouseSensitivity, _invertMouseY);
            AudioListener.volume = _masterVolume;
            _audioDirector.SetVolumes(_sfxVolume, _ambienceVolume);
            _villageSoundscape?.SetAmbienceVolume(_ambienceVolume);
            _core = gameObject.AddComponent<CoreGameplay>();
            _core.Bind(SendCorePayload);
            _core.BindInput(_input);
            _core.RequestResolved += OnCoreRequestResolved;
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var action)) action();
            // Keep spatial player SFX and remote-voice range checks anchored to the
            // authoritative local body, not the scene origin where the director was created.
            _audioDirector?.SetPlayerPosition(_views != null && _views.PlayerTransform != null
                ? _views.PlayerTransform.position : transform.position);
            // Keep F3 diagnostics live even on the title/login screen, before movement starts.
            if (_movementDebugVisible && _input != null)
                _pressedKeysDebug = _input.BuildPressedKeysDebug();
            if (_core != null)
                _core.SetContext(_connectionState == WorldConnectionState.Active, _firstPerson, _hudCollapsed,
                    _views != null ? _views.PlayerTransform : null, _cameraRig);
            UpdateTalkConfirmationTimeout();
            if (_connectionState != WorldConnectionState.Active)
                SetChatOpen(false);
            else if (!_chatOpen && _input != null && _input.ChatPressed)
                SetChatOpen(true);
            else if (_chatOpen && _input != null && _input.CancelPressed)
                SetChatOpen(false);

            // The title screen and chat are pointer-driven modes: never feed mouse delta
            // into the camera while either is active.
            _cameraRig?.SetMouseGestureCapture(_connectionState != WorldConnectionState.Active || _chatOpen);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = _connectionState != WorldConnectionState.Active || _chatOpen;
            if (_connectionState != WorldConnectionState.Active && _drinkRequestPending)
                _drinkRequestPending = false; // RequestGate is cleared on disconnect; do not leave social UI locked.
            if (_input != null && _input.ToggleHudPressed) _hudCollapsed = !_hudCollapsed;
            if (_input != null && _input.ToggleDiagnosticsPressed) _movementDebugVisible = !_movementDebugVisible;
            if (_input != null && _input.ToggleFullscreenPressed) ToggleFullscreen();
            if (_interactionProbe != null)
                _interactionProbe.enabled = _connectionState == WorldConnectionState.Active;

            if (_connectionState == WorldConnectionState.Active && !_chatOpen)
            {
                UpdateJump();
                UpdateMovement();
                UpdateDialogueChoiceInput();
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
            CoreGameplay.ApplyUiTheme();
            if (_connectionState != WorldConnectionState.Active || _frame == null || !_frame.Alive)
            {
                DrawTitleLoadingBackdrop();
                DrawMovementDebugOverlay();
                return;
            }
            if (_hudCollapsed)
            {
                if (GUI.Button(new Rect(12, 12, 150, 30), "Показать меню [F1]"))
                    _hudCollapsed = false;
            }
            else
            {
            var panelWidth = Mathf.Min(500f, Screen.width - 24f);
            var panelHeight = Mathf.Min(620f, Screen.height - 24f);
            GUI.Box(new Rect(12f, 12f, panelWidth, panelHeight), "ПОГРАНИЧЬЕ  /  ЖИВОЙ МИР");
            GUILayout.BeginArea(new Rect(24f, 42f, panelWidth - 24f, panelHeight - 44f));
            _hudScroll = GUILayout.BeginScrollView(_hudScroll,
                GUILayout.Width(panelWidth - 24f), GUILayout.Height(panelHeight - 44f));
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
                GUILayout.Label($"ДЕНЬ {_frame.Day}  ·  ГОД {_frame.Year}  ·  {_frame.Season}");
                if (_frame.Place != null)
                    GUILayout.Label($"⌖  {_frame.Place.Name}  ·  {_frame.Place.Tier}");

                GUILayout.BeginHorizontal();
                DrawHudTabButton("Персонаж", HudTab.Character);
                DrawHudTabButton("Инвентарь", HudTab.Inventory);
                DrawHudTabButton("Журнал", HudTab.Journal);
                DrawHudTabButton("Карта", HudTab.Map);
                DrawHudTabButton("Настройки", HudTab.Settings);
                GUILayout.EndHorizontal();
                GUILayout.Space(8f);

                switch (_hudTab)
                {
                    case HudTab.Character:
                        DrawCharacterTab();
                        break;
                    case HudTab.Inventory:
                        DrawInventoryTab();
                        break;
                    case HudTab.Journal:
                        DrawJournalTab();
                        break;
                    case HudTab.Map:
                        DrawMapTab();
                        break;
                    case HudTab.Settings:
                        DrawSettingsTab();
                        break;
                }

                GUILayout.Space(8f);
                if (GUILayout.Button("Отключиться от мира")) DisconnectWorld("Отключено от мира. Можно подключиться снова.");
                if (GUILayout.Button("Выйти из аккаунта")) Logout();
            }

            GUILayout.Space(8);
            GUILayout.Label(_status, GUILayout.Height(56));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            }

            DrawMovementDebugOverlay();
            DrawSocialOverlay();
            if (_chatOpen)
                DrawChatOverlay();
            else if (GUI.Button(new Rect(Screen.width - 124f, 12f, 112f, 28f), "Чат [T]"))
                SetChatOpen(true);
        }

        // Opaque title/loading screen hides the local fallback village while the authoritative
        // layout and first world snapshot are being fetched. It is drawn before the login panel.
        private void DrawTitleLoadingBackdrop()
        {
            var previousColor = GUI.color;
            var previousEnabled = GUI.enabled;
            GUI.color = new Color(0.035f, 0.031f, 0.027f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.11f, 0.09f, 0.07f, 0.94f);
            var panelWidth = Mathf.Min(560f, Screen.width - 36f);
            var panelHeight = Mathf.Min(_showMainSettings ? 760f : 590f, Screen.height - 36f);
            var panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            GUI.Box(panel, GUIContent.none);
            var title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp(Screen.height / 15, 34, 62), fontStyle = FontStyle.Bold };
            GUI.color = new Color(0.83f, 0.68f, 0.45f, 1f);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 24f, panel.width - 40f, 76f), "ПОГРАНИЧЬЕ", title);
            var subtitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Italic };
            GUI.color = new Color(0.79f, 0.74f, 0.66f, 1f);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 94f, panel.width - 40f, 30f), "Жизнь на краю известного мира", subtitle);

            GUILayout.BeginArea(new Rect(panel.x + 42f, panel.y + 142f, panel.width - 84f, panel.height - 164f));
            _mainMenuScroll = GUILayout.BeginScrollView(_mainMenuScroll, GUILayout.ExpandHeight(true));
            if (_showMainSettings)
            {
                GUILayout.Label("НАСТРОЙКИ");
                DrawOptionsControls();
                GUILayout.Space(10f);
                if (GUILayout.Button("Назад", GUILayout.Height(40f))) _showMainSettings = false;
            }
            else
            {
                GUILayout.Label("ПОДКЛЮЧЕНИЕ К МИРУ");
                GUILayout.Label("Адрес сервера");
                GUI.enabled = _connectionState == WorldConnectionState.Closed && !_busy;
                serverUrl = GUILayout.TextField(serverUrl, GUILayout.Height(32f));
                GUI.enabled = previousEnabled;
                if (string.IsNullOrEmpty(_token))
                {
                    GUILayout.Space(5f);
                    GUILayout.Label("Имя пользователя");
                    _login = GUILayout.TextField(_login, GUILayout.Height(32f));
                    GUILayout.Label("Пароль");
                    _password = GUILayout.PasswordField(_password, '*', GUILayout.Height(32f));
                    GUILayout.Space(8f);
                    GUI.enabled = !_busy;
                    if (GUILayout.Button(_busy ? "Подождите…" : "Войти", GUILayout.Height(42f))) _ = LoginAsync();
                    if (GUILayout.Button("Создать учётную запись", GUILayout.Height(36f))) _ = RegisterAsync();
                    GUI.enabled = previousEnabled;
                }
                else
                {
                    GUILayout.Label(ConnectionStateLabel());
                    GUI.enabled = !_busy && _connectionState == WorldConnectionState.Closed;
                    if (GUILayout.Button("ВОЙТИ В МИР", GUILayout.Height(48f))) _ = ConnectAsync();
                    GUI.enabled = previousEnabled;
                    if (_connectionState == WorldConnectionState.Connecting || _connectionState == WorldConnectionState.AwaitingSnapshot)
                    {
                        if (GUILayout.Button("Отменить подключение", GUILayout.Height(34f)))
                            DisconnectWorld("Подключение отменено.");
                    }
                    if (GUILayout.Button("Выйти из аккаунта", GUILayout.Height(32f))) Logout();
                }
                GUILayout.Space(8f);
                if (GUILayout.Button("Настройки", GUILayout.Height(34f))) _showMainSettings = true;
                if (GUILayout.Button("Выход из игры", GUILayout.Height(34f))) Application.Quit();
            }
            GUILayout.Space(8f);
            GUILayout.Label(_status, GUILayout.MinHeight(34f));
            if (_connectionState == WorldConnectionState.Connecting || _connectionState == WorldConnectionState.AwaitingSnapshot)
                GUILayout.Label(_connectionState == WorldConnectionState.Connecting ? "Устанавливаем связь…" : "Получаем состояние мира…");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.enabled = previousEnabled;
        }

        private static void ToggleFullscreen()
        {
            var enable = !Screen.fullScreen;
            Screen.fullScreenMode = enable ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.fullScreen = enable;
        }

        private void OnSocketChatMessage(WorldSocketClient socket, string json)
        {
            ChatEventData message;
            try { message = JsonUtility.FromJson<ChatEventData>(json); }
            catch { return; }
            if (message == null || message.type != "chat" || string.IsNullOrEmpty(message.text)) return;
            Enqueue(() =>
            {
                if (!ReferenceEquals(_socket, socket)) return;
                var who = !string.IsNullOrEmpty(message.from_name) ? message.from_name :
                    (!string.IsNullOrEmpty(message.from_desc) ? message.from_desc : "Незнакомец");
                var prefix = message.channel == "whisper" ? "Шёпот · " : "";
                _chatHistory.Add(prefix + who + ": " + message.text);
                while (_chatHistory.Count > 80) _chatHistory.RemoveAt(0);
                _chatScroll.y = float.MaxValue;
            });
        }

        private void DrawChatOverlay()
        {
            if (_connectionState != WorldConnectionState.Active || _frame == null || !_frame.Alive) return;
            var width = Mathf.Min(360f, Screen.width - 24f);
            var height = Mathf.Min(300f, Screen.height - 24f);
            var rect = new Rect(Screen.width - width - 12f, Screen.height - height - 12f, width, height);
            GUI.Box(rect, "Чат поблизости");
            if (GUI.Button(new Rect(rect.xMax - 30f, rect.y + 3f, 24f, 22f), "×"))
            {
                SetChatOpen(false);
                return;
            }
            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, rect.height - 38f));
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(Mathf.Max(80f, rect.height - 150f)));
            var first = Mathf.Max(0, _chatHistory.Count - 40);
            for (var i = first; i < _chatHistory.Count; i++)
                GUILayout.Label(_chatHistory[i], GUILayout.MinHeight(20f));
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("PogranichyeChatInput");
            _chatInput = GUILayout.TextField(_chatInput, GUILayout.MinWidth(100f), GUILayout.Height(28f));
            if (_chatFocusPending)
            {
                GUI.FocusControl("PogranichyeChatInput");
                _chatFocusPending = false;
            }
            var canSend = !string.IsNullOrWhiteSpace(_chatInput) && !_busy;
            GUI.enabled = canSend;
            if (GUILayout.Button("Отправить", GUILayout.Width(92f), GUILayout.Height(28f)))
            {
                var text = _chatInput.Trim();
                _chatInput = "";
                _ = SendChatMessageAsync(text);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.enabled = _voiceChat != null && _connectionState == WorldConnectionState.Active;
            if (GUILayout.Button(_voiceChat != null && _voiceChat.IsJoined ? "Выйти из голоса" : "Войти в голос", GUILayout.Height(28f)))
                _voiceChat.ToggleVoice();
            if (_voiceChat != null && _voiceChat.IsJoined &&
                GUILayout.Button(_voiceChat.IsMicrophoneEnabled ? "Выключить микрофон" : "Включить микрофон", GUILayout.Height(28f)))
                _voiceChat.ToggleMicrophone();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (_voiceChat != null) GUILayout.Label(_voiceChat.Status);
            GUILayout.EndArea();
        }

        private void SetChatOpen(bool open)
        {
            if (_chatOpen == open) return;
            _chatOpen = open;
            _chatFocusPending = open;
            if (_cameraRig != null)
                _cameraRig.SetMouseGestureCapture(open || _connectionState != WorldConnectionState.Active);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = open || _connectionState != WorldConnectionState.Active;
        }

        private async Task SendChatMessageAsync(string text)
        {
            var socket = _socket;
            try
            {
                if (socket == null || _connectionState != WorldConnectionState.Active)
                    throw new InvalidOperationException("Чат доступен после подключения к миру.");
                await socket.SendChatAsync("local", text, null, _lifetime.Token);
            }
            catch (Exception ex)
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                        _status = "Не удалось отправить сообщение: " + ex.Message;
                });
            }
        }

        private void DrawMovementDebugOverlay()
        {
            if (!_movementDebugVisible) return;

            var rect = new Rect(Screen.width - 390f, 12f, 378f, 210f);
            GUI.Box(rect, "ДИАГНОСТИКА ВВОДА / КАМЕРА [F3]");
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
            GUI.Label(new Rect(rect.x + 10f, rect.y + 128f, rect.width - 20f, 20f),
                $"Input System {_movementKeyboardDebug.x:0.00}/{_movementKeyboardDebug.y:0.00} | raw action {_movementRawAxesDebug.x:0.00}/{_movementRawAxesDebug.y:0.00}");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 150f, rect.width - 20f, 46f),
                "Нажато: " + _pressedKeysDebug);
        }

        private void DrawHudTabButton(string label, HudTab tab)
        {
            var previous = GUI.color;
            if (_hudTab == tab) GUI.color = new Color(0.88f, 0.72f, 0.48f);
            if (GUILayout.Button(label, GUILayout.Height(32f))) _hudTab = tab;
            GUI.color = previous;
        }

        private void DrawCharacterTab()
        {
            if (_frame?.Me == null) return;
            var me = _frame.Me;
            if (_frame.Place != null)
                GUILayout.Label($"Еда: {_frame.Place.Food:0} | Хлеб: {_frame.Place.Bread:0} | Цена: {_frame.Place.BreadPrice:0.0}");
            GUILayout.Label($"{me.Name} {me.Surname} — {me.Role}");
            GUILayout.Label($"Здоровье {me.Health:0} | Голод {me.Hunger:0} | Энергия {me.Energy:0} | Настрой {me.Morale:0}");
            GUILayout.Label($"Цель: {me.Goal} | Действие: {me.Last}");
            GUILayout.Label($"Монеты {me.Coins:0.0} | Долг {me.Debt:0.0}");
            GUILayout.Label($"Запал бега (сервер): {_serverStamina * 100f:0}% | WASD — движение, Shift — бег, Space — прыжок");
            if (me.Position != null)
            {
                GUILayout.Label($"Сцена X {me.Position.X:0.00} / Z {me.Position.Y:0.00} | взгляд {me.Position.Facing:0}°");
                GUILayout.Label($"Сеть движения: очередь {_moveSendQueue.Count}/{MaxQueuedMoveIntents} | неподтверждено {_pendingMoveIntents.Count} | отправка {(_moveSendInFlight ? "да" : "нет")} | отброшено {_discardedMoveIntentCount}");
            }
            else GUILayout.Label("Движение недоступно: серверный кадр без me.pos (нужен v1.1).");

            if (GUILayout.Button(_firstPerson ? "Переключить на вид от третьего лица" : "Переключить на вид от первого лица"))
                SetCameraMode(!_firstPerson);
            GUILayout.Label(_firstPerson
                ? "WASD — движение; Shift — бег; Space — прыжок; мышь — осмотр; ПКМ — альтернативный режим предмета/инструмента."
                : "WASD — движение; Shift — бег; Space — прыжок; мышь — обзор камеры; колесо — зум; ПКМ — альтернативный режим предмета/инструмента.");
            GUILayout.Space(4f);
            GUILayout.Label("ЗНАКОМСТВО И ПРАКТИКА");
            GUILayout.Label(TutorialHint(), GUILayout.Height(58f));
            if (me.Result != null && !string.IsNullOrEmpty(me.Result.RequestId))
                GUILayout.Label(me.Result.Ok
                    ? "Подтверждено сервером: " + me.Result.RequestId
                    : "Сервер отказал: " + (me.Result.Reason ?? "причина не указана"));
            GUILayout.Label(_tutorialStep == 0
                ? "Сначала познакомьтесь с жителем: наведитесь на него и нажмите E. Выберите реплику «Будем знакомы». Свободные занятия доступны ниже."
                : "Свободные занятия — не поручение. Мир проверит, доступны ли они сейчас:");
            if (GUILayout.Button("🔨 Кузница / огород — работать")) RequestPracticeGoal("work");
            if (GUILayout.Button("🍞 Рынок — поесть")) RequestPracticeGoal("eat");
            if (GUILayout.Button("🏠 Дом — отдохнуть")) RequestPracticeGoal("rest");
            GUILayout.Space(5f);
            GUILayout.Label("🍺 РАЗГОВОР И ПИТЬЁ");
            GUILayout.Label("Заговорите с жителем клавишей E: сервер даст варианты реплик, знакомство откроет имя. У корчмы можно угостить знакомого; входящее приглашение игрока появится здесь.", GUILayout.Height(54f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🪓 Рубка — мини-игра")) StartActivityMiniGame(ActivityMiniGame.Woodcutting);
            if (GUILayout.Button("🎣 Рыбалка — мини-игра")) StartActivityMiniGame(ActivityMiniGame.Fishing);
            if (GUILayout.Button("✨ Разряд")) QueueWorldAction("cast");
            GUILayout.EndHorizontal();
            DrawActivityMiniGame();
            GUILayout.Label("Мана: " + me.Mana.ToString("0.0"));
        }

        private void DrawInventoryTab()
        {
            if (_frame?.Me?.Inventory == null)
            {
                GUILayout.Label("Инвентарь пока недоступен.");
                return;
            }
            if (_core == null)
            {
                GUILayout.Label("Интерфейс действий инвентаря ещё загружается.");
                return;
            }
            _core.DrawInventoryTab();
        }

        private void DrawJournalTab()
        {
            if (_frame?.Me == null) return;
            var me = _frame.Me;
            GUILayout.Label("ЖУРНАЛ ПЕРСОНАЖА");
            GUILayout.Label($"День {_frame.Day}, год {_frame.Year}, {_frame.Season}");
            GUILayout.Space(5f);
            GUILayout.Label("ВВОДНЫЙ МАРШРУТ И ЛИЧНОЕ ПОРУЧЕНИЕ");
            GUILayout.Label(TutorialHint(), GUILayout.MinHeight(42f));
            if (me.Task != null)
            {
                GUILayout.Label($"Знакомство: {(me.Task.Met ? "есть" : "нет")}  Навык работы: {(me.Task.Worked ? "есть" : "нет")}  Пиво: {(me.Task.Drank ? "выпито" : "не выпито")}");
                if (me.Task.Issued != null)
                {
                    var errand = me.Task.Issued;
                    var giver = string.IsNullOrWhiteSpace(errand.GiverName) ? "житель" : errand.GiverName;
                    var good = OpeningErrandGoodLabel(errand.Good);
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("ПРИНЯТОЕ ЛИЧНОЕ ПОРУЧЕНИЕ · " + giver);
                    GUILayout.Label($"Принести: {good} — {Mathf.Max(0, errand.Have)}/{Mathf.Max(0, errand.Quantity)}");
                    GUILayout.Label(errand.Ready ? "Можно отнести поручение и получить плату." : "Поручение принято. Продолжай собирать нужное.");
                    GUILayout.Label($"Обещанная плата: {errand.Coins:0.##} монет");
                    GUILayout.EndVertical();
                }
                else if (me.Task.ErrandDone)
                {
                    GUILayout.Label("Личное поручение жителя сдано и сохранено сервером.");
                }
                else if (me.Task.Met)
                {
                    GUILayout.Label("Личное поручение ещё не принято. Свободные занятия развивают навыки, но не заменяют сдачу поручения.");
                }
                else
                {
                    GUILayout.Label("Личное поручение откроется после знакомства с жителем.");
                }

                if (me.Task.Done && me.Task.ErrandDone)
                    GUILayout.Label("Вводный маршрут завершён; личное поручение сдано сервером.");
                else if (me.Task.Done)
                    GUILayout.Label("Базовые вехи маршрута выполнены, но личное поручение жителя ещё не сдавалось.");
                else
                    GUILayout.Label("Вводный маршрут продолжается.");
            }
            else
            {
                GUILayout.Label("Сервер пока не передал состояние вводного маршрута.");
            }

            GUILayout.Space(8f);
            GUILayout.Label("ЛЕТОПИСЬ ПЕРСОНАЖА");
            if (me.Journal == null)
            {
                GUILayout.Label("Сервер пока не передаёт сохранённые вехи. Старые кадры v1 остаются совместимыми.", GUILayout.MinHeight(36f));
            }
            else if (me.Journal.Count == 0)
            {
                GUILayout.Label("Пока нет сохранённых вех. Они появятся после знакомства, первого труда и кружки в корчме.", GUILayout.MinHeight(42f));
            }
            else
            {
                foreach (var entry in me.Journal)
                {
                    if (entry == null) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"День {entry.Day} · {JournalKindLabel(entry.Kind)}");
                    GUILayout.Label(entry.Text, GUILayout.MinHeight(24f));
                    GUILayout.EndVertical();
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("ТЕКУЩЕЕ СОСТОЯНИЕ");
            GUILayout.Label(string.IsNullOrEmpty(me.Last) ? "Последнее действие пока не сообщено." : me.Last, GUILayout.MinHeight(24f));
            if (me.Result != null && !string.IsNullOrEmpty(me.Result.RequestId))
                GUILayout.Label((me.Result.Ok ? "Подтверждено сервером: " : "Отказ сервера: ") + (me.Result.Reason ?? me.Result.RequestId), GUILayout.MinHeight(32f));

            GUILayout.Space(8f);
            GUILayout.Label("НАБЛЮДЕНИЯ ЭТОГО ПОДКЛЮЧЕНИЯ");
            if (_sessionJournal.Count == 0)
                GUILayout.Label("Пока нет новых наблюдаемых событий.");
            else
                foreach (var entry in _sessionJournal)
                    GUILayout.Label("• " + entry, GUILayout.MinHeight(24f));
            GUILayout.Label("Наблюдения собираются клиентом с момента входа и не заменяют сохранённую серверную летопись.", GUILayout.MinHeight(36f));
        }

        private static string OpeningErrandGoodLabel(string good)
        {
            switch (good)
            {
                case "wood": return "дрова";
                case "fish": return "рыбу";
                case "meat": return "мясо";
                case "ore": return "руду";
                case "bread": return "хлеб";
                case "herb": return "травы";
                case "hide": return "шкуры";
                case "metal": return "железо";
                default: return string.IsNullOrWhiteSpace(good) ? "неизвестный товар" : good;
            }
        }

        private static string JournalKindLabel(string kind)
        {
            switch (kind)
            {
                case "met": return "Знакомство";
                case "worked": return "Первый труд";
                case "drank": return "Кружка в корчме";
                default: return string.IsNullOrWhiteSpace(kind) ? "Событие" : "Событие: " + kind;
            }
        }

        private void ResetSessionJournal()
        {
            _sessionJournal.Clear();
            _lastJournalAction = null;
            _lastJournalTaskSignature = null;
            _lastJournalResultId = null;
        }

        private void CaptureSessionJournal(WorldFrame frame)
        {
            if (frame?.Me == null) return;
            var me = frame.Me;
            var stamp = $"День {frame.Day}, год {frame.Year}";

            if (!string.IsNullOrWhiteSpace(me.Last) && me.Last != _lastJournalAction)
            {
                _lastJournalAction = me.Last;
                AddSessionJournalEntry(stamp + " · " + me.Last);
            }

            if (me.Task != null)
            {
                var issued = me.Task.Issued;
                var signature = $"{me.Task.Met}|{me.Task.Worked}|{me.Task.Drank}|{me.Task.Done}|{me.Task.ErrandDone}|{me.Task.Text}"
                    + (issued == null ? "|no-errand" : $"|{issued.Id}|{issued.Good}|{issued.Have}|{issued.Quantity}|{issued.Ready}");
                if (signature != _lastJournalTaskSignature)
                {
                    _lastJournalTaskSignature = signature;
                    AddSessionJournalEntry(stamp + " · Поручение: " + (me.Task.Text ?? "обновился прогресс"));
                }
            }

            if (me.Result != null && !string.IsNullOrEmpty(me.Result.RequestId)
                && me.Result.RequestId != _lastJournalResultId)
            {
                _lastJournalResultId = me.Result.RequestId;
                var outcome = me.Result.Ok ? "Подтверждено" : "Отказ сервера";
                AddSessionJournalEntry(stamp + " · " + outcome + ": "
                    + (me.Result.Reason ?? me.Result.RequestId));
            }
        }

        private void AddSessionJournalEntry(string entry)
        {
            _sessionJournal.Insert(0, entry);
            if (_sessionJournal.Count > 40)
                _sessionJournal.RemoveAt(_sessionJournal.Count - 1);
        }

        private static readonly string[] MapPoiOrder =
        {
            "tavern", "smithy", "granary", "market", "well",
            "hearth", "garden", "river", "woods", "mine"
        };

        private void DrawMapTab()
        {
            GUILayout.Label("КАРТА ПОСЕЛЕНИЯ");
            GUILayout.Label("Схема построена по серверному Layout v2. Она показывает местные препятствия и точки интереса, а не придумывает проходимые пути.");
            if (_layout == null || _layout.Version != 2)
            {
                GUILayout.Label("Серверная карта поселения пока недоступна.");
            }
            else
            {
                DrawLocalMap();
                GUILayout.Label("■ Здания  ≋ Река  ═ Мост  ● Места  • Жители  ★ Вы");
                GUILayout.Label("Север ↑ · координаты в метрах · граница карты соответствует серверной сцене 300 × 300 м");
            }

            GUILayout.Space(10f);
            GUILayout.Label("ИЗВЕСТНЫЕ ПОСЕЛЕНИЯ");
            DrawKnownSettlements();
        }

        private void DrawLocalMap()
        {
            const float mapSize = 320f;
            var mapRect = GUILayoutUtility.GetRect(mapSize, mapSize,
                GUILayout.Width(mapSize), GUILayout.Height(mapSize));
            var previousColor = GUI.color;
            DrawMapFill(mapRect, new Color(0.23f, 0.30f, 0.20f));

            const int gridLines = 10;
            for (var i = 1; i < gridLines; i++)
            {
                var x = mapRect.x + mapRect.width * i / gridLines;
                var y = mapRect.y + mapRect.height * i / gridLines;
                DrawMapFill(new Rect(x, mapRect.y, 1f, mapRect.height), new Color(0.75f, 0.78f, 0.64f, 0.16f));
                DrawMapFill(new Rect(mapRect.x, y, mapRect.width, 1f), new Color(0.75f, 0.78f, 0.64f, 0.16f));
            }

            var half = _layout.WorldHalf > 0d ? _layout.WorldHalf : 150d;
            if (_layout.RiverPresent)
            {
                var riverRect = WorldMapProjection.ToMapRect(
                    -half, _layout.River.Y0, half, _layout.River.Y1, half, mapRect);
                DrawMapFill(riverRect, new Color(0.20f, 0.43f, 0.52f, 0.95f));

                var bridgeRect = WorldMapProjection.ToMapRect(
                    _layout.River.BridgeX0, _layout.River.Y0 - 1.5d,
                    _layout.River.BridgeX1, _layout.River.Y1 + 1.5d, half, mapRect);
                DrawMapFill(bridgeRect, new Color(0.55f, 0.37f, 0.22f, 1f));
            }

            if (_layout.Buildings != null)
            {
                foreach (var building in _layout.Buildings)
                {
                    if (building == null) continue;
                    var footprint = WorldMapProjection.ToMapRect(
                        building.X0, building.Y0, building.X1, building.Y1, half, mapRect);
                    DrawMapFill(footprint, new Color(0.30f, 0.23f, 0.18f, 0.98f));
                }
            }

            EnsureMapStyles();
            if (_layout.Pois != null)
            {
                foreach (var poiId in MapPoiOrder)
                {
                    if (!_layout.Pois.TryGetValue(poiId, out var point) || point == null) continue;
                    var position = WorldMapProjection.ToMapPoint(point.X, point.Y, half, mapRect);
                    var marker = new Rect(position.x - 9f, position.y - 9f, 18f, 18f);
                    var previousBackground = GUI.backgroundColor;
                    var previousContent = GUI.contentColor;
                    GUI.backgroundColor = new Color(0.70f, 0.53f, 0.31f, 1f);
                    GUI.contentColor = Color.white;
                    GUI.Box(marker, MapPoiGlyph(poiId), _mapPoiStyle);
                    GUI.backgroundColor = previousBackground;
                    GUI.contentColor = previousContent;
                    var labelRect = new Rect(position.x + 10f, position.y - 8f, 112f, 16f);
                    GUI.Label(labelRect, MapPoiName(poiId), _mapPoiLabelStyle);
                }
            }

            var neighbors = _frame?.Place?.Neighbors;
            if (neighbors != null)
            {
                foreach (var neighbor in neighbors)
                {
                    if (neighbor?.Position == null) continue;
                    var position = WorldMapProjection.ToMapPoint(
                        neighbor.Position.X, neighbor.Position.Y, half, mapRect);
                    var marker = new Rect(position.x - 4f, position.y - 4f, 8f, 8f);
                    var previousBackground = GUI.backgroundColor;
                    var previousContent = GUI.contentColor;
                    GUI.backgroundColor = neighbor.Controlled
                        ? new Color(0.28f, 0.72f, 0.88f, 1f)
                        : neighbor.Known
                            ? new Color(0.78f, 0.78f, 0.58f, 1f)
                            : new Color(0.58f, 0.62f, 0.52f, 1f);
                    GUI.contentColor = Color.white;
                    GUI.Box(marker, neighbor.Controlled ? "И" : "•", _mapNeighborStyle);
                    GUI.backgroundColor = previousBackground;
                    GUI.contentColor = previousContent;
                    var residentName = neighbor.Controlled
                        ? "Игрок"
                        : neighbor.Known && !string.IsNullOrWhiteSpace(neighbor.Name) ? neighbor.Name : "Житель";
                    var activity = MapActivityName(neighbor.ActivityCode);
                    var residentLabel = string.IsNullOrEmpty(activity)
                        ? residentName
                        : residentName + " · " + activity;
                    GUI.Label(new Rect(position.x + 5f, position.y - 7f, 128f, 14f), residentLabel, _mapPoiLabelStyle);
                }
            }

            var playerPosition = _frame?.Me?.Position;
            if (playerPosition != null)
            {
                var position = WorldMapProjection.ToMapPoint(
                    playerPosition.X, playerPosition.Y, half, mapRect);
                var marker = new Rect(position.x - 8f, position.y - 8f, 16f, 16f);
                var previousBackground = GUI.backgroundColor;
                var previousContent = GUI.contentColor;
                GUI.backgroundColor = new Color(0.65f, 0.20f, 0.15f, 1f);
                GUI.contentColor = Color.white;
                GUI.Box(marker, "★", _mapPlayerStyle);
                GUI.backgroundColor = previousBackground;
                GUI.contentColor = previousContent;
            }

            GUI.color = previousColor;
            var borderColor = new Color(0.10f, 0.13f, 0.10f, 1f);
            DrawMapFill(new Rect(mapRect.x, mapRect.y, mapRect.width, 2f), borderColor);
            DrawMapFill(new Rect(mapRect.x, mapRect.yMax - 2f, mapRect.width, 2f), borderColor);
            DrawMapFill(new Rect(mapRect.x, mapRect.y, 2f, mapRect.height), borderColor);
            DrawMapFill(new Rect(mapRect.xMax - 2f, mapRect.y, 2f, mapRect.height), borderColor);
            if (playerPosition != null)
                GUILayout.Label($"Ваше положение: X {playerPosition.X:0.0} м · Y {playerPosition.Y:0.0} м");
            else
                GUILayout.Label("Положение персонажа пока не передано сервером.");
            GUILayout.Label("Места: К — корчма · Кз — кузница · Кн — конюшня и амбар · Р — рынок · Кл — колодец · С — очаг · О — огород · Рк — река · Л — лес · Ш — рудник");
        }

        private static void DrawMapFill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureMapStyles()
        {
            if (_mapPoiStyle != null && _mapPlayerStyle != null) return;
            _mapPoiStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _mapPoiStyle.normal.textColor = Color.white;
            _mapPoiLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _mapPoiLabelStyle.normal.textColor = new Color(1f, 0.96f, 0.82f, 1f);
            _mapNeighborStyle = new GUIStyle(_mapPoiStyle) { fontSize = 8 };
            _mapNeighborStyle.normal.textColor = Color.white;
            _mapPlayerStyle = new GUIStyle(_mapPoiStyle) { fontSize = 11 };
            _mapPlayerStyle.normal.textColor = Color.white;
        }

        private static string MapPoiName(string poiId)
        {
            switch (poiId)
            {
                case "tavern": return "Корчма";
                case "smithy": return "Кузница";
                case "granary": return "Конюшня";
                case "market": return "Рынок";
                case "well": return "Колодец";
                case "hearth": return "Очаг";
                case "garden": return "Огород";
                case "river": return "Река";
                case "woods": return "Лес";
                case "mine": return "Рудник";
                default: return poiId;
            }
        }

        private static string MapPoiGlyph(string poiId)
        {
            switch (poiId)
            {
                case "tavern": return "К";
                case "smithy": return "Кз";
                case "granary": return "Кн";
                case "market": return "Р";
                case "well": return "Кл";
                case "hearth": return "С";
                case "garden": return "О";
                case "river": return "Рк";
                case "woods": return "Л";
                case "mine": return "Ш";
                default: return "·";
            }
        }

        private static string MapActivityName(string activityCode)
        {
            // Use the stable server activity registry, never localized "last" prose or guessed goals.
            switch (activityCode)
            {
                case "sleep": return "спит";
                case "rest_idle": return "отдыхает";
                case "meal": return "ест";
                case "drink_tavern": return "пьёт";
                case "visit_neighbors": return "общается";
                case "chop_wood": return "рубит лес";
                case "fish_river": return "рыбачит";
                case "farm_sow": return "сеет";
                case "farm_reap": return "жнёт";
                case "farm_tend": return "ухаживает за полем";
                case "smith_forge": return "куёт";
                case "cook_hearth": return "готовит";
                case "mine_ore": return "добывает руду";
                case "hunt_wild": return "на промысле";
                case "forage_herbs": return "собирает травы";
                case "brew_remedy": return "варит снадобье";
                case "weave": return "ткёт";
                case "herd_graze": return "пасёт скот";
                case "herd_barn": return "ухаживает за скотом";
                case "keep_tavern": return "за стойкой";
                case "study_mana": return "изучает плетения";
                case "fisher_mend": return "чинит сети";
                case "farm_mend": return "чинит снасти";
                case "farm_nosow":
                case "farm_noreap":
                case "smith_cold":
                case "mine_spent":
                case "hunter_empty":
                case "weaver_wait": return "ожидает дела";
                default: return string.Empty;
            }
        }

        private void DrawKnownSettlements()
        {
            var me = _frame?.Me;
            var knownPlaces = me?.KnownPlaces;
            if (knownPlaces == null || knownPlaces.Count == 0)
            {
                if (_frame?.Place != null)
                    GUILayout.Label("Текущее поселение: " + _frame.Place.Name + ". Другие места появятся здесь только после того, как персонаж получит о них личное знание.");
                else
                    GUILayout.Label("Пока нет поселений, открытых персонажу.");
                return;
            }

            foreach (var known in knownPlaces)
            {
                if (known == null || string.IsNullOrWhiteSpace(known.Id)) continue;
                SettlementFrame details = null;
                if (me.World != null)
                {
                    foreach (var settlement in me.World)
                    {
                        if (settlement != null && settlement.Id == known.Id)
                        {
                            details = settlement;
                            break;
                        }
                    }
                }

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(string.IsNullOrWhiteSpace(known.Name) ? "Поселение" : known.Name);
                var status = known.Visited ? "Персонаж здесь бывал" : "Персонаж ещё не посещал";
                if (known.Known) status += " · есть личное знание";
                GUILayout.Label(status);
                if (details != null)
                    GUILayout.Label($"{details.Tier} · {(details.Outer ? "Внешний край" : "Родной край")} · X {details.X:0}, Y {details.Y:0}");
                GUILayout.EndVertical();
            }
        }

        private void DrawSettingsTab()
        {
            GUILayout.Label("НАСТРОЙКИ");
            DrawOptionsControls();
            GUILayout.Space(8f);
            if (GUILayout.Button("Сбросить настройки", GUILayout.Height(32f)))
            {
                PlayerPrefs.DeleteKey("Pogranichye.MasterVolume");
                PlayerPrefs.DeleteKey("Pogranichye.SfxVolume");
                PlayerPrefs.DeleteKey("Pogranichye.AmbienceVolume");
                PlayerPrefs.DeleteKey("Pogranichye.MouseSensitivity");
                PlayerPrefs.DeleteKey("Pogranichye.InvertMouseY");
                PlayerPrefs.DeleteKey("Pogranichye.ShadowDistance");
                PlayerPrefs.DeleteKey("Pogranichye.LodBias");
                PlayerPrefs.DeleteKey("Pogranichye.AntiAliasing");
                PlayerPrefs.DeleteKey("Pogranichye.VSync");
                PlayerPrefs.DeleteKey("Pogranichye.TargetFrameRate");
                _masterVolume = 0.85f; _sfxVolume = 0.9f; _ambienceVolume = 0.75f;
                _mouseSensitivity = 0.035f; _invertMouseY = false; _shadowDistance = 120f;
                _lodBias = 1f; _antiAliasing = 2;
                _vSync = true; _targetFrameRate = 60;
                AudioListener.volume = _masterVolume;
                _audioDirector?.SetVolumes(_sfxVolume, _ambienceVolume);
                _villageSoundscape?.SetAmbienceVolume(_ambienceVolume);
                _cameraRig?.SetLookSettings(_mouseSensitivity, _invertMouseY);
                ApplyGraphicsSettings();
                PlayerPrefs.Save();
            }
        }

        private void DrawOptionsControls()
        {
            GUILayout.Label("ЗВУК");
            DrawVolumeSlider("Общая громкость", ref _masterVolume, "Pogranichye.MasterVolume");
            DrawVolumeSlider("Эффекты: шаги, бой, магия", ref _sfxVolume, "Pogranichye.SfxVolume");
            DrawVolumeSlider("Окружение: ветер, птицы, вода, костёр", ref _ambienceVolume, "Pogranichye.AmbienceVolume");
            _audioDirector?.SetVolumes(_sfxVolume, _ambienceVolume);
            _villageSoundscape?.SetAmbienceVolume(_ambienceVolume);

            GUILayout.Space(8f);
            GUILayout.Label("ГРАФИКА");
            var qualityNames = QualitySettings.names;
            if (qualityNames != null && qualityNames.Length > 0)
            {
                GUILayout.Label("Общий пресет качества");
                var quality = QualitySettings.GetQualityLevel();
                var selected = GUILayout.SelectionGrid(quality, qualityNames, Mathf.Min(3, qualityNames.Length), GUILayout.MinHeight(32f));
                if (selected != quality && selected >= 0 && selected < qualityNames.Length)
                    QualitySettings.SetQualityLevel(selected, true);
            }
            GUILayout.Label("Дальность теней: " + Mathf.RoundToInt(_shadowDistance) + " м");
            var shadows = GUILayout.HorizontalSlider(_shadowDistance, 35f, 220f);
            if (!Mathf.Approximately(shadows, _shadowDistance))
            {
                _shadowDistance = shadows;
                QualitySettings.shadowDistance = shadows;
                SaveFloat("Pogranichye.ShadowDistance", shadows);
            }
            GUILayout.Label("Детализация объектов (LOD): " + _lodBias.ToString("0.0") + "×");
            var lod = GUILayout.HorizontalSlider(_lodBias, 0.5f, 2.5f);
            if (!Mathf.Approximately(lod, _lodBias))
            {
                _lodBias = lod;
                QualitySettings.lodBias = lod;
                SaveFloat("Pogranichye.LodBias", lod);
            }
            GUILayout.Label("Сглаживание краёв");
            var aaOptions = new[] { 0, 2, 4, 8 };
            var aaLabels = new[] { "Выкл.", "2×", "4×", "8×" };
            var aaIndex = Array.IndexOf(aaOptions, _antiAliasing);
            if (aaIndex < 0) aaIndex = 0;
            var nextAa = GUILayout.SelectionGrid(aaIndex, aaLabels, 4, GUILayout.MinHeight(30f));
            if (nextAa != aaIndex && nextAa >= 0 && nextAa < aaOptions.Length)
            {
                _antiAliasing = aaOptions[nextAa];
                QualitySettings.antiAliasing = _antiAliasing;
                SaveInt("Pogranichye.AntiAliasing", _antiAliasing);
            }
            var sync = GUILayout.Toggle(_vSync, "Вертикальная синхронизация");
            if (sync != _vSync)
            {
                _vSync = sync;
                QualitySettings.vSyncCount = sync ? 1 : 0;
                SaveInt("Pogranichye.VSync", sync ? 1 : 0);
            }
            GUILayout.Label("Ограничение кадров");
            var fpsOptions = new[] { 30, 60, 90, 120, 144, 0 };
            var fpsLabels = new[] { "30", "60", "90", "120", "144", "Без ограничения" };
            var fpsIndex = Array.IndexOf(fpsOptions, _targetFrameRate);
            if (fpsIndex < 0) fpsIndex = 1;
            var newFpsIndex = GUILayout.SelectionGrid(fpsIndex, fpsLabels, 3, GUILayout.MinHeight(32f));
            if (newFpsIndex != fpsIndex && newFpsIndex >= 0 && newFpsIndex < fpsOptions.Length)
            {
                _targetFrameRate = fpsOptions[newFpsIndex];
                Application.targetFrameRate = _targetFrameRate == 0 ? -1 : _targetFrameRate;
                SaveInt("Pogranichye.TargetFrameRate", _targetFrameRate);
            }
            GUILayout.Label("Режим экрана");
            if (GUILayout.Button(Screen.fullScreen ? "Перейти в оконный режим" : "Полноэкранный режим", GUILayout.Height(32f)))
                ToggleFullscreen();
            GUILayout.Label($"{Screen.width}×{Screen.height} · {(Screen.fullScreen ? "полный экран" : "окно")}");

            GUILayout.Space(8f);
            GUILayout.Label("УПРАВЛЕНИЕ");
            GUILayout.Label("Чувствительность мыши: " + _mouseSensitivity.ToString("0.000"));
            var sensitivity = GUILayout.HorizontalSlider(_mouseSensitivity, 0.008f, 0.09f);
            if (!Mathf.Approximately(sensitivity, _mouseSensitivity))
            {
                _mouseSensitivity = sensitivity;
                _cameraRig?.SetLookSettings(_mouseSensitivity, _invertMouseY);
                SaveFloat("Pogranichye.MouseSensitivity", sensitivity);
            }
            var invert = GUILayout.Toggle(_invertMouseY, "Инвертировать вертикальный обзор");
            if (invert != _invertMouseY)
            {
                _invertMouseY = invert;
                _cameraRig?.SetLookSettings(_mouseSensitivity, _invertMouseY);
                SaveInt("Pogranichye.InvertMouseY", invert ? 1 : 0);
            }
            if (GUILayout.Button(_firstPerson ? "Камера: первое лицо" : "Камера: третье лицо", GUILayout.Height(30f)))
                SetCameraMode(!_firstPerson);
            GUILayout.Label("WASD — движение · Alt — лёгкий бег · Shift — быстрый бег · Space — прыжок\nМышь — обзор · E — взаимодействие · F1 — интерфейс\nЛКМ — атака/выстрел · ПКМ — блок · Q — уворот · R — плетение\nZ/X/V/B/C — боевые приёмы · T — стойка · Tab — дела и меню");
        }

        private void DrawVolumeSlider(string label, ref float value, string key)
        {
            GUILayout.Label(label + ": " + Mathf.RoundToInt(value * 100f) + "%");
            var next = GUILayout.HorizontalSlider(value, 0f, 1f);
            if (Mathf.Approximately(next, value)) return;
            value = next;
            SaveFloat(key, value);
            if (key == "Pogranichye.MasterVolume") AudioListener.volume = value;
        }

        private static void SaveFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }

        private static void SaveInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        private void ApplyGraphicsSettings()
        {
            QualitySettings.shadowDistance = _shadowDistance;
            QualitySettings.lodBias = _lodBias;
            QualitySettings.antiAliasing = _antiAliasing;
            QualitySettings.vSyncCount = _vSync ? 1 : 0;
            Application.targetFrameRate = _targetFrameRate == 0 ? -1 : _targetFrameRate;
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
                foreach (var item in inventory.Items)
                {
                    if (item.Value <= 0) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("• " + item.Key + "  × " + item.Value, GUILayout.MinWidth(150f));
                    if (IsHandEquipment(item.Key) && !string.Equals(inventory.Hand, item.Key, StringComparison.Ordinal))
                    {
                        if (GUILayout.Button("В руку", GUILayout.Width(86f)))
                            QueueInventoryItemIntent("equip", item.Key);
                    }
                    if (IsWearableEquipment(item.Key))
                    {
                        // The worn slot is server-authoritative; never toggle optimistically on click.
                        var isWorn = IsItemWorn(inventory, item.Key);
                        var operation = isWorn ? "unwear" : "wear";
                        if (GUILayout.Button(isWorn ? "Снять" : "Надеть", GUILayout.Width(86f)))
                            QueueInventoryItemIntent(operation, item.Key);
                    }
                    GUILayout.EndHorizontal();
                }
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

        private static bool IsHandEquipment(string item)
        {
            return item == "axe" || item == "sword" || item == "knife" || item == "bow"
                || item == "rod" || item == "torch" || item == "lantern";
        }

        private static bool IsWearableEquipment(string item)
        {
            return item == "cloak" || item == "boots" || item == "hat"
                || item == "jerkin" || item == "shield" || item == "purse";
        }

        private static bool IsItemWorn(InventoryFrame inventory, string item)
        {
            if (inventory?.Worn == null || string.IsNullOrEmpty(item)) return false;
            foreach (var wornItem in inventory.Worn.Values)
            {
                if (string.Equals(wornItem, item, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void QueueInventoryItemIntent(string op, string item)
        {
            if (_connectionState != WorldConnectionState.Active || _core == null ||
                !_core.QueueRequest(InventoryRequestLabelPrefix + " " + op + " " + item, op, "item", item))
            {
                _status = "Не удалось поставить действие с предметом в очередь. Проверьте соединение и дождитесь предыдущего ответа.";
                return;
            }

            _status = "Ожидаю подтверждения сервера: " + op + " / " + item + "…";
        }

        private void UpdateDialogueChoiceInput()
        {
            var options = _frame?.Me?.Talk?.Options;
            if (_input == null || options == null) return;
            var count = Mathf.Min(9, options.Count);
            for (var i = 0; i < count; i++)
            {
                if (!_input.JourneyChoicePressed(i)) continue;
                var option = options[i];
                if (option != null && !string.IsNullOrEmpty(option.Id))
                    QueueSay(option.Id);
                return;
            }
        }

        private void EnsureDialogueStyles()
        {
            if (_dialogueWhoStyle == null)
            {
                _dialogueWhoStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 21,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                _dialogueWhoStyle.normal.textColor = new Color(0.94f, 0.82f, 0.62f);
            }
            if (_dialogueLineStyle == null)
            {
                _dialogueLineStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    wordWrap = true,
                    richText = true,
                    alignment = TextAnchor.UpperLeft
                };
                _dialogueLineStyle.normal.textColor = new Color(0.97f, 0.94f, 0.86f);
            }
            if (_dialogueOptionStyle == null)
            {
                _dialogueOptionStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 18,
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(14, 12, 6, 6)
                };
            }
            if (_dialogueHintStyle == null)
            {
                _dialogueHintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft
                };
                _dialogueHintStyle.normal.textColor = new Color(0.78f, 0.75f, 0.67f);
            }
        }

        private void DrawSocialOverlay()
        {
            if (_frame?.Me == null) return;
            var talk = _frame.Me.Talk;
            if (talk != null)
            {
                EnsureDialogueStyles();
                var optionCount = talk.Options != null ? Mathf.Min(9, talk.Options.Count) : 0;
                var desiredHeight = 190f + optionCount * 50f + (!string.IsNullOrEmpty(_talkFeedback) ? 30f : 0f);
                var panelHeight = Mathf.Min(Screen.height - 28f, Mathf.Max(230f, desiredHeight));
                var panelWidth = Mathf.Min(780f, Screen.width - 28f);
                var rect = new Rect((Screen.width - panelWidth) * 0.5f,
                    Screen.height - panelHeight - 14f, panelWidth, panelHeight);
                GUI.Box(rect, "Разговор");
                GUILayout.BeginArea(new Rect(rect.x + 18f, rect.y + 30f, rect.width - 36f, rect.height - 42f));
                GUILayout.Label(talk.Who ?? "Собеседник", _dialogueWhoStyle, GUILayout.Height(30f));
                _dialogueScroll = GUILayout.BeginScrollView(_dialogueScroll,
                    GUILayout.Height(Mathf.Max(86f, rect.height - 164f)));
                GUILayout.Label(talk.Line ?? "…", _dialogueLineStyle, GUILayout.MinHeight(66f));
                if (!string.IsNullOrEmpty(_talkFeedback))
                    GUILayout.Label(_talkFeedback, _dialogueHintStyle, GUILayout.MinHeight(24f));
                if (talk.Options != null)
                {
                    for (var i = 0; i < talk.Options.Count && i < 9; i++)
                    {
                        var option = talk.Options[i];
                        if (option == null || string.IsNullOrEmpty(option.Id)) continue;
                        var label = (i + 1) + ".  " + (option.Text ?? option.Id);
                        if (GUILayout.Button(label, _dialogueOptionStyle, GUILayout.Height(46f)))
                            QueueSay(option.Id);
                    }
                }
                GUILayout.EndScrollView();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Выбор: клавиши 1–9 или нажми на реплику", _dialogueHintStyle);
                if (GUILayout.Button("Закончить разговор", GUILayout.Width(190f), GUILayout.Height(36f)))
                    _ = SendEndTalkAsync();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
            else if (!string.IsNullOrEmpty(_talkFeedback))
            {
                var rect = new Rect(Screen.width - 430f, Screen.height - 126f, 410f, 110f);
                GUI.Box(rect, "Разговор с жителем");
                GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 38f));
                GUILayout.Label(_talkFeedback, GUILayout.Height(66f));
                GUILayout.EndArea();
            }

            var invite = _frame.Me.InviteFrom;
            if (invite != null)
            {
                var rect = new Rect(Screen.width - 430f, Screen.height - 150f, 410f, 128f);
                GUI.Box(rect, "Приглашение выпить");
                GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 38f));
                GUILayout.Label((invite.Who ?? "Игрок") + " зовёт к стойке");
                if (_drinkRequestPending)
                {
                    GUILayout.Label("Ожидаем подтверждения сервера…");
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Принять")) QueueDrinkRequest("accept", null, "Принятие приглашения");
                    if (GUILayout.Button("Отказаться")) QueueDrinkRequest("declinedrink", null, "Отказ от приглашения");
                    GUILayout.EndHorizontal();
                }
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
                        var previousEnabled = GUI.enabled;
                        GUI.enabled = previousEnabled && !_drinkRequestPending;
                        if (GUILayout.Button("Позвать выпить", GUILayout.Width(130f)))
                            QueueDrinkRequest("invite", neighbor.Id, "Приглашение отправлено");
                        GUI.enabled = previousEnabled;
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndArea();
                }
            }
        }

        private void QueueWorldAction(string action)
        {
            if (_connectionState != WorldConnectionState.Active || _core == null ||
                !_core.QueueRequest(WorldActionLabelPrefix + " " + action, "act", "action", action))
            {
                _status = "Не удалось поставить действие в очередь. Проверьте соединение и дождитесь предыдущего ответа.";
                return;
            }
            _status = "Ожидаю подтверждения сервера: " + action + "…";
            if (action == "cast" || action == "weave")
            {
                _audioDirector?.SetPlayerPosition(_views?.PlayerTransform != null ? _views.PlayerTransform.position : transform.position);
                _audioDirector?.Magic();
            }
            var gesture = action == "chop" ? "chop" : action == "fish" ? "fish_reel" : action;
            _views?.PlayPlayerActionGesture(gesture);
        }

        private void StartActivityMiniGame(ActivityMiniGame game)
        {
            if (_connectionState != WorldConnectionState.Active || _core == null)
            {
                _activityMiniGameFeedback = "Сначала подключитесь к миру.";
                return;
            }
            _activityMiniGame = game;
            _activityMiniGameStartedAt = Time.realtimeSinceStartup;
            if (game == ActivityMiniGame.Fishing) _views?.PlayPlayerActionGesture("fish_cast");
            _woodcuttingGoodHits = 0;
            _activityMiniGameFeedback = game == ActivityMiniGame.Woodcutting
                ? "Трижды нажмите «Ударить», когда метка окажется в светлой зоне."
                : "Следите за поплавком. Когда появится поклёвка, быстро нажмите «Подсечь».";
        }

        private void DrawActivityMiniGame()
        {
            if (_activityMiniGame == ActivityMiniGame.None)
            {
                if (!string.IsNullOrEmpty(_activityMiniGameFeedback))
                    GUILayout.Label(_activityMiniGameFeedback);
                return;
            }

            var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _activityMiniGameStartedAt);
            GUILayout.BeginVertical(GUI.skin.box);
            if (_activityMiniGame == ActivityMiniGame.Woodcutting)
            {
                GUILayout.Label("РУБКА · РИТМ УДАРОВ");
                var phase = Mathf.PingPong(elapsed * 0.85f, 1f);
                GUILayout.Label("Точные удары: " + _woodcuttingGoodHits + " / 3");
                DrawTimingTrack(phase, 0.40f, 0.18f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ударить", GUILayout.Height(34f)))
                {
                    _views?.PlayPlayerActionGesture("chop");
                    if (Mathf.Abs(phase - 0.40f) <= 0.09f)
                    {
                        _woodcuttingGoodHits++;
                        _activityMiniGameFeedback = "Точный удар!";
                        if (_woodcuttingGoodHits >= 3)
                        {
                            _activityMiniGame = ActivityMiniGame.None;
                            _activityMiniGameFeedback = "Ритм выдержан. Сервер проверяет инструмент, место и результат…";
                            QueueWorldAction("chop");
                        }
                    }
                    else _activityMiniGameFeedback = "Удар пришёлся мимо ритма. Попробуйте ещё раз.";
                }
                if (GUILayout.Button("Отойти", GUILayout.Width(90f)))
                {
                    _activityMiniGame = ActivityMiniGame.None;
                    _activityMiniGameFeedback = "Работа прервана.";
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("РЫБАЛКА · ПОКЛЁВКА");
                const float biteAt = 2.0f;
                const float biteDuration = 2.6f;
                if (elapsed < biteAt)
                {
                    GUILayout.Label("Поплавок спокойно покачивается…");
                    GUILayout.HorizontalSlider(Mathf.Clamp01(elapsed / biteAt), 0f, 1f);
                    GUILayout.Label("Ждите поклёвку.");
                }
                else if (elapsed <= biteAt + biteDuration)
                {
                    GUILayout.Label("ПОКЛЁВКА! Подсекайте!");
                    GUILayout.HorizontalSlider((biteAt + biteDuration - elapsed) / biteDuration, 0f, 1f);
                    if (GUILayout.Button("Подсечь!", GUILayout.Height(38f)))
                    {
                        _views?.PlayPlayerActionGesture("fish_reel");
                        _activityMiniGame = ActivityMiniGame.None;
                        _activityMiniGameFeedback = "Подсечка выполнена. Сервер проверяет водоём, снасти и улов…";
                        QueueWorldAction("fish");
                    }
                }
                else
                {
                    GUILayout.Label("Рыба ушла. Попробуйте забросить снова.");
                    if (GUILayout.Button("Новый заброс"))
                        StartActivityMiniGame(ActivityMiniGame.Fishing);
                }
                if (GUILayout.Button("Свернуть рыбалку"))
                {
                    _activityMiniGame = ActivityMiniGame.None;
                    _activityMiniGameFeedback = "Вы смотали леску.";
                }
            }
            if (!string.IsNullOrEmpty(_activityMiniGameFeedback))
                GUILayout.Label(_activityMiniGameFeedback);
            GUILayout.EndVertical();
        }

        private static void DrawTimingTrack(float marker, float targetCenter, float targetWidth)
        {
            var rect = GUILayoutUtility.GetRect(220f, 22f, GUILayout.ExpandWidth(true));
            var previous = GUI.color;
            GUI.color = new Color(0.20f, 0.26f, 0.19f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.77f, 0.68f, 0.42f, 1f);
            GUI.DrawTexture(new Rect(rect.x + rect.width * (targetCenter - targetWidth * 0.5f),
                rect.y, rect.width * targetWidth, rect.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.90f, 0.92f, 0.78f, 1f);
            GUI.DrawTexture(new Rect(rect.x + rect.width * marker - 2f, rect.y - 3f,
                4f, rect.height + 6f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void SendCorePayload(Dictionary<string, object> payload)
        {
            _ = SendCorePayloadAsync(payload);
        }

        private async Task SendCorePayloadAsync(Dictionary<string, object> payload)
        {
            var socket = _socket;
            try
            {
                if (_connectionState != WorldConnectionState.Active || socket == null)
                    throw new InvalidOperationException("Мир ещё не подключён.");
                await socket.SendPayloadAsync(payload, _lifetime.Token);
            }
            catch (Exception ex)
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket))
                        _status = "Не удалось отправить действие: " + ex.Message;
                });
            }
        }

        private void QueueTalk(string target, int requestSerial)
        {
            var label = TalkRequestLabelPrefix + target;
            if (_connectionState == WorldConnectionState.Active && _core != null &&
                _core.QueueRequest(label, "talk", "target", target))
                return;

            if (_pendingTalkRequestSerial == requestSerial &&
                string.Equals(_pendingTalkTargetId, target, StringComparison.Ordinal))
            {
                _pendingTalkTargetId = null;
                _pendingTalkRequestSerial = 0;
                _confirmedTalkTargetId = null;
                _talkConfirmationTimedOut = false;
                _talkFeedback = "Очередь действий занята или соединение недоступно. Повтори разговор.";
            }
        }

        private void OnCoreRequestResolved(string label, RequestGate.Outcome outcome, string reason)
        {
            if (label != null && label.StartsWith(TalkRequestLabelPrefix, StringComparison.Ordinal))
            {
                var target = label.Substring(TalkRequestLabelPrefix.Length);
                if (!string.Equals(target, _pendingTalkTargetId, StringComparison.Ordinal))
                    return; // a superseded click on another NPC must not alter the current dialog UI

                if (outcome == RequestGate.Outcome.Rejected || outcome == RequestGate.Outcome.TimedOut)
                {
                    _pendingTalkTargetId = null;
                    _pendingTalkRequestSerial = 0;
                    _confirmedTalkTargetId = null;
                    _talkConfirmationTimedOut = outcome == RequestGate.Outcome.TimedOut;
                    _talkFeedback = outcome == RequestGate.Outcome.Rejected
                        ? "Житель не ответил на обращение: " + reason
                        : "Не удалось начать разговор: " + reason;
                }
                else
                {
                    _talkFeedback = "Сервер подтвердил обращение к жителю.";
                }
                return;
            }

            if (label == DialogueRequestLabel)
                _talkFeedback = outcome == RequestGate.Outcome.Accepted
                    ? "Реплика принята сервером."
                    : "Ответ жителя не принят: " + reason;

            if (label != null && label.StartsWith(DrinkRequestLabelPrefix, StringComparison.Ordinal))
            {
                _drinkRequestPending = false;
                if (outcome == RequestGate.Outcome.Accepted)
                {
                    _audioDirector?.SetPlayerPosition(_views?.PlayerTransform != null ? _views.PlayerTransform.position : transform.position);
                    _audioDirector?.DrinkBeer();
                }
                _status = outcome == RequestGate.Outcome.Accepted
                    ? label.Substring(DrinkRequestLabelPrefix.Length).Trim() + ": сервер подтвердил."
                    : outcome == RequestGate.Outcome.Rejected
                        ? "Сервер отклонил действие с кружкой: " + reason
                        : "Нет подтверждения сервера на действие с кружкой. Можно попробовать ещё раз.";
            }

            if (label != null && label.StartsWith(InventoryRequestLabelPrefix, StringComparison.Ordinal))
                _status = outcome == RequestGate.Outcome.Accepted
                    ? label.Substring(InventoryRequestLabelPrefix.Length).Trim() + ": сервер подтвердил."
                    : outcome == RequestGate.Outcome.Rejected
                        ? "Сервер отклонил действие с предметом: " + reason
                        : "Нет подтверждения сервера на действие с предметом. Можно попробовать ещё раз.";

            if (label != null && label.StartsWith(WorldActionLabelPrefix, StringComparison.Ordinal))
                _status = outcome == RequestGate.Outcome.Accepted
                    ? label.Substring(WorldActionLabelPrefix.Length).Trim() + ": сервер подтвердил."
                    : outcome == RequestGate.Outcome.Rejected
                        ? "Сервер отклонил действие: " + reason
                        : "Нет подтверждения сервера на действие. Можно попробовать ещё раз.";
        }

        private void UpdateTalkConfirmationTimeout()
        {
            if (string.IsNullOrEmpty(_pendingTalkTargetId) || _talkConfirmationTimedOut ||
                _connectionState != WorldConnectionState.Active ||
                Time.unscaledTime < _pendingTalkDeadline)
                return;

            _talkConfirmationTimedOut = true;
            _confirmedTalkTargetId = null;
            _talkFeedback = "В кадре сервера нет разговора с этим жителем. Запрос не подтверждён; подойдите ближе и повторите.";
        }

        private void UpdateTalkConfirmation(WorldFrame frame)
        {
            if (frame?.Me == null) return;
            var talk = frame.Me.Talk;
            if (string.IsNullOrEmpty(_pendingTalkTargetId))
            {
                if (!string.IsNullOrEmpty(_confirmedTalkTargetId) &&
                    (talk == null || !string.Equals(talk.With, _confirmedTalkTargetId, StringComparison.Ordinal)))
                {
                    _confirmedTalkTargetId = null;
                    _talkFeedback = null;
                }
                return;
            }

            if (talk == null || !string.Equals(talk.With, _pendingTalkTargetId, StringComparison.Ordinal))
                return;

            _confirmedTalkTargetId = _pendingTalkTargetId;
            _pendingTalkTargetId = null;
            _pendingTalkRequestSerial = 0;
            _talkConfirmationTimedOut = false;
            _talkFeedback = "В кадре сервера подтверждён активный разговор с выбранным жителем.";
        }

        private void QueueSay(string line)
        {
            if (_connectionState == WorldConnectionState.Active && _core != null &&
                _core.QueueRequest(DialogueRequestLabel, "say", "line", line))
                return;
            _talkFeedback = "Не удалось поставить реплику в очередь: соединение недоступно или очередь заполнена.";
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

        private bool QueueDrinkRequest(string op, string target, string label)
        {
            if (_drinkRequestPending || _connectionState != WorldConnectionState.Active || _core == null)
                return false;

            var queued = op == "invite"
                ? _core.QueueRequest(DrinkRequestLabelPrefix + " " + label, op, "target", target)
                : _core.QueueRequest(DrinkRequestLabelPrefix + " " + label, op);
            if (!queued)
            {
                _status = "Не удалось поставить действие с кружкой в очередь. Дождитесь ответа на предыдущее действие.";
                return false;
            }

            _drinkRequestPending = true;
            _status = "Ожидаю подтверждения сервера: " + label.ToLowerInvariant() + "…";
            return true;
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

        private void UpdateTutorialIntroduction(WorldFrame frame)
        {
            // The first tutorial step is a real acquaintance, not a tavern goal.
            // Advance only after the server marks this exact resident as known.
            if (_tutorialStep != 0 || string.IsNullOrEmpty(_tutorialNpcTargetId) ||
                frame == null || !frame.Alive || frame.Me == null)
                return;

            var acquainted = frame.Me.Talk != null && frame.Me.Talk.Known &&
                string.Equals(frame.Me.Talk.With, _tutorialNpcTargetId, StringComparison.Ordinal);
            if (!acquainted && frame.Place?.Neighbors != null)
            {
                foreach (var neighbor in frame.Place.Neighbors)
                {
                    if (neighbor != null && neighbor.Known &&
                        string.Equals(neighbor.Id, _tutorialNpcTargetId, StringComparison.Ordinal))
                    {
                        acquainted = true;
                        break;
                    }
                }
            }

            if (!acquainted) return;
            _tutorialStep = 1;
            _tutorialNpcTargetId = null;
            _status = "Вы познакомились с жителем. Дальше доступны свободные занятия.";
        }

        private string TutorialHint()
        {
            if (_frame == null || _frame.Me == null)
                return "Подключитесь к миру, чтобы начать знакомство с деревней.";

            var task = _frame.Me.Task;
            if (task == null)
            {
                return _tutorialStep == 0
                    ? "Шаг 1 — подойдите к жителю и нажмите E. Выберите реплику «Будем знакомы»."
                    : "Сервер пока не передал состояние вводного маршрута.";
            }

            if (task.Issued != null)
                return !string.IsNullOrWhiteSpace(task.Text)
                    ? task.Text
                    : "У вас есть принятое личное поручение жителя.";

            if (task.ErrandDone)
            {
                if (task.Done)
                    return "Вводный маршрут завершён: личное поручение сдано серверу.";
                return "Личное поручение сдано серверу. " +
                    (string.IsNullOrWhiteSpace(task.Text) ? "Загляните в корчму и выпейте пива." : task.Text);
            }

            if (!task.Met)
                return !string.IsNullOrWhiteSpace(task.Text)
                    ? task.Text
                    : "Шаг 1 — подойдите к жителю и нажмите E. Выберите реплику «Будем знакомы».";

            return "Знакомство подтверждено, но личное поручение ещё не принято. " +
                "Поговорите со знакомым жителем; если у него есть актуальная просьба, примите её в окне «Дела». " +
                "Свободная работа развивает навыки, но сама по себе не считается сдачей поручения.";
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
                QueueWorldAction(poi.ActionIntent);
                return;
            }

            RequestPracticeGoal(poi.Goal);
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

            var targetId = npc.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_tutorialStep == 0) _tutorialNpcTargetId = targetId;
            var requestSerial = ++_talkRequestSerial;
            _pendingTalkTargetId = targetId;
            _pendingTalkRequestSerial = requestSerial;
            _confirmedTalkTargetId = null;
            _pendingTalkDeadline = Time.unscaledTime + TalkConfirmationTimeoutSeconds;
            _talkConfirmationTimedOut = false;
            _talkFeedback = "Запрос отправлен. Ожидаю кадр сервера с разговором именно с этим жителем.";
            QueueTalk(targetId, requestSerial);
            _status = "Обращаюсь к жителю. Жду ответ сервера и доступные реплики.";
        }

        private void RequestPracticeGoal(string goal)
        {
            // Practice intents are server-validated, but are not tutorial completion or quest progress.
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

            ResetSessionJournal();
            _busy = true;
            _connectionState = WorldConnectionState.Connecting;
            _frame = null;
            _tutorialNpcTargetId = null;
            _pendingTalkTargetId = null;
            _pendingTalkRequestSerial = 0;
            _confirmedTalkTargetId = null;
            _talkFeedback = null;
            _talkConfirmationTimedOut = false;
            ResetJumpPresentation();
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
            socket.ChatReceived += json => OnSocketChatMessage(socket, json);
            socket.Closed += reason => Enqueue(() => HandleSocketClosed(socket, reason));

            try
            {
                // Layout v2 is required before enabling prediction: local geometry must match the server.
                var http = new HttpAuthClient(serverUrl);
                var layout = await http.GetLayoutAsync();
                _layout = layout;
                // v1.19: read global map metadata before enabling the socket. Older compatible servers
                // may not expose it, so this optional layer must never block the settlement demo.
                try { _worldManifest = await http.GetWorldManifestAsync(); }
                catch (Exception worldError)
                {
                    _worldManifest = null;
                    Enqueue(() => Debug.LogWarning("Карта мира v1.19 пока недоступна; остаёмся в локальном режиме поселения: " + worldError.Message));
                }
                _terrainChunk = null;
                _terrainChunkRequestedId = null;
                _terrainAppliedSettlementId = null;
                _terrainAppliedAnchorEast = double.NaN;
                _terrainAppliedAnchorNorth = double.NaN;
                _terrainAppliedAnchorUp = double.NaN;
                // After a reconnect another settlement's layout may still be on screen: start again from
                // the default one; EnsureLayoutForPlace switches to the right settlement on the first frame.
                var replacing = _layoutSettlementId != null;
                _layoutSettlementId = null;
                _layoutRequestedFor = null;
                if (replacing) WorldLayoutPresentationBuilder.Rebuild(_presentationRoot, layout);
                else WorldLayoutPresentationBuilder.Build(_presentationRoot, layout);
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

            _voiceChat?.StopVoice();
            _socket = null;
            _connectionState = WorldConnectionState.Closed;
            _frame = null;
            _pendingTalkTargetId = null;
            _pendingTalkRequestSerial = 0;
            _confirmedTalkTargetId = null;
            _talkFeedback = null;
            _talkConfirmationTimedOut = false;
            _moveSendInFlight = false;
            _pendingMoveIntents.Clear();
            _moveSendQueue.Clear();
            _moveSendInFlight = false;
            ResetJumpPresentation();
            _views.Clear();
            if (_cameraRig != null) _cameraRig.SetTarget(null);
            socket.Dispose();
            _status = "Связь закрыта: " + reason;
        }

        private void OnSocketMessage(WorldSocketClient socket, string json)
        {
            // Light "fast" frames (requested with ?fast=N) are not full snapshots: they bypass the strict
            // v1 snapshot parser and only refresh the combat layer.
            if (CoreGameplay.IsFastFrame(json))
            {
                Enqueue(() =>
                {
                    if (ReferenceEquals(_socket, socket) && _connectionState == WorldConnectionState.Active && _core != null)
                        _core.ApplyFastFrame(json);
                });
                return;
            }

            try
            {
                var frame = WorldFrameParser.Parse(json);
                Enqueue(() =>
                {
                    // Ignore frames queued by a socket that has already been replaced or closed.
                    if (!ReferenceEquals(_socket, socket) || _connectionState == WorldConnectionState.Closed)
                        return;

                    _frame = frame;
                    EnsureLayoutForPlace(frame);
                    EnsureTerrainChunkForFrame(frame);
                    if (frame.Me != null) _voiceChat?.Bind(socket, frame.Me.Id, _lifetime.Token);
                    CaptureSessionJournal(frame);
                    UpdateTalkConfirmation(frame);
                    UpdateTutorialIntroduction(frame);
                    if (!frame.Alive)
                    {
                        _voiceChat?.StopVoice();
                        _pendingTalkTargetId = null;
                        _pendingTalkRequestSerial = 0;
                        _confirmedTalkTargetId = null;
                        _talkFeedback = null;
                        _talkConfirmationTimedOut = false;
                        _moveSendInFlight = false;
                        _pendingMoveIntents.Clear();
                        _moveSendQueue.Clear();
                        _moveSendAccumulator = 0f;
                        ResetJumpPresentation();
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
                        if (_core != null) _core.ApplyFrame(json);
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



        private void ResetJumpPresentation()
        {
            _jumpOffset = 0f;
            _jumpVelocity = 0f;
            if (_views != null) _views.SetPlayerPresentationOffset(Vector3.zero);
            if (_cameraRig != null) _cameraRig.SetJumpOffset(0f);
        }

        private void UpdateJump()
        {
            // This only offsets the rendered player and camera. The authoritative root,
            // ScenePos, collision resolution and server move packets remain on the ground.
            if (_input != null && _input.JumpPressed && _jumpOffset <= 0.001f)
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
            _audioDirector?.SetPlayerPosition(player.position);

            // The shared Input System Move action currently binds keyboard layouts only.
            // Gamepad/stick support will be added as an explicit, tested binding rather than
            // inheriting arbitrary legacy axes that could drift and move the character.
            var keyboardInput = _input != null ? _input.Move : Vector2.zero;
            var rawAxes = keyboardInput;
            _movementRawAxesDebug = rawAxes;
            var input = Time.timeScale <= 0f ? Vector2.zero : Vector2.ClampMagnitude(keyboardInput, 1f);
            _movementKeyboardDebug = keyboardInput;

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
            _movementServerFacingDebug = (float)_frame.Me.Position.Facing;

            var moving = worldMove.sqrMagnitude > 0.001f;
            if (_input != null && (_input.PrimaryPressed || _input.QuickAttackPressed || _input.DodgePressed)) _audioDirector?.Combat();
            if (_input != null && _input.WeavePressed) _audioDirector?.Magic();
            var runRequested = moving && _input != null && (_input.JogHeld || _input.SprintHeld);
            var fastRequested = runRequested && _input != null && _input.SprintHeld;
            var canRun = runRequested && GetPredictedStamina() > 0f;
            if (moving) _audioDirector?.Footsteps(canRun);
            var speed = canRun ? (fastRequested ? 6.0f : 3.2f) : 1.4f;

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
                Fast = fastRequested,
                Face = facingDegrees,
                DeltaTime = elapsed
            };
            _pendingMoveIntents.Add(intent);
            _moveSendQueue.Enqueue(intent);
            if (moving)
            {
                _discardedMoveIntentCount += MovementIntentQueuePolicy.TrimOldest(
                    _moveSendQueue, _pendingMoveIntents, MoveIntentSequence, MaxQueuedMoveIntents);
            }
            else
            {
                _discardedMoveIntentCount += MovementIntentQueuePolicy.DiscardMovingOnStop(
                    _moveSendQueue, _pendingMoveIntents, MoveIntentSequence, MoveIntentHasMovement);
            }
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
                var speed = canRun ? (intent.Fast ? 6.0f : 3.2f) : 1.4f;
                if (canRun) stamina = Mathf.Max(0f, stamina - 0.10f * dt);
                else stamina = Mathf.Min(1f, stamina + 0.10f * dt);

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
                await socket.SendMoveAsync(intent.Mx, intent.My, intent.Run, intent.Fast, intent.Face,
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
            _voiceChat?.StopVoice();
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
            ResetJumpPresentation();
            _views.Clear();
            _tutorialNpcTargetId = null;
            _pendingTalkTargetId = null;
            _pendingTalkRequestSerial = 0;
            _confirmedTalkTargetId = null;
            _talkFeedback = null;
            _talkConfirmationTimedOut = false;
            if (_cameraRig != null)
            {
                _cameraRig.SetTarget(null);
                _cameraRig.SetMouseGestureCapture(true);
            }
            SetChatOpen(false);
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

        /// <summary>
        /// v1.19: every settlement has its own server layout. When the confirmed place changes, fetch
        /// that settlement's layout and rebuild the presentation and local collision from it. The first
        /// frame after login also lands here, so the tutorial default is replaced only when needed.
        /// </summary>
        /// <summary>
        /// Streams only the chunk containing the authoritative player position. Server coordinates stay
        /// double precision; only the final chunk mesh is rebased to the current settlement anchor.
        /// Seamless server travel remains disabled until multi-chunk presentation and collision are complete.
        /// </summary>
        private void EnsureTerrainChunkForFrame(WorldFrame frame)
        {
            if (_worldManifest == null || frame == null || frame.Me == null ||
                frame.Place == null || frame.Place.Anchor == null ||
                frame.Me.WorldPosition == null || !frame.Me.WorldPosition.IsValid)
                return;

            var anchor = frame.Place.Anchor;
            var position = frame.Me.WorldPosition;
            if (anchor.Frame != position.Frame || position.Frame != 0) return;
            var chunkX = position.Chunk[0];
            var chunkZ = position.Chunk[1];
            var chunkId = position.Frame + ":" + chunkX + ":" + chunkZ;
            if (_terrainChunk != null && _terrainChunk.Id == chunkId)
            {
                var changedAnchor = _terrainAppliedSettlementId != frame.Place.Id ||
                    Math.Abs(_terrainAppliedAnchorEast - anchor.East) > 0.001 ||
                    Math.Abs(_terrainAppliedAnchorNorth - anchor.North) > 0.001 ||
                    Math.Abs(_terrainAppliedAnchorUp - anchor.Up) > 0.001;
                if (changedAnchor)
                {
                    WorldTerrainChunkBuilder.Apply(_presentationRoot, _terrainChunk, anchor);
                    _terrainAppliedSettlementId = frame.Place.Id;
                    _terrainAppliedAnchorEast = anchor.East;
                    _terrainAppliedAnchorNorth = anchor.North;
                    _terrainAppliedAnchorUp = anchor.Up;
                }
                return;
            }
            if (_terrainChunkRequestedId == chunkId) return;
            _terrainChunkRequestedId = chunkId;
            _ = LoadTerrainChunkAsync(chunkX, chunkZ, chunkId);
        }

        private async Task LoadTerrainChunkAsync(long chunkX, long chunkZ, string chunkId)
        {
            try
            {
                var chunk = await new HttpAuthClient(serverUrl).GetTerrainChunkAsync(chunkX, chunkZ, 16);
                Enqueue(() =>
                {
                    if (_terrainChunkRequestedId != chunkId || _frame == null ||
                        _frame.Me == null || _frame.Me.WorldPosition == null ||
                        _frame.Me.WorldPosition.Chunk == null ||
                        _frame.Me.WorldPosition.Chunk[0] != chunkX ||
                        _frame.Me.WorldPosition.Chunk[1] != chunkZ ||
                        _frame.Place == null || _frame.Place.Anchor == null)
                        return;
                    _terrainChunk = chunk;
                    _terrainChunkRequestedId = null;
                    WorldTerrainChunkBuilder.Apply(_presentationRoot, chunk, _frame.Place.Anchor);
                    _terrainAppliedSettlementId = _frame.Place.Id;
                    _terrainAppliedAnchorEast = _frame.Place.Anchor.East;
                    _terrainAppliedAnchorNorth = _frame.Place.Anchor.North;
                    _terrainAppliedAnchorUp = _frame.Place.Anchor.Up;
                });
            }
            catch (Exception exception)
            {
                Enqueue(() =>
                {
                    if (_terrainChunkRequestedId == chunkId) _terrainChunkRequestedId = null;
                    Debug.LogWarning("Не удалось загрузить чанк рельефа " + chunkId + ": " + exception.Message);
                });
            }
        }

        private void EnsureLayoutForPlace(WorldFrame frame)
        {
            var placeId = frame != null && frame.Place != null ? frame.Place.Id : null;
            if (string.IsNullOrEmpty(placeId)) return;
            if (placeId == _layoutSettlementId || placeId == _layoutRequestedFor) return;
            // A server without per-settlement layouts sends no plan; the default layout is already right.
            if (_layoutSettlementId == null && (frame.Place.Plan == null || frame.Place.Plan == "starter"))
            {
                _layoutSettlementId = placeId;
                return;
            }
            _layoutRequestedFor = placeId;
            _ = ReloadLayoutAsync(placeId);
        }

        private async Task ReloadLayoutAsync(string placeId)
        {
            try
            {
                var layout = await new HttpAuthClient(serverUrl).GetLayoutAsync(placeId);
                Enqueue(() =>
                {
                    if (_layoutRequestedFor != placeId) return;
                    _layout = layout;
                    _layoutSettlementId = placeId;
                    _layoutRequestedFor = null;
                    WorldLayoutPresentationBuilder.Rebuild(_presentationRoot, layout);
                });
            }
            catch (Exception exception)
            {
                Enqueue(() =>
                {
                    if (_layoutRequestedFor == placeId) _layoutRequestedFor = null;
                    Debug.LogWarning("Не удалось получить раскладку поселения " + placeId + ": " + exception.Message);
                });
            }
        }

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

            // Runtime-created cameras do not receive Unity's default AudioListener.
            // Without one, every AudioSource in the village is effectively inaudible.
            var listener = camera.GetComponent<AudioListener>();
            if (listener == null) listener = camera.gameObject.AddComponent<AudioListener>();
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            for (var i = 0; i < listeners.Length; i++)
                if (listeners[i] != null && listeners[i] != listener)
                    listeners[i].enabled = false;

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
            _cameraRig.BindInput(_input);
            _interactionProbe.BindInput(_input);
            _cameraRig.SetDefaultFieldOfView(camera.fieldOfView);

            if (FindAnyObjectByType<Light>() == null)
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
            // Keep the village readable while allowing the distant ridgeline (255–305 m)
            // to remain visible beyond the 300×300 m settlement layout.
            RenderSettings.fogStartDistance = 165f;
            RenderSettings.fogEndDistance = 430f;
        }

        private void OnDestroy()
        {
            WorldPoi.Selected -= OnPoiSelected;
            WorldNpc.Selected -= OnNpcSelected;
            _voiceChat?.StopVoice();
            _lifetime.Cancel();
            if (_cameraRig != null)
            {
                _cameraRig.SetTarget(null);
                _cameraRig.SetMouseGestureCapture(true);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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
