using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Diagnostics;
using Pogranichye.Client.Networking.Protocol;
using Pogranichye.Client.Presentation;
using Pogranichye.Client.World;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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
        // The initial snapshot warm-up is socket-scoped so a late callback from an old
        // connection can never become the first frame of a replacement session.
        private readonly InitialFullFrameBuffer _initialFullFrameBuffer = new InitialFullFrameBuffer();
        private CoreGameplay _core; // protocol v1.10 gameplay: combat, weaves, requests, crafting, journeys, party
        private EntityViewRegistry _views;
        private PresentationCameraRig _cameraRig;
        private ClientInput _input;
        private WorldInteractionProbe _interactionProbe;
        private bool _createdCameraRig;
        private Transform _presentationRoot;
        private SettlementLayoutCache _layoutCache;
        private string _login = "";
        private string _password = "";
        private string _status = "Введите учётные данные сервера.";
        private bool _showMainSettings;
        private int _settingsTab = 2;
        private Vector2 _mainMenuScroll;
        private float _masterVolume = 0.85f;
        private float _sfxVolume = 0.9f;
        private float _ambienceVolume = 0.75f;
        private float _mouseSensitivity = 0.035f;
        private bool _invertMouseY;
        private string _rebindingAction;
        private int _rebindingBindingIndex = -1;
        private string _bindingStatus = "Назначения клавиш сохраняются на этом устройстве.";
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
        private string _priorityToSend;
        private WorldFrame _frame;
        private WorldLayoutFrame _layout;
        // The no-id /api/layout response is retained for the tutorial place across settlement switches.
        private WorldLayoutFrame _starterLayout;
        // v1.19 world-streaming foundation. The manifest is metadata; terrain is cached one chunk at a time.
        private WorldManifestFrame _worldManifest;
        private JObject _worldCatalog;
        private float _nextWorldManifestRefreshAt;
        private float _nextSettlementPrefetchAt;
        private string _worldManifestLastPlaceId;
        private bool _worldManifestRefreshInFlight;
        private int _worldManifestGeneration;
        private readonly HashSet<string> _layoutPrefetchInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _layoutPrefetchRetryAfter = new Dictionary<string, float>(StringComparer.Ordinal);
        private const float WorldManifestRefreshSeconds = 20f;
        private const float SettlementLayoutPrefetchRadiusMeters = 1150f;
        private const int MaxConcurrentLayoutPrefetches = 2;
        private const int MaxNearbyLayoutsToPrefetch = 10;
        // Nearby settlement graph is cached per current settlement/manifest, not rebuilt on every IMGUI repaint.
        private readonly List<WorldSettlementFrame> _worldMapNearby = new List<WorldSettlementFrame>();
        private readonly List<double> _worldMapNearbyDistances = new List<double>();
        private readonly Dictionary<string, WorldSettlementFrame> _worldMapNearbyById = new Dictionary<string, WorldSettlementFrame>();
        private WorldManifestFrame _worldMapCachedManifest;
        private string _worldMapCachedSettlementId;
        private int _worldMapCachedFrame = -1;
        private double _worldMapRadius = 500d;
        private readonly Dictionary<string, TerrainChunkFrame> _terrainChunks = new Dictionary<string, TerrainChunkFrame>();
        private readonly Dictionary<string, WorldSpaceChunkFrame> _spaceChunks = new Dictionary<string, WorldSpaceChunkFrame>();
        private WorldAnchorFrame _floatingOriginAnchor;
        private readonly HashSet<string> _terrainChunkRequests = new HashSet<string>();
        private readonly Dictionary<string, CancellationTokenSource> _terrainChunkCancellation =
            new Dictionary<string, CancellationTokenSource>();
        private readonly Dictionary<string, float> _terrainChunkRetryAfter = new Dictionary<string, float>();
        private const float TerrainChunkRetryDelaySeconds = 10f;
        private const int MaxConcurrentTerrainChunkRequests = 3;
        // Keep a one-chunk retention halo around the requested 3x3 window. This lets
        // neighboring global chunks overlap while the player crosses a chunk/settlement edge.
        private const int TerrainChunkRequestRadius = 1;
        private const int TerrainChunkRetentionRadius = 2;
        private int _terrainStreamGeneration;
        private string _terrainStreamSettlementId;
        private bool _hasTerrainCenter;
        private int _terrainCenterFrame = -1;
        private long _terrainCenterX;
        private long _terrainCenterZ;
        private string _terrainAppliedSettlementId;
        private double _terrainAppliedAnchorEast = double.NaN;
        private double _terrainAppliedAnchorNorth = double.NaN;
        private double _terrainAppliedAnchorUp = double.NaN;
        // v1.19: id of the settlement whose layout is currently built; null means the tutorial default.
        private string _layoutSettlementId;
        private string _prototypeSettlementId;
        private string _layoutRequestedFor;
        private int _layoutRequestGeneration;
        private string _layoutFailedFor;
        private float _layoutRetryAfter;
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
        private int _selectedCardIndex = -1;
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
        private bool _firstPerson = true;
        private bool _hudCollapsed;
        private bool _autoWalk;
        private bool _showAdvancedCharacterActions;
        private bool _marketPanelOpen;
        private bool _stablePanelOpen;
        private bool _tavernPanelOpen;
        // Hides an active card table without leaving the tavern, so beer and other tavern actions remain reachable.
        private bool _tavernCardGameHidden;
        private bool _activityPanelOpen;
        private string _activityTargetId;
        private string _activityTargetTitle;
        private string _activityPrimaryAction;
        private bool _systemMenuOpen;
        private HudTab _hudTab = HudTab.Character;
        private bool _movementDebugVisible;
        private string _lastDiagnosticDumpStatus = "Дамп ещё не сохранён";
        private GUIStyle _statusTextStyle;
        private GUIStyle _pendingActionStyle;
        private GUIStyle _objectiveCardStyle;
        private Vector2 _movementInputDebug;
        private Vector2 _movementKeyboardDebug;
        private Vector2 _movementRawAxesDebug;
        private string _pressedKeysDebug = "none";
        private Vector3 _movementDirectionDebug;
        private float _movementCameraYawDebug;
        private float _movementPlayerYawDebug;
        private float _movementServerFacingDebug;
        private Vector2 _hudScroll;
        private Vector2 _contextActionScroll;
        private float _mapZoom = 1f;
        private bool _mapExpanded;
        private Vector2 _mapPan;
        private bool _mapDragging;
        private Vector2 _mapLastPointer;
        private GUIStyle _mapPoiStyle;
        private GUIStyle _mapPoiLabelStyle;
        private GUIStyle _mapPoiLabelRightStyle;
        private GUIStyle _mapNeighborStyle;
        private GUIStyle _mapNeighborLabelStyle;
        private GUIStyle _mapNeighborLabelRightStyle;
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
        private string _movementPlaceId;
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
            _layoutCache = new SettlementLayoutCache(_presentationRoot);
            // Remove stale scene-authored prototype village before the login screen or
            // first server frame can expose it as if it were the real settlement.
            _layoutCache.RemovePrototypeVillageFallbacks();
            var entityRoot = new GameObject("EntityViews").transform;
            entityRoot.SetParent(_presentationRoot, false);
            _views = new EntityViewRegistry(entityRoot);
            WorldPoi.Selected += OnPoiSelected;
            WorldNpc.Selected += OnNpcSelected;
            SetupPresentationScene();
            // No client-generated village fallback: world geometry is built only from server Layout/manifest data.
            _villageSoundscape = _presentationRoot.GetComponent<VillageSoundscape>();
            if (_villageSoundscape == null)
                _villageSoundscape = _presentationRoot.gameObject.AddComponent<VillageSoundscape>();
            _villageSoundscape.Initialize();
            WorldEnvironmentBuilder.Build(_presentationRoot, _cameraRig != null ? _cameraRig.GetComponent<Camera>() : Camera.main);
            _audioDirector = _presentationRoot.gameObject.AddComponent<WorldAudioDirector>();
            _audioDirector.Initialize();
            _views.DrinkPresented += OnDrinkPresented;
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
            var savedQuality = PlayerPrefs.GetInt("Pogranichye.QualityLevel", -1);
            if (savedQuality >= 0 && QualitySettings.names != null && savedQuality < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(savedQuality, true);
            if (PlayerPrefs.HasKey("Pogranichye.Fullscreen"))
            {
                var savedFullscreen = PlayerPrefs.GetInt("Pogranichye.Fullscreen", 0) != 0;
                Screen.fullScreenMode = savedFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Screen.fullScreen = savedFullscreen;
            }
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
            var activeSocket = _socket;
            if (activeSocket != null &&
                _initialFullFrameBuffer.TryTake(activeSocket, DateTime.UtcNow, out var delayedInitialFrame))
                ProcessFullWorldFrame(activeSocket, delayedInitialFrame);
            UpdateWorldStreaming();
            WorldWildSitePresentationBuilder.Update(_presentationRoot, _worldManifest, _frame);
            // Keep spatial player SFX and remote-voice range checks anchored to the
            // authoritative local body, not the scene origin where the director was created.
            var audioPlayer = _views != null && _views.PlayerTransform != null
                ? _views.PlayerTransform : transform;
            _audioDirector?.SetPlayerPosition(audioPlayer.position);
            UpdateServerAudioAnchors(audioPlayer);
            // Keep F3 diagnostics live even on the title/login screen, before movement starts.
            if (_movementDebugVisible && _input != null)
                _pressedKeysDebug = _input.BuildPressedKeysDebug();
            if (_core != null)
                _core.SetContext(_connectionState == WorldConnectionState.Active, _firstPerson, _hudCollapsed,
                    _views != null ? _views.PlayerTransform : null, _cameraRig);
            var currentPlace = _frame != null ? _frame.Place : null;
            var currentPlaceId = currentPlace != null ? currentPlace.Id : null;
            var layoutMatchesCurrentPlace = PoiPositionPolicy.IsLayoutForSettlement(
                _layoutSettlementId, currentPlaceId);
            var layoutForCurrentPlace = layoutMatchesCurrentPlace ? _layout : null;
            var allowLegacyPoiFallback = currentPlace != null &&
                PoiPositionPolicy.AllowsLegacyFallback(currentPlace.Plan);
            _core?.SetLayout(layoutForCurrentPlace, allowLegacyPoiFallback);
            UpdateTalkConfirmationTimeout();
            if (_connectionState != WorldConnectionState.Active)
                SetChatOpen(false);
            else if (!_chatOpen && !_systemMenuOpen && _input != null && _input.ChatPressed)
                SetChatOpen(true);

            // Two top-level menus only: Tab toggles the character journal; Esc/F1 toggles system options.
            if (_connectionState == WorldConnectionState.Active && !_systemMenuOpen &&
                !_chatOpen && _activityMiniGame == ActivityMiniGame.None &&
                _input != null && _input.MenuPressed)
            {
                _hudCollapsed = !_hudCollapsed;
                _core?.SetExternalCharacterMenuOpen(!_hudCollapsed);
            }

            // Dedicated section keys open the character journal directly. They are processed
            // before movement gating so the same key press cannot leak into gameplay movement.
            var characterHotkey = _input == null
                ? CharacterMenuHotkeySection.None
                : CharacterMenuHotkeyPolicy.Resolve(_input.InventoryMenuPressed,
                    _input.JournalMenuPressed, _input.MapMenuPressed);
            if (_connectionState == WorldConnectionState.Active && !_systemMenuOpen &&
                !_chatOpen && _activityMiniGame == ActivityMiniGame.None &&
                characterHotkey != CharacterMenuHotkeySection.None)
            {
                switch (characterHotkey)
                {
                    case CharacterMenuHotkeySection.Inventory:
                        _hudTab = HudTab.Inventory;
                        break;
                    case CharacterMenuHotkeySection.Journal:
                        _hudTab = HudTab.Journal;
                        break;
                    case CharacterMenuHotkeySection.Map:
                        _hudTab = HudTab.Map;
                        break;
                }
                _hudCollapsed = false;
                _core?.SetExternalCharacterMenuOpen(true);
            }

            if (_input != null && _input.ToggleHudPressed)
                SetSystemMenuOpen(!_systemMenuOpen);
            else if (_input != null && _input.CancelPressed && _activityMiniGame == ActivityMiniGame.None)
            {
                if (_mapExpanded) _mapExpanded = false;
                else if (_chatOpen) SetChatOpen(false);
                else if (_activityPanelOpen) _activityPanelOpen = false;
                else if (_marketPanelOpen) _marketPanelOpen = false;
                else if (_stablePanelOpen) _stablePanelOpen = false;
                else if (_tavernPanelOpen) _tavernPanelOpen = false;
                else if (!_hudCollapsed)
                {
                    _hudCollapsed = true;
                    _core?.SetExternalCharacterMenuOpen(false);
                }
                else SetSystemMenuOpen(!_systemMenuOpen);
            }

            // F1/Esc opens a modal system menu; TAB remains the character menu.
            // Any open gameplay/system window owns input and prevents movement/camera leakage.
            var pointerOverUi = IsPointerOverUi();
            var contextualWindowOpen = _marketPanelOpen || _stablePanelOpen || _tavernPanelOpen || _activityPanelOpen;
            var hardGameplayInputBlock = _systemMenuOpen || _chatOpen || contextualWindowOpen ||
                (_core != null && _core.CapturesInput) || _activityMiniGame != ActivityMiniGame.None;
            var gameplayInputBlocked = hardGameplayInputBlock || pointerOverUi;
            // Never let auto-walk resume unexpectedly after a modal, chat, minigame or disconnect.
            // Merely hovering the small auto-walk control pauses movement but preserves its state.
            if (hardGameplayInputBlock || _connectionState != WorldConnectionState.Active)
                _autoWalk = false;
            else if (_input != null && _input.AutoWalkTogglePressed)
                _autoWalk = !_autoWalk;
            _core?.SetExternalUiCapture(_systemMenuOpen || _chatOpen || contextualWindowOpen || pointerOverUi ||
                _activityMiniGame != ActivityMiniGame.None);
            _core?.SetExternalModalMenuOpen(_systemMenuOpen || contextualWindowOpen);
            _core?.SetExternalCharacterMenuOpen(_connectionState == WorldConnectionState.Active &&
                !_systemMenuOpen && !_hudCollapsed);
            _voiceChat?.SetPushToTalkHeld(_connectionState == WorldConnectionState.Active &&
                !_systemMenuOpen && !_chatOpen && _input != null && _input.VoicePushToTalkHeld);
            _cameraRig?.SetMouseGestureCapture(_connectionState != WorldConnectionState.Active || gameplayInputBlocked);
            // Unity can release a locked cursor after focus changes. Re-assert capture only
            // while active gameplay owns input; every menu/chat/context/minigame path releases it.
            var captureGameplayCursor = _connectionState == WorldConnectionState.Active &&
                !gameplayInputBlocked && Application.isFocused;
            Cursor.lockState = captureGameplayCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captureGameplayCursor;
            if (_connectionState != WorldConnectionState.Active && _drinkRequestPending)
                _drinkRequestPending = false; // RequestGate is cleared on disconnect; do not leave social UI locked.
            if (_activityMiniGame != ActivityMiniGame.None && _input != null)
            {
                if (_input.CancelPressed)
                {
                    _activityMiniGame = ActivityMiniGame.None;
                    _activityMiniGameFeedback = "Работа прервана.";
                }
                else if (_input.JumpPressed)
                {
                    // Space is the primary mini-game action while a mini-game owns input;
                    // it must never leak through as a character jump.
                    HandleActivityMiniGamePrimaryAction();
                }
            }
            if (_input != null && _input.ToggleDiagnosticsPressed) _movementDebugVisible = !_movementDebugVisible;
            if (_input != null && _input.ToggleFullscreenPressed) ToggleFullscreen();
            if (_interactionProbe != null)
                _interactionProbe.enabled = _connectionState == WorldConnectionState.Active;

            if (_connectionState == WorldConnectionState.Active && !gameplayInputBlocked)
            {
                UpdateJump();
                UpdateMovement();
                UpdateDialogueChoiceInput();
                if (_core == null || !_core.CapturesInput)
                {
                    if (_input != null && _input.StartWoodcutPressed)
                        StartActivityMiniGame(ActivityMiniGame.Woodcutting);
                    else if (_input != null && _input.StartFishingPressed)
                        StartActivityMiniGame(ActivityMiniGame.Fishing);
                }
            }

            if (_connectionState == WorldConnectionState.Active && !string.IsNullOrEmpty(_priorityToSend))
            {
                var goal = _priorityToSend;
                _priorityToSend = null;
                _ = SendCharacterPriorityAsync(goal);
            }
        }

        private bool IsPointerOverUi()
        {
            if (_systemMenuOpen || _chatOpen || _mapExpanded) return true;
            if (_input == null) return false;
            var pointer = _input.PointerPosition;
            var guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);

            if (!_hudCollapsed)
            {
                var panelWidth = Mathf.Min(500f, Screen.width - 24f);
                var panelHeight = Mathf.Min(620f, Screen.height - 24f);
                if (new Rect(12f, 12f, panelWidth, panelHeight).Contains(guiPoint)) return true;
            }

            if (_frame?.Me == null) return false;
            if (GetContextActionRect().Contains(guiPoint)) return true;

            if (_activityMiniGame != ActivityMiniGame.None)
            {
                var miniWidth = Mathf.Min(420f, Screen.width - 24f);
                if (new Rect((Screen.width - miniWidth) * 0.5f, Screen.height - 286f,
                    miniWidth, 178f).Contains(guiPoint)) return true;
            }

            if (new Rect(Screen.width - 124f, 12f, 112f, 28f).Contains(guiPoint)) return true;
            if (_hudCollapsed && GetAutoWalkControlRect().Contains(guiPoint)) return true;
            if (_frame.Me.Talk?.Options != null && _frame.Me.Talk.Options.Count > 0 &&
                new Rect(Screen.width - 450f, Screen.height - 540f, 430f, 520f).Contains(guiPoint)) return true;
            if (_frame.Me.InviteFrom != null &&
                new Rect(Screen.width - 450f, Screen.height - 170f, 430f, 150f).Contains(guiPoint)) return true;
            return false;
        }

        private void OnGUI()
        {
            ReleaseLocalMapCaptureIfInactive();
            CoreGameplay.ApplyUiTheme();
            if (_connectionState != WorldConnectionState.Active || _frame == null || !_frame.Alive || _systemMenuOpen)
            {
                DrawTitleLoadingBackdrop();
                DrawMovementDebugOverlay();
                return;
            }

            // Server-authoritative calendar/clock remains visible while the player is in the world.
            DrawWorldClockOverlay();

            if (_mapExpanded)
            {
                DrawExpandedMapOverlay();
                return;
            }

            if (!_hudCollapsed)
            {
                // A journal-style overlay replaces the narrow prototype strip. Keep the world
                // visible around it and reserve a stable navigation rail beside the page content.
                var panelWidth = Mathf.Min(1600f, Screen.width * 0.88f);
                var panelHeight = Mathf.Min(1000f, Screen.height * 0.88f);
                var panelRect = new Rect(
                    (Screen.width - panelWidth) * 0.5f,
                    (Screen.height - panelHeight) * 0.5f,
                    panelWidth, panelHeight);
                GUI.Box(panelRect, "ДНЕВНИК ПЕРСОНАЖА");
                GUILayout.BeginArea(new Rect(panelRect.x + 18f, panelRect.y + 42f,
                    panelRect.width - 36f, panelRect.height - 58f));

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(184f),
                    GUILayout.ExpandHeight(true));
                GUILayout.Label("РАЗДЕЛЫ");
                DrawHudTabButton("Персонаж", HudTab.Character);
                DrawHudTabButton("Вещи  [I]", HudTab.Inventory);
                DrawHudTabButton("Журнал  [J]", HudTab.Journal);
                DrawHudTabButton("Карта  [M]", HudTab.Map);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Tab — закрыть");
                GUILayout.Label("Esc — настройки");
                GUILayout.EndVertical();

                GUILayout.Space(12f);
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUILayout.BeginHorizontal();
                GUILayout.Label(_frame.Place != null
                    ? $"ДЕНЬ {_frame.Day}  ·  {(_frame.Hour.HasValue ? _frame.Hour.Value.ToString("00", CultureInfo.InvariantCulture) + ":00" : "--:--")}  ·  ГОД {_frame.Year}  ·  {_frame.Season}  ·  {_frame.Place.Name}"
                    : "Мир загружается");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Закрыть  ×", GUILayout.Width(112f), GUILayout.Height(32f)))
                {
                    _hudCollapsed = true;
                    _core?.SetExternalCharacterMenuOpen(false);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);

                _hudScroll = GUILayout.BeginScrollView(_hudScroll,
                    GUILayout.ExpandWidth(true), GUILayout.Height(panelHeight - 150f));
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
                }
                GUILayout.EndScrollView();

                EnsureStatusTextStyle();
                GUILayout.Label(_status, _statusTextStyle, GUILayout.MinHeight(44f));
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }

            DrawMovementDebugOverlay();
            DrawSocialOverlay();
            DrawPendingActionIndicator();
            if (_hudCollapsed)
            {
                DrawObjectiveCard();
                DrawAutoWalkControl();
            }
            if (_chatOpen)
                DrawChatOverlay();
            else
            {
                // Contextual actions remain visible beside the journal on wide screens.
                // On narrow screens they move to the collapsed-HUD layout to avoid overlap.
                DrawContextActions();
                if (_hudCollapsed && _activityMiniGame != ActivityMiniGame.None)
                {
                    var miniWidth = Mathf.Min(420f, Screen.width - 24f);
                    GUILayout.BeginArea(new Rect((Screen.width - miniWidth) * 0.5f,
                        Screen.height - 286f, miniWidth, 178f));
                    DrawActivityMiniGame();
                    GUILayout.EndArea();
                }
                if (GUI.Button(new Rect(Screen.width - 124f, 12f, 112f, 28f), "Чат [T]"))
                    SetChatOpen(true);
            }
        }

        private void DrawWorldClockOverlay()
        {
            if (_frame == null) return;
            var time = _frame.Hour.HasValue
                ? _frame.Hour.Value.ToString("00", CultureInfo.InvariantCulture) + ":00"
                : "--:--";
            var calendar = "ДЕНЬ " + _frame.Day.ToString(CultureInfo.InvariantCulture) +
                "  ·  " + time + "  ·  " + (_frame.Season ?? "") +
                "  ·  ГОД " + _frame.Year.ToString(CultureInfo.InvariantCulture);
            var rect = new Rect(12f, 12f, Mathf.Min(430f, Screen.width - 148f), 34f);
            GUI.Box(rect, calendar);
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
                if (GUILayout.Button("Сбросить настройки", GUILayout.Height(34f))) ResetSettingsToDefaults();
                if (GUILayout.Button("Назад", GUILayout.Height(40f))) _showMainSettings = false;
            }
            else
            {
                GUILayout.Label(_connectionState == WorldConnectionState.Active && _frame != null && _frame.Alive ? "СИСТЕМНОЕ МЕНЮ" : "ПОДКЛЮЧЕНИЕ К МИРУ");
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
                else if (_connectionState == WorldConnectionState.Active && _frame != null && _frame.Alive)
                {
                    GUILayout.Label("СИСТЕМНАЯ ИНФОРМАЦИЯ");
                    GUILayout.Label("Аккаунт: " + _login);
                    GUILayout.Label("Состояние: в мире");
                    GUILayout.Label("Место: " + (_frame.Place != null ? _frame.Place.Name : "не определено"));
                    GUILayout.Label("Сервер: " + serverUrl);
                    if (GUILayout.Button("Продолжить игру [F1 / Esc]", GUILayout.Height(42f)))
                        SetSystemMenuOpen(false);
                    if (GUILayout.Button("Отключиться от мира", GUILayout.Height(34f)))
                        DisconnectWorld("Отключено от мира. Можно подключиться снова.");
                    if (GUILayout.Button("Выйти из аккаунта", GUILayout.Height(32f))) Logout();
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
                if (GUILayout.Button(Screen.fullScreen ? "Оконный режим [F11]" : "Полный экран [F11]", GUILayout.Height(30f)))
                    ToggleFullscreen();
                if (GUILayout.Button("Настройки", GUILayout.Height(34f))) _showMainSettings = true;
                if (GUILayout.Button("Выход из игры", GUILayout.Height(34f))) Application.Quit();
            }
            GUILayout.Space(8f);
            EnsureStatusTextStyle();
            GUILayout.Label(_status, _statusTextStyle, GUILayout.MinHeight(48f));
            if (_connectionState == WorldConnectionState.Connecting || _connectionState == WorldConnectionState.AwaitingSnapshot)
                GUILayout.Label(_connectionState == WorldConnectionState.Connecting ? "Устанавливаем связь…" : "Получаем состояние мира…");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.enabled = previousEnabled;
        }

        private void ToggleFullscreen()
        {
            var enable = !Screen.fullScreen;
            Screen.fullScreenMode = enable ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.fullScreen = enable;
            SaveInt("Pogranichye.Fullscreen", enable ? 1 : 0);
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
            GUI.Box(rect, "Чат поблизости · T открыть · Enter отправить");
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
            var chatEvent = Event.current;
            if (canSend && chatEvent != null && chatEvent.type == EventType.KeyDown &&
                (chatEvent.keyCode == KeyCode.Return || chatEvent.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == "PogranichyeChatInput")
            {
                SendCurrentChatMessage();
                chatEvent.Use();
            }
            GUI.enabled = canSend;
            if (GUILayout.Button("Отправить", GUILayout.Width(92f), GUILayout.Height(28f)))
                SendCurrentChatMessage();
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
            GUILayout.Label("Голос: войдите в канал. Кнопка включает открытый микрофон; удерживайте P, чтобы говорить только пока клавиша нажата. Для PTT один раз разрешите доступ к микрофону.");
            GUILayout.EndArea();
        }

        private void SendCurrentChatMessage()
        {
            var text = _chatInput.Trim();
            if (string.IsNullOrEmpty(text) || _busy) return;
            _chatInput = "";
            _ = SendChatMessageAsync(text);
            _chatFocusPending = true;
        }

        private void SetSystemMenuOpen(bool open)
        {
            if (_systemMenuOpen == open) return;
            _systemMenuOpen = open;
            if (open)
            {
                _hudCollapsed = true;
                _core?.SetExternalCharacterMenuOpen(false);
                SetChatOpen(false);
                if (_activityMiniGame != ActivityMiniGame.None)
                {
                    _activityMiniGame = ActivityMiniGame.None;
                    _activityMiniGameFeedback = "Работа прервана.";
                }
            }
            _core?.SetExternalModalMenuOpen(open);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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

            var rect = new Rect(Screen.width - 430f, 12f, 418f, 390f);
            GUI.Box(rect, "ТЕХНИЧЕСКАЯ ДИАГНОСТИКА [F3]");
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
            var neighbors = _frame?.Place?.Neighbors?.Count ?? 0;
            var views = _views?.RemoteViewCount ?? 0;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 198f, rect.width - 20f, 20f),
                "NPC AOI: " + neighbors + " | визуальных сущностей: " + views);
            if (_frame?.Me?.Position != null)
                GUI.Label(new Rect(rect.x + 10f, rect.y + 218f, rect.width - 20f, 20f),
                    "ScenePos: " + _frame.Me.Position.X.ToString("0.0", CultureInfo.InvariantCulture) +
                    ", " + _frame.Me.Position.Y.ToString("0.0", CultureInfo.InvariantCulture) +
                    " · facing " + _frame.Me.Position.Facing.ToString("0.0", CultureInfo.InvariantCulture));
            if (_frame?.Me?.WorldPosition != null)
                GUI.Label(new Rect(rect.x + 10f, rect.y + 238f, rect.width - 20f, 20f),
                    "WorldPos: " + _frame.Me.WorldPosition.East.ToString("0.0", CultureInfo.InvariantCulture) +
                    ", " + _frame.Me.WorldPosition.North.ToString("0.0", CultureInfo.InvariantCulture) +
                    " · frame " + _frame.Me.WorldPosition.Frame +
                    " · space " + (_frame.Me.WorldPosition.SpaceId ?? "—") +
                    " · level " + (_frame.Me.WorldPosition.LevelId ?? "—"));
            GUI.Label(new Rect(rect.x + 10f, rect.y + 258f, rect.width - 20f, 20f),
                "Move queue: " + _moveSendQueue.Count + "/" + MaxQueuedMoveIntents +
                " · pending " + _pendingMoveIntents.Count + " · dropped " + _discardedMoveIntentCount);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 278f, rect.width - 20f, 20f),
                "Place: " + (_frame?.Place?.Id ?? "—") + " · terrain chunks: " + _terrainChunks.Count +
                " · in flight: " + _terrainChunkRequests.Count +
                " · step: " + GetTerrainStepSummary() + " · biome: " + GetTerrainBiomeSummary());
            if (GUI.Button(new Rect(rect.x + 10f, rect.y + 304f, rect.width - 20f, 28f), "СОХРАНИТЬ ДАМП ДЛЯ ОТЛАДКИ"))
                SaveClientDiagnosticDump();
            GUI.Label(new Rect(rect.x + 10f, rect.y + 336f, rect.width - 20f, 24f), _lastDiagnosticDumpStatus);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 360f, rect.width - 20f, 22f),
                "Локальный файл: включает координаты и состояние персонажа.");
        }

        private string GetTerrainStepSummary()
        {
            var steps = new HashSet<int>();
            foreach (var chunk in _terrainChunks.Values)
                if (chunk != null && chunk.StepMeters > 0) steps.Add(chunk.StepMeters);
            if (steps.Count == 0) return "—";
            var values = new List<int>(steps);
            values.Sort();
            return string.Join("/", values.ConvertAll(value => value.ToString(CultureInfo.InvariantCulture)).ToArray()) + "m";
        }

        private string GetTerrainBiomeSummary()
        {
            var biomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in _terrainChunks.Values)
                if (!string.IsNullOrWhiteSpace(chunk?.Biome)) biomes.Add(chunk.Biome);
            if (biomes.Count == 0) return "—";
            var values = new List<string>(biomes);
            values.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", values.ToArray());
        }

        private static Rect GetAutoWalkControlRect()
        {
            return new Rect(12f, Screen.height - 58f, 214f, 44f);
        }

        private void DrawAutoWalkControl()
        {
            var rect = GetAutoWalkControlRect();
            var previousColor = GUI.color;
            var previousBackground = GUI.backgroundColor;
            var previousContent = GUI.contentColor;
            GUI.color = Color.white;
            GUI.backgroundColor = _autoWalk
                ? new Color(0.30f, 0.48f, 0.31f, 0.98f)
                : new Color(0.13f, 0.12f, 0.10f, 0.96f);
            GUI.contentColor = _autoWalk
                ? new Color(0.96f, 0.98f, 0.88f, 1f)
                : new Color(0.91f, 0.87f, 0.77f, 1f);
            var label = _autoWalk ? "АВТОХОДЬБА  ВКЛ  [ = ]" : "АВТОХОДЬБА  [ = ]";
            if (GUI.Button(rect, label))
                _autoWalk = !_autoWalk;
            GUI.color = previousColor;
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private void DrawObjectiveCard()
        {
            var message = TutorialObjectiveHint();
            if (string.IsNullOrWhiteSpace(message)) return;

            if (_objectiveCardStyle == null)
            {
                _objectiveCardStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = ClientUiTypography.HudBody,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(12, 12, 8, 8)
                };
                _objectiveCardStyle.normal.textColor = new Color(0.94f, 0.88f, 0.75f, 1f);
            }

            var width = Mathf.Min(380f, Mathf.Max(250f, Screen.width * 0.32f));
            var content = new GUIContent(message);
            var height = Mathf.Max(62f, _objectiveCardStyle.CalcHeight(content, width - 24f) + 34f);
            height = Mathf.Min(height, Mathf.Max(92f, Screen.height * 0.28f));
            var rect = new Rect(12f, 12f, width, height);
            var previousColor = GUI.color;
            GUI.color = new Color(0.10f, 0.085f, 0.065f, 0.90f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = new Color(0.82f, 0.68f, 0.45f, 1f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 24f), "СЛЕДУЮЩИЙ ШАГ",
                new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = ClientUiTypography.HudSecondary });
            GUI.color = new Color(0.94f, 0.88f, 0.75f, 1f);
            GUI.Label(new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f), content, _objectiveCardStyle);
            GUI.color = previousColor;
        }

        private string TutorialObjectiveHint()
        {
            if (_frame == null || _frame.Me == null)
                return "Подключитесь к миру, чтобы начать.";

            var task = _frame.Me.Task;
            if (task == null || !task.Met)
                return _tutorialStep == 0
                    ? "Подойдите к жителю, наведитесь и нажмите E. Выберите «Будем знакомы»."
                    : "Ожидаем от сервера следующий шаг вводного маршрута.";

            if (task.Issued != null)
                return !string.IsNullOrWhiteSpace(task.Text) ? task.Text : "Выполните принятое поручение жителя.";

            if (task.ErrandDone)
                return task.Done ? string.Empty : "Поручение сдано. Загляните в корчму и выпейте пива.";

            return "Вы знакомы с жителем. Поговорите с ним и примите актуальное поручение в окне «Дела».";
        }

        private void DrawPendingActionIndicator()
        {
            if (_core == null || !_core.HasPendingRequests) return;

            if (_pendingActionStyle == null)
            {
                _pendingActionStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = ClientUiTypography.HudBody,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(12, 12, 7, 7)
                };
                _pendingActionStyle.normal.textColor = new Color(0.96f, 0.88f, 0.70f, 1f);
            }

            var width = Mathf.Min(440f, Mathf.Max(220f, Screen.width - 540f));
            var label = _core.PendingRequestLabel;
            var message = string.IsNullOrWhiteSpace(label)
                ? "Ожидаем подтверждения мира…"
                : "Ожидаем подтверждения мира: " + label;
            if (_core.PendingRequestCount > 1)
                message += "  ·  в очереди: " + (_core.PendingRequestCount - 1);
            var height = Mathf.Max(42f, _pendingActionStyle.CalcHeight(new GUIContent(message), width - 24f) + 10f);
            var topOffset = 50f;
            var neighbors = _frame != null && _frame.Place != null ? _frame.Place.Neighbors : null;
            if (neighbors != null && IsAtTavern())
            {
                var inviteableCount = 0;
                foreach (var neighbor in neighbors)
                    if (neighbor != null && neighbor.Controlled) inviteableCount++;
                if (inviteableCount > 0) topOffset = 18f + 56f + inviteableCount * 34f + 8f;
            }
            var rect = new Rect(Screen.width - width - 12f, topOffset, width, height);
            var previousColor = GUI.color;
            GUI.color = new Color(0.10f, 0.085f, 0.065f, 0.94f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = new Color(0.91f, 0.76f, 0.49f, 1f);
            GUI.Label(rect, message, _pendingActionStyle);
            GUI.color = previousColor;
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
            GUILayout.Label($"{me.Name} {me.Surname}");
            GUILayout.Label($"Здоровье {me.Health:0} | Голод {me.Hunger:0} | Энергия {me.Energy:0} | Настрой {me.Morale:0}");
            GUILayout.Label($"Монеты {me.Coins:0.0} | Долг {me.Debt:0.0}");
            GUILayout.Label($"Выносливость: {_serverStamina * 100f:0}% | WASD — движение, Shift — бег, Space — прыжок");
            if (me.Trail != null && !string.IsNullOrWhiteSpace(me.Trail.Kind))
                GUILayout.Label((me.Trail.Kind == "road" ? "🛤 Тракт" : "👣 Тропка") +
                    ": " + (me.Trail.FromSettlement ?? "?") + " — " + (me.Trail.ToSettlement ?? "?") +
                    " · шаг легче ×" + me.Trail.Boost.ToString("0.00", CultureInfo.InvariantCulture));
            GUILayout.Label("Автоходьба: кнопка внизу экрана или =. Любое ручное направление отключает её.");

            if (GUILayout.Button(_firstPerson ? "Переключить на вид от третьего лица" : "Переключить на вид от первого лица"))
                SetCameraMode(!_firstPerson);
            GUILayout.Label(_firstPerson
                ? "WASD — движение; Shift — бег; Space — прыжок; мышь — осмотр; ПКМ — альтернативный режим предмета/инструмента."
                : "WASD — движение; Shift — бег; Space — прыжок; мышь — обзор камеры; колесо — зум; ПКМ — альтернативный режим предмета/инструмента.");
            GUILayout.Space(4f);
            GUILayout.Label("ЗНАКОМСТВО И ПРИОРИТЕТЫ ПЕРСОНАЖА");
            GUILayout.Label(TutorialHint(), GUILayout.Height(58f));
            if (me.Result != null && !string.IsNullOrEmpty(me.Result.RequestId))
                GUILayout.Label(me.Result.Ok
                    ? "Последнее действие выполнено."
                    : "Не удалось выполнить: " + (me.Result.Reason ?? "попробуйте ещё раз."));
            GUILayout.Label(_tutorialStep == 0
                ? "Сначала познакомьтесь с жителем: наведитесь на него и нажмите E. Выберите реплику «Будем знакомы»."
                : "Следующий шаг вводного маршрута отмечен в журнале. Дополнительные занятия необязательны.");
            if (GUILayout.Button(_showAdvancedCharacterActions
                ? "Скрыть приоритеты и дополнительные занятия"
                : "Показать приоритеты и дополнительные занятия"))
                _showAdvancedCharacterActions = !_showAdvancedCharacterActions;

            if (_showAdvancedCharacterActions)
            {
                GUILayout.Space(4f);
                GUILayout.Label("ДОЛГОСРОЧНЫЕ ПРИОРИТЕТЫ · В ТОМ ЧИСЛЕ ДЛЯ ОФЛАЙНА");
                GUILayout.Label("Это не запускает действие в конкретной точке. Вы задаёте серверу общий приоритет персонажа; конкретное занятие и его результат выбирает и симулирует сервер.");
                if (GUILayout.Button("🔨 Приоритет: труд и ремесло")) RequestOfflinePriority("work");
                if (GUILayout.Button("🍞 Приоритет: добывать пищу")) RequestOfflinePriority("eat");
                if (GUILayout.Button("🏠 Приоритет: отдыхать")) RequestOfflinePriority("rest");
                if (GUILayout.Button("🍺 Приоритет: общение")) RequestOfflinePriority("socialize");
            }

            GUILayout.Space(5f);
            GUILayout.Label("🍺 РАЗГОВОР И ПИТЬЁ");
            GUILayout.Label("Заговорите с жителем клавишей E: сервер даст варианты реплик, знакомство откроет имя. У корчмы можно угостить знакомого; входящее приглашение игрока появится здесь.", GUILayout.Height(54f));
            _core?.DrawPartyActions();

            if (_showAdvancedCharacterActions)
            {
                GUILayout.Space(6f);
                GUILayout.Label("ДРУГИЕ ЗАНЯТИЯ");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🪓 Рубка — мини-игра")) StartActivityMiniGame(ActivityMiniGame.Woodcutting);
                if (GUILayout.Button("🎣 Рыбалка — мини-игра")) StartActivityMiniGame(ActivityMiniGame.Fishing);
                if (GUILayout.Button("✨ Разряд")) QueueWorldAction("cast");
                GUILayout.EndHorizontal();
                GUILayout.Label("G — начать рубку у леса · K — начать рыбалку у воды. Нужен топор/удочка в руке.");
                GUILayout.Label("Мана: " + me.Mana.ToString("0.0"));
                if (me.Weave != null)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label("ПЛЕТЕНИЯ");
                    GUILayout.Label("Дар: " + (me.Weave.Gift ? "пробуждён" : "не пробуждён") +
                        " · предел сложности " + me.Weave.Limit.ToString("0.#") +
                        " · ёмкость ума " + me.Weave.Capacity.ToString("0.#"));
                    if (me.Weave.Known != null && me.Weave.Known.Count > 0)
                    {
                        GUILayout.Label("Знакомые узоры:");
                        foreach (var schema in me.Weave.Known)
                        {
                            if (schema == null || string.IsNullOrWhiteSpace(schema.Name)) continue;
                            var nodes = schema.Nodes != null ? string.Join(" → ", schema.Nodes.ToArray()) : "";
                            GUILayout.Label("• " + schema.Name + (nodes.Length > 0 ? " — " + nodes : ""));
                        }
                    }
                    else GUILayout.Label("Знакомые узоры пока не открыты.");
                    if (me.Weave.CanLearn && GUILayout.Button("Медитировать и постигать плетения"))
                        QueueWorldAction("meditate");
                    if (me.Weave.CanStudy && GUILayout.Button("Учиться у мага рядом"))
                        QueueWorldAction("study");
                    if (!me.Weave.CanLearn && !me.Weave.CanStudy)
                        GUILayout.Label("Сейчас некому учить и нет места силы поблизости.");
                }
            }
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
            var task = me.Task;

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("ДНЕВНИК ПЕРСОНАЖА");
            GUILayout.Label($"День {_frame.Day} · {_frame.Season} · год {_frame.Year}");
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(task != null && task.Done && task.ErrandDone ? "Путь продолжается" : "Текущие дела");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("АКТИВНЫЕ ЗАДАНИЯ");
            if (task == null)
            {
                GUILayout.Label("Сервер пока не передал задания. Старые версии мира остаются совместимыми.");
            }
            else
            {
                var completed = (task.Met ? 1 : 0) + (task.Worked ? 1 : 0) + (task.Drank ? 1 : 0);
                const int milestones = 3;
                GUILayout.Label("Первые шаги в поселении");
                DrawQuestProgress(completed / (float)milestones,
                    completed + " из " + milestones + " важных вех");
                DrawQuestStep("Познакомиться с жителем", task.Met);
                DrawQuestStep("Попробовать себя в труде", task.Worked);
                DrawQuestStep("Разделить кружку в корчме", task.Drank);

                if (task.Issued != null)
                {
                    var errand = task.Issued;
                    var giver = string.IsNullOrWhiteSpace(errand.GiverName) ? "Житель поселения" : errand.GiverName;
                    var good = OpeningErrandGoodLabel(errand.Good);
                    GUILayout.Space(8f);
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("ПОРУЧЕНИЕ · " + giver);
                    GUILayout.Label("Собрать и принести: " + good);
                    var quantity = Mathf.Max(1, errand.Quantity);
                    var have = Mathf.Clamp(errand.Have, 0, quantity);
                    DrawQuestProgress(have / (float)quantity, have + " / " + quantity);
                    if (!string.IsNullOrWhiteSpace(errand.SettlementName))
                        GUILayout.Label("Место: " + errand.SettlementName);
                    else if (!string.IsNullOrWhiteSpace(errand.SettlementId))
                        GUILayout.Label("Поселение: " + errand.SettlementId);
                    GUILayout.Label(errand.Ready
                        ? "Всё собрано. Вернись к тому, кто поручил работу."
                        : "Поручение активно. Прогресс обновляется по данным мира.");
                    GUILayout.Label("Награда: " + errand.Coins.ToString("0.##") + " монет");
                    GUILayout.EndVertical();
                }
                else if (task.ErrandDone)
                {
                    GUILayout.Label("✓ Личное поручение выполнено и сохранено миром.");
                }
                else if (task.Met)
                {
                    GUILayout.Label("Поговори со знакомым жителем, чтобы узнать о доступной работе.");
                }
                else
                {
                    GUILayout.Label("Задания и поручения появляются из общения с жителями, а не из меню.");
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            if (_core != null) _core.DrawQuestJournal();

            GUILayout.Space(6f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("ЛЕТОПИСЬ");
            if (me.Journal == null)
                GUILayout.Label("Сохранённая летопись пока недоступна.");
            else if (me.Journal.Count == 0)
                GUILayout.Label("Здесь будут оставаться важные события жизни персонажа.");
            else
            {
                foreach (var entry in me.Journal)
                {
                    if (entry == null) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Д" + entry.Day, GUILayout.Width(42f));
                    GUILayout.BeginVertical();
                    GUILayout.Label(JournalKindLabel(entry.Kind));
                    GUILayout.Label(entry.Text);
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3f);
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("ПОСЛЕДНЕЕ СОБЫТИЕ");
            GUILayout.Label(string.IsNullOrEmpty(me.Last) ? "Пока нет новых событий." : me.Last);
            if (me.Result != null && !string.IsNullOrEmpty(me.Result.RequestId))
                GUILayout.Label((me.Result.Ok ? "Подтверждено миром · " : "Отказ · ") +
                    (me.Result.Reason ?? me.Result.RequestId));
            GUILayout.EndVertical();

            if (_sessionJournal.Count > 0)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("СЕГОДНЯ В ЭТОМ СЕАНСЕ");
                foreach (var entry in _sessionJournal)
                    GUILayout.Label("• " + entry);
                GUILayout.Label("Это заметки текущего подключения, не сохранённые сервером записи.");
                GUILayout.EndVertical();
            }
        }

        private static void DrawQuestStep(string label, bool done)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(done ? "✓" : "○", GUILayout.Width(22f));
            GUILayout.Label(label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(done ? "Готово" : "Ожидает", GUILayout.Width(76f));
            GUILayout.EndHorizontal();
        }

        private static void DrawQuestProgress(float value, string caption)
        {
            GUILayout.Label(caption);
            var rect = GUILayoutUtility.GetRect(1f, 9f, GUILayout.ExpandWidth(true));
            var old = GUI.color;
            GUI.color = new Color(0.12f, 0.13f, 0.11f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.66f, 0.53f, 0.33f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), Texture2D.whiteTexture);
            GUI.color = old;
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

        private void DrawExpandedMapOverlay()
        {
            var panel = new Rect(12f, 12f, Screen.width - 24f, Screen.height - 24f);
            GUI.Box(panel, "КАРТА МИРА · РАСШИРЕННЫЙ РЕЖИМ");
            GUILayout.BeginArea(new Rect(panel.x + 16f, panel.y + 30f, panel.width - 32f, panel.height - 44f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Карта мира · серверная география · Esc — закрыть");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Свернуть  ×", GUILayout.Width(116f), GUILayout.Height(32f)))
                _mapExpanded = false;
            GUILayout.EndHorizontal();
            _hudScroll = GUILayout.BeginScrollView(_hudScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawMapTab();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMapTab()
        {
            GUILayout.Label("КАРТА МИРА");
            GUILayout.Label("Единая карта: от дорог и городов до улиц и зданий. География и положение поселений берутся из данных сервера; неизвестные персонажу места не подписываются.");
            var place = _frame?.Place;
            var anchor = place?.Anchor;
            var manifest = _worldManifest;
            var worldRadius = ResolveUnifiedMapRadius(manifest, anchor);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Масштаб " + _mapZoom.ToString("0.0") + "×", GUILayout.Width(100f));
            var nextZoom = GUILayout.HorizontalSlider(_mapZoom, 1f, 24f);
            if (GUILayout.Button("−", GUILayout.Width(28f))) nextZoom = Mathf.Max(1f, _mapZoom - 0.75f);
            if (GUILayout.Button("+", GUILayout.Width(28f))) nextZoom = Mathf.Min(24f, _mapZoom + 0.75f);
            if (GUILayout.Button("К персонажу", GUILayout.Width(112f))) _mapPan = Vector2.zero;
            if (GUILayout.Button("Сброс", GUILayout.Width(54f))) { nextZoom = 1f; _mapPan = Vector2.zero; }
            if (GUILayout.Button(_mapExpanded ? "Свернуть" : "На весь экран", GUILayout.Width(112f)))
                _mapExpanded = !_mapExpanded;
            _mapZoom = Mathf.Clamp(nextZoom, 1f, 24f);
            GUILayout.EndHorizontal();
            GUILayout.Label("Колесо — приблизить/отдалить · ЛКМ — двигать карту · Север ↑");
            if (manifest?.Settlements == null || place == null || anchor == null)
            {
                GUILayout.Label("Карта мира ожидает серверный манифест и глобальное положение персонажа.");
                return;
            }

            EnsureMapStyles();
            var mapSize = WorldMapViewportPolicy.GetMapSize(Screen.width, Screen.height, _mapExpanded);
            var rect = GUILayoutUtility.GetRect(mapSize, mapSize, GUILayout.Width(mapSize), GUILayout.Height(mapSize));
            var player = _frame?.Me?.WorldPosition;
            var hasPlayer = player != null && player.IsValid && player.Frame == anchor.Frame;
            var playerEast = hasPlayer ? player.East : anchor.East;
            var playerNorth = hasPlayer ? player.North : anchor.North;
            var mapHalf = UnifiedMapHalf(worldRadius, _mapZoom);
            HandleUnifiedMapPointer(rect, mapHalf);
            var centerEast = playerEast + _mapPan.x;
            var centerNorth = playerNorth + _mapPan.y;
            DrawMapFill(rect, new Color(0.18f, 0.24f, 0.19f, 1f));
            for (var i = 1; i < 8; i++)
            {
                var x = rect.x + rect.width * i / 8f;
                var y = rect.y + rect.height * i / 8f;
                DrawMapFill(new Rect(x, rect.y, 1f, rect.height), new Color(0.78f, 0.79f, 0.67f, 0.10f));
                DrawMapFill(new Rect(rect.x, y, rect.width, 1f), new Color(0.78f, 0.79f, 0.67f, 0.10f));
            }

            Vector2 ProjectGlobal(double east, double north)
            {
                return WorldMapProjection.ToMapPoint(east - centerEast, north - centerNorth, mapHalf, rect);
            }

            // World roads are drawn only from the authoritative server road graph.
            var settlementsById = new Dictionary<string, WorldSettlementFrame>(StringComparer.Ordinal);
            foreach (var settlement in manifest.Settlements)
                if (settlement != null && settlement.Frame == anchor.Frame && !string.IsNullOrWhiteSpace(settlement.Id))
                    settlementsById[settlement.Id] = settlement;
            // At settlement/region zoom, prefer the actual server-authored chunk polylines.
            // Manifest edges are only a schematic connection between settlements; they are
            // deliberately retained at world zoom where local terrain chunks are not loaded.
            var detailedPathIds = new HashSet<string>(StringComparer.Ordinal);
            var drawDetailedPaths = mapHalf <= 1800d;
            if (drawDetailedPaths)
            {
                // Waterways are server-authored terrain geometry. Never synthesize missing lines on the client.
                // Draw them before roads
                // so bridges and crossings remain legible above the water.
                foreach (var chunk in _terrainChunks.Values)
                {
                    if (chunk == null || chunk.Frame != anchor.Frame || chunk.Origin == null || chunk.Water == null)
                        continue;
                    foreach (var water in chunk.Water)
                    {
                        if (water == null || water.Points == null || water.Points.Count < 2 ||
                            double.IsNaN(water.WidthMeters) || double.IsInfinity(water.WidthMeters) ||
                            water.WidthMeters <= 0d) continue;
                        var kind = (water.Kind ?? string.Empty).ToLowerInvariant();
                        var color = kind == "waterfall"
                            ? new Color(0.58f, 0.78f, 0.84f, 0.96f)
                            : kind == "coast" || kind == "lake" || kind == "pond"
                                ? new Color(0.30f, 0.58f, 0.68f, 0.88f)
                                : new Color(0.34f, 0.67f, 0.78f, 0.92f);
                        var metersToPixels = rect.width / Math.Max(1d, mapHalf * 2d);
                        var width = Mathf.Clamp((float)(water.WidthMeters * metersToPixels), 1.2f, 5.5f);
                        for (var i = 1; i < water.Points.Count; i++)
                        {
                            var a = water.Points[i - 1];
                            var b = water.Points[i];
                            if (a == null || b == null ||
                                double.IsNaN(a.X) || double.IsInfinity(a.X) ||
                                double.IsNaN(a.Z) || double.IsInfinity(a.Z) ||
                                double.IsNaN(b.X) || double.IsInfinity(b.X) ||
                                double.IsNaN(b.Z) || double.IsInfinity(b.Z)) continue;
                            var dx = b.X - a.X;
                            var dz = b.Z - a.Z;
                            if (dx * dx + dz * dz < 0.000001d) continue;
                            DrawMapLineClipped(
                                ProjectGlobal(chunk.Origin.East + a.X, chunk.Origin.North + a.Z),
                                ProjectGlobal(chunk.Origin.East + b.X, chunk.Origin.North + b.Z),
                                width, color, rect);
                        }
                    }
                }
            }
            if (drawDetailedPaths)
            {
                foreach (var chunk in _terrainChunks.Values)
                {
                    if (chunk == null || chunk.Frame != anchor.Frame || chunk.Origin == null || chunk.Roads == null)
                        continue;
                    foreach (var path in chunk.Roads)
                    {
                        if (path == null || path.Points == null || path.Points.Count < 2) continue;
                        var hasUsableSegment = false;
                        var isTrail = string.Equals(path.Kind, "trail", StringComparison.OrdinalIgnoreCase);
                        var isBridge = string.Equals(path.Kind, "bridge", StringComparison.OrdinalIgnoreCase);
                        var color = isBridge
                            ? new Color(0.68f, 0.61f, 0.43f, 0.98f)
                            : isTrail
                                ? new Color(0.66f, 0.52f, 0.34f, 0.96f)
                                : new Color(0.84f, 0.68f, 0.43f, 0.98f);
                        var metersToPixels = rect.width / Math.Max(1d, mapHalf * 2d);
                        var width = Mathf.Clamp((float)(Math.Max(1d, path.WidthMeters) * metersToPixels),
                            isTrail ? 1.1f : 1.5f, isTrail ? 2.6f : 4.2f);
                        for (var i = 1; i < path.Points.Count; i++)
                        {
                            var a = path.Points[i - 1];
                            var b = path.Points[i];
                            if (a == null || b == null ||
                                double.IsNaN(a.X) || double.IsInfinity(a.X) ||
                                double.IsNaN(a.Z) || double.IsInfinity(a.Z) ||
                                double.IsNaN(b.X) || double.IsInfinity(b.X) ||
                                double.IsNaN(b.Z) || double.IsInfinity(b.Z)) continue;
                            var deltaEast = b.X - a.X;
                            var deltaNorth = b.Z - a.Z;
                            if (deltaEast * deltaEast + deltaNorth * deltaNorth < 0.000001d) continue;
                            hasUsableSegment = true;
                            DrawMapLineClipped(
                                ProjectGlobal(chunk.Origin.East + a.X, chunk.Origin.North + a.Z),
                                ProjectGlobal(chunk.Origin.East + b.X, chunk.Origin.North + b.Z),
                                width, color, rect);
                        }
                        // A malformed/degenerate chunk path must not hide the manifest fallback.
                        if (hasUsableSegment && !string.IsNullOrWhiteSpace(path.Id))
                            detailedPathIds.Add(path.Id);
                    }
                }
            }

            if (manifest.Roads != null)
            {
                foreach (var road in manifest.Roads)
                {
                    if (road == null || !settlementsById.TryGetValue(road.From, out var from) ||
                        !settlementsById.TryGetValue(road.To, out var to)) continue;
                    // Never draw a misleading straight chord over a loaded detailed route.
                    if (drawDetailedPaths && !string.IsNullOrWhiteSpace(road.Id) &&
                        detailedPathIds.Contains(road.Id)) continue;
                    var isTract = string.Equals(road.Kind, "road", StringComparison.OrdinalIgnoreCase);
                    var pathWidth = road.WidthMeters > 0d
                        ? Mathf.Clamp((float)(1.0d + road.WidthMeters * 0.32d), 1.2f, 3.2f)
                        : (isTract ? 2.2f : 1.5f);
                    var mapLineWidth = mapHalf < 12000d ? pathWidth : Math.Max(1.1f, pathWidth * 0.65f);
                    var mapLineColor = isTract
                        ? new Color(0.78f, 0.61f, 0.35f, 0.92f)
                        : new Color(0.55f, 0.48f, 0.34f, 0.82f);
                    DrawMapLineClipped(ProjectGlobal(from.East, from.North), ProjectGlobal(to.East, to.North),
                        mapLineWidth, mapLineColor, rect);
                }
            }

            var knownNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var knownPlaces = _frame.Me?.KnownPlaces;
            if (knownPlaces != null)
                foreach (var known in knownPlaces)
                    if (known != null && !string.IsNullOrWhiteSpace(known.Id))
                        knownNames[known.Id] = string.IsNullOrWhiteSpace(known.Name) ? "Неизвестное место" : known.Name;

            foreach (var settlement in settlementsById.Values)
            {
                var current = string.Equals(settlement.Id, place.Id, StringComparison.Ordinal);
                var known = current || knownNames.TryGetValue(settlement.Id, out _);
                if (!known) continue;
                var point = ProjectGlobal(settlement.East, settlement.North);
                if (!rect.Contains(point)) continue;
                var size = current ? 16f : settlement.Tier == "city" ? 13f : 10f;
                var oldBackground = GUI.backgroundColor;
                var oldContent = GUI.contentColor;
                GUI.backgroundColor = current ? new Color(0.75f, 0.25f, 0.16f, 1f) :
                    settlement.Tier == "city" ? new Color(0.82f, 0.68f, 0.38f, 1f) :
                    new Color(0.68f, 0.68f, 0.49f, 1f);
                GUI.contentColor = Color.white;
                GUI.Box(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size),
                    current ? "★" : settlement.Tier == "city" ? "◆" : "●", _mapNeighborStyle);
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
                var label = current ? place.Name : knownNames[settlement.Id];
                GUI.Label(new Rect(point.x + size * 0.5f + 3f, point.y - 9f, 150f, 18f), label, _mapPoiLabelStyle);
            }

            // At regional/settlement scales, reveal the current settlement's actual server layout.
            // Local offsets are projected from the same authoritative settlement-centre anchor.
            if (_layout != null && _layout.Version == 2 && mapHalf <= 1800d)
            {
                if (_layout.RiverPresent)
                {
                    var a = ProjectGlobal(anchor.East - _layout.WorldHalf, anchor.North + _layout.River.Y0);
                    var b = ProjectGlobal(anchor.East + _layout.WorldHalf, anchor.North + _layout.River.Y1);
                    DrawMapFill(ClipMapRect(Rect.MinMaxRect(Mathf.Min(a.x,b.x), Mathf.Min(a.y,b.y),
                        Mathf.Max(a.x,b.x), Mathf.Max(a.y,b.y)), rect), new Color(0.20f,0.43f,0.55f,0.85f));
                }
                if (_layout.Buildings != null)
                    foreach (var building in _layout.Buildings)
                    {
                        if (building == null) continue;
                        var a = ProjectGlobal(anchor.East + building.X0, anchor.North + building.Y1);
                        var b = ProjectGlobal(anchor.East + building.X1, anchor.North + building.Y0);
                        DrawMapFill(ClipMapRect(Rect.MinMaxRect(Mathf.Min(a.x,b.x), Mathf.Min(a.y,b.y),
                            Mathf.Max(a.x,b.x), Mathf.Max(a.y,b.y)), rect), new Color(0.36f,0.27f,0.20f,0.98f));
                    }
                if (_layout.Pois != null)
                    foreach (var poiId in MapPoiOrder)
                    {
                        if (!_layout.Pois.TryGetValue(poiId, out var poi) || poi == null || !ShouldShowMapPoi(poiId)) continue;
                        var point = ProjectGlobal(anchor.East + poi.X, anchor.North + poi.Y);
                        if (!rect.Contains(point)) continue;
                        GUI.Box(new Rect(point.x-7f,point.y-7f,14f,14f),MapPoiGlyph(poiId),_mapPoiStyle);
                        GUI.Label(new Rect(point.x+8f,point.y-8f,92f,16f),MapPoiDisplayName(poiId),_mapPoiLabelStyle);
                    }
            }

            if (hasPlayer)
            {
                var point = ProjectGlobal(playerEast, playerNorth);
                GUI.Box(new Rect(point.x-9f,point.y-9f,18f,18f),"★",_mapPlayerStyle);
            }
            var oldColor = GUI.color;
            GUI.color = Color.white;
            DrawMapFill(new Rect(rect.x,rect.y,rect.width,2f),new Color(0.08f,0.11f,0.08f,1f));
            DrawMapFill(new Rect(rect.x,rect.yMax-2f,rect.width,2f),new Color(0.08f,0.11f,0.08f,1f));
            DrawMapFill(new Rect(rect.x,rect.y,2f,rect.height),new Color(0.08f,0.11f,0.08f,1f));
            DrawMapFill(new Rect(rect.xMax-2f,rect.y,2f,rect.height),new Color(0.08f,0.11f,0.08f,1f));
            GUI.color = oldColor;
            GUILayout.Label("Текущее место: " + place.Name + " · " + (place.Realm?.Name ?? "вольные земли"));
            if (hasPlayer)
            {
                var distanceToCenter = Math.Sqrt(Math.Pow(playerEast-anchor.East,2d)+Math.Pow(playerNorth-anchor.North,2d));
                GUILayout.Label("Глобальные координаты: восток " + playerEast.ToString("0",CultureInfo.InvariantCulture) +
                    " м · север " + playerNorth.ToString("0",CultureInfo.InvariantCulture) +
                    " м · от центра поселения " + distanceToCenter.ToString("0",CultureInfo.InvariantCulture) + " м");
                var mapCamera = Camera.main;
                if (mapCamera != null)
                    GUILayout.Label("Направление взгляда: " + CompassDirection(mapCamera.transform.eulerAngles.y) +
                        " · азимут " + Mathf.Repeat(mapCamera.transform.eulerAngles.y, 360f).ToString("0", CultureInfo.InvariantCulture) + "°");
            }
            GUILayout.Label(mapHalf > 1800d
                ? "Масштаб мира: города, известные поселения и серверные дороги."
                : mapHalf > 300d
                    ? "Региональный масштаб: дороги и окрестности текущего поселения."
                    : "Масштаб поселения: здания и места из серверной планировки.");
            GUILayout.Label(drawDetailedPaths
                ? "★ Вы · ◆ город · ● поселение · дороги, тропы и водотоки показаны по серверным линиям загруженных чанков."
                : "★ Вы · ◆ город · ● поселение · ━ схематическая связь поселений (тип и ширина пути от сервера); точная трасса раскрывается при приближении.");
        }

        private static string CompassDirection(float yaw)
        {
            var directions = new[] { "север", "северо-восток", "восток", "юго-восток",
                "юг", "юго-запад", "запад", "северо-запад" };
            var index = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 45f) % directions.Length;
            return directions[index];
        }

        private static double ResolveUnifiedMapRadius(WorldManifestFrame manifest, WorldAnchorFrame anchor)
        {
            var radius = 500d;
            if (manifest?.Settlements == null || anchor == null) return radius;
            foreach (var settlement in manifest.Settlements)
            {
                if (settlement == null || settlement.Frame != anchor.Frame) continue;
                var dx = settlement.East - anchor.East;
                var dy = settlement.North - anchor.North;
                radius = Math.Max(radius, Math.Sqrt(dx * dx + dy * dy));
            }
            return Math.Max(1000d, radius * 1.08d);
        }

        private static double UnifiedMapHalf(double worldRadius, float zoom)
        {
            var t = Mathf.InverseLerp(1f, 24f, zoom);
            t = t * t * (3f - 2f * t);
            return Math.Max(150d, worldRadius * (1d - t) + 150d * t);
        }

        private void HandleUnifiedMapPointer(Rect mapRect, double mapHalf)
        {
            var currentEvent = Event.current;
            if (currentEvent == null) return;
            var controlId = GUIUtility.GetControlID(FocusType.Passive, mapRect);
            if (currentEvent.type == EventType.ScrollWheel && mapRect.Contains(currentEvent.mousePosition))
            {
                _mapZoom = Mathf.Clamp(_mapZoom + (currentEvent.delta.y < 0f ? 0.75f : -0.75f), 1f, 24f);
                currentEvent.Use();
                return;
            }
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && mapRect.Contains(currentEvent.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _mapDragging = true;
                _mapLastPointer = currentEvent.mousePosition;
                currentEvent.Use();
                return;
            }
            if (GUIUtility.hotControl != controlId || !_mapDragging) return;
            if (currentEvent.type == EventType.MouseDrag)
            {
                var hasPlayer = _frame?.Me?.WorldPosition != null;
                var delta = currentEvent.mousePosition - _mapLastPointer;
                var metersPerPixel = mapHalf * 2d / Math.Max(1f, mapRect.width);
                _mapPan.x -= (float)(delta.x * metersPerPixel);
                _mapPan.y += (float)(delta.y * metersPerPixel);
                _mapLastPointer = currentEvent.mousePosition;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp)
            {
                _mapDragging = false;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
            }
        }

        private static Rect ClipMapRect(Rect source, Rect viewport)
        {
            var left = Mathf.Max(source.xMin, viewport.xMin);
            var top = Mathf.Max(source.yMin, viewport.yMin);
            var right = Mathf.Min(source.xMax, viewport.xMax);
            var bottom = Mathf.Min(source.yMax, viewport.yMax);
            return right <= left || bottom <= top ? new Rect(0f,0f,0f,0f) : Rect.MinMaxRect(left,top,right,bottom);
        }



        // A map drag owns IMGUI hotControl only while the map is actually available.
        // OnGUI can return early for the system menu/loading screen, or the player can
        // switch tabs/close the journal while dragging. In those cases DrawLocalMap no
        // longer receives MouseUp, so release capture here before any early return.
        private void ReleaseLocalMapCaptureIfInactive()
        {
            if (!_mapDragging) return;
            var currentEvent = Event.current;
            var mouseReleased = currentEvent != null && currentEvent.type == EventType.MouseUp;
            var mapUnavailable = _hudCollapsed || _systemMenuOpen || _chatOpen ||
                _connectionState != WorldConnectionState.Active || _frame == null || _hudTab != HudTab.Map;
            if (!mouseReleased && !mapUnavailable) return;

            _mapDragging = false;
            GUIUtility.hotControl = 0;
            if (mouseReleased) currentEvent.Use();
        }

        private WorldAnchorFrame ResolveWorldAnchor(WorldFrame frame)
        {
            if (frame?.Place?.Anchor != null)
            {
                _floatingOriginAnchor = null;
                return frame.Place.Anchor;
            }

            var worldPosition = frame?.Me?.WorldPosition;
            if (worldPosition == null || !worldPosition.IsValid) return null;

            const double rebaseDistanceMeters = 512d;
            var dx = worldPosition.East - (_floatingOriginAnchor != null ? _floatingOriginAnchor.East : worldPosition.East);
            var dn = worldPosition.North - (_floatingOriginAnchor != null ? _floatingOriginAnchor.North : worldPosition.North);
            if (_floatingOriginAnchor == null || _floatingOriginAnchor.Frame != worldPosition.Frame ||
                Math.Sqrt(dx * dx + dn * dn) > rebaseDistanceMeters)
            {
                _floatingOriginAnchor = new WorldAnchorFrame
                {
                    Frame = worldPosition.Frame,
                    East = worldPosition.East,
                    North = worldPosition.North,
                    Up = worldPosition.Ground
                };
            }
            return _floatingOriginAnchor;
        }

        private bool TryGetPlayerMapLocalPosition(out Vector2 local)
        {
            local = Vector2.zero;
            if (WorldPositionProjection.TryToUnityPosition(
                _frame?.Me?.WorldPosition, ResolveWorldAnchor(_frame), out var projected))
            {
                local = new Vector2(projected.x, projected.z);
                return true;
            }
            var position = _frame?.Me?.Position;
            if (position == null) return false;
            local = new Vector2((float)position.X, (float)position.Y);
            return true;
        }

        private void HandleLocalMapPointer(Rect mapRect, double worldHalf)
        {
            var currentEvent = Event.current;
            if (currentEvent == null) return;

            var controlId = GUIUtility.GetControlID(FocusType.Passive, mapRect);
            if (currentEvent.type == EventType.ScrollWheel && mapRect.Contains(currentEvent.mousePosition))
            {
                var previousZoom = _mapZoom;
                _mapZoom = Mathf.Clamp(
                    _mapZoom + (currentEvent.delta.y < 0f ? 0.25f : -0.25f), 1f, 4f);
                if (_mapZoom <= 1.01f) _mapPan = Vector2.zero;
                else if (previousZoom <= 1.01f) _mapPan = Vector2.zero;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 &&
                mapRect.Contains(currentEvent.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _mapDragging = true;
                _mapLastPointer = currentEvent.mousePosition;
                currentEvent.Use();
                return;
            }

            if (GUIUtility.hotControl != controlId || !_mapDragging) return;
            if (currentEvent.type == EventType.MouseDrag)
            {
                if (_mapZoom > 1.01f)
                {
                    var delta = currentEvent.mousePosition - _mapLastPointer;
                    var half = (float)(worldHalf / _mapZoom);
                    _mapPan.x -= delta.x * (2f * half / Mathf.Max(1f, mapRect.width));
                    _mapPan.y += delta.y * (2f * half / Mathf.Max(1f, mapRect.height));

                    // Keep the viewport within the server-authored settlement bounds.
                    var hasPlayerPosition = TryGetPlayerMapLocalPosition(out var playerLocal);
                    var centerX = hasPlayerPosition ? playerLocal.x + _mapPan.x : _mapPan.x;
                    var centerY = hasPlayerPosition ? playerLocal.y + _mapPan.y : _mapPan.y;
                    var maxCenter = Math.Max(0d, worldHalf - half);
                    centerX = Mathf.Clamp(centerX, (float)-maxCenter, (float)maxCenter);
                    centerY = Mathf.Clamp(centerY, (float)-maxCenter, (float)maxCenter);
                    if (hasPlayerPosition)
                    {
                        _mapPan.x = (float)(centerX - playerLocal.x);
                        _mapPan.y = (float)(centerY - playerLocal.y);
                    }
                    else
                    {
                        _mapPan.x = (float)centerX;
                        _mapPan.y = (float)centerY;
                    }
                }

                _mapLastPointer = currentEvent.mousePosition;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp)
            {
                _mapDragging = false;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
            }
        }

        private void DrawLocalMap()
        {
            var mapSize = Mathf.Min(400f, Screen.width - 96f);
            var mapRect = GUILayoutUtility.GetRect(mapSize, mapSize,
                GUILayout.Width(mapSize), GUILayout.Height(mapSize));
            var worldHalf = _layout.WorldHalf > 0d ? _layout.WorldHalf : 150d;
            HandleLocalMapPointer(mapRect, worldHalf);
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

            var hasPlayerPosition = TryGetPlayerMapLocalPosition(out var playerMapLocal);
            var centerX = _mapZoom > 1.01f && hasPlayerPosition
                ? playerMapLocal.x + _mapPan.x : 0d;
            var centerY = _mapZoom > 1.01f && hasPlayerPosition
                ? playerMapLocal.y + _mapPan.y : 0d;
            var half = worldHalf / _mapZoom;
            if (_layout.RiverPresent)
            {
                var riverRect = MapViewportRect(-half - centerX, _layout.River.Y0 - centerY,
                    half - centerX, _layout.River.Y1 - centerY, half, mapRect);
                DrawMapFill(riverRect, new Color(0.20f, 0.43f, 0.52f, 0.95f));

                var bridgeRect = MapViewportRect(_layout.River.BridgeX0 - centerX,
                    _layout.River.Y0 - 1.5d - centerY, _layout.River.BridgeX1 - centerX,
                    _layout.River.Y1 + 1.5d - centerY, half, mapRect);
                DrawMapFill(bridgeRect, new Color(0.55f, 0.37f, 0.22f, 1f));
            }

            if (_layout.Buildings != null)
            {
                foreach (var building in _layout.Buildings)
                {
                    if (building == null) continue;
                    var footprint = MapViewportRect(building.X0 - centerX, building.Y0 - centerY,
                        building.X1 - centerX, building.Y1 - centerY, half, mapRect);
                    DrawMapFill(footprint, new Color(0.30f, 0.23f, 0.18f, 0.98f));
                }
            }

            EnsureMapStyles();
            if (_layout.Pois != null)
            {
                foreach (var poiId in MapPoiOrder)
                {
                    if (!_layout.Pois.TryGetValue(poiId, out var point) || point == null || !ShouldShowMapPoi(poiId)) continue;
                    var position = WorldMapProjection.ToMapPoint(point.X - centerX, point.Y - centerY, half, mapRect);
                    if (!mapRect.Contains(position)) continue;
                    var marker = new Rect(position.x - 9f, position.y - 9f, 18f, 18f);
                    var previousBackground = GUI.backgroundColor;
                    var previousContent = GUI.contentColor;
                    GUI.backgroundColor = new Color(0.70f, 0.53f, 0.31f, 1f);
                    GUI.contentColor = Color.white;
                    GUI.Box(marker, MapPoiGlyph(poiId), _mapPoiStyle);
                    GUI.backgroundColor = previousBackground;
                    GUI.contentColor = previousContent;
                    if (MapLabelLayoutPolicy.TryPlace(mapRect.x, mapRect.y, mapRect.width, mapRect.height,
                        position.x, position.y, 112f, 18f, 4f, out var poiLabel))
                    {
                        var labelRect = new Rect(poiLabel.X, poiLabel.Y, poiLabel.Width, poiLabel.Height);
                        GUI.Label(labelRect, MapPoiDisplayName(poiId),
                            poiLabel.AlignRight ? _mapPoiLabelRightStyle : _mapPoiLabelStyle);
                    }
                }
            }

            // v1.22 wilderness sites are published in global E/N coordinates. Convert
            // them through the current settlement's authoritative centre anchor before
            // projecting onto this local map; never treat global coordinates as local XY.
            var sites = _frame?.Place?.Sites;
            var settlementAnchor = ResolveWorldAnchor(_frame);
            if (sites != null && settlementAnchor != null)
            {
                foreach (var site in sites)
                {
                    if (site == null) continue;
                    if (!WildSiteMapProjectionPolicy.TryToSettlementLocal(
                        site.East, site.North, settlementAnchor.East, settlementAnchor.North,
                        out var localEast, out var localNorth)) continue;
                    var position = WorldMapProjection.ToMapPoint(
                        localEast - centerX, localNorth - centerY, half, mapRect);
                    if (!mapRect.Contains(position)) continue;

                    var marker = new Rect(position.x - 9f, position.y - 9f, 18f, 18f);
                    var previousBackground = GUI.backgroundColor;
                    var previousContent = GUI.contentColor;
                    GUI.backgroundColor = site.Here
                        ? new Color(0.36f, 0.66f, 0.42f, 1f)
                        : new Color(0.43f, 0.38f, 0.62f, 1f);
                    GUI.contentColor = Color.white;
                    GUI.Box(marker, new GUIContent(WildSiteMapGlyph(site.Kind), site.Note ?? string.Empty), _mapPoiStyle);
                    GUI.backgroundColor = previousBackground;
                    GUI.contentColor = previousContent;

                    var siteLabel = (site.Name ?? "Неизвестное место") + " · " +
                        site.DistanceMeters.ToString("0", CultureInfo.InvariantCulture) + " м";
                    if (site.Here) siteLabel += " · рядом";
                    if (!string.IsNullOrWhiteSpace(site.Note)) siteLabel += "\n" + site.Note;
                    var siteLabelHeight = string.IsNullOrWhiteSpace(site.Note) ? 28f : 42f;
                    if (MapLabelLayoutPolicy.TryPlace(mapRect.x, mapRect.y, mapRect.width, mapRect.height,
                        position.x, position.y, 156f, siteLabelHeight, 4f, out var siteLabelPlacement))
                    {
                        var labelRect = new Rect(siteLabelPlacement.X, siteLabelPlacement.Y,
                            siteLabelPlacement.Width, siteLabelPlacement.Height);
                        GUI.Label(labelRect, siteLabel,
                            siteLabelPlacement.AlignRight ? _mapNeighborLabelRightStyle : _mapNeighborLabelStyle);
                    }
                }
            }

            var neighbors = _frame?.Place?.Neighbors;
            if (neighbors != null)
            {
                foreach (var neighbor in neighbors)
                {
                    if (neighbor == null) continue;
                    Vector3 neighborLocal;
                    if (!WorldPositionProjection.TryToUnityPosition(
                        neighbor.WorldPosition, settlementAnchor, out neighborLocal))
                    {
                        if (neighbor.Position == null) continue;
                        neighborLocal = EntityViewRegistry.ToUnityPosition(neighbor.Position);
                    }
                    var position = WorldMapProjection.ToMapPoint(
                        neighborLocal.x - centerX, neighborLocal.z - centerY, half, mapRect);
                    if (!mapRect.Contains(position)) continue;
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
                    if (MapLabelLayoutPolicy.TryPlace(mapRect.x, mapRect.y, mapRect.width, mapRect.height,
                        position.x, position.y, 128f, 28f, 4f, out var residentLabelPlacement))
                    {
                        var labelRect = new Rect(residentLabelPlacement.X, residentLabelPlacement.Y,
                            residentLabelPlacement.Width, residentLabelPlacement.Height);
                        GUI.Label(labelRect, residentLabel,
                            residentLabelPlacement.AlignRight ? _mapNeighborLabelRightStyle : _mapNeighborLabelStyle);
                    }
                }
            }

            if (hasPlayerPosition)
            {
                var position = WorldMapProjection.ToMapPoint(
                    playerMapLocal.x - centerX, playerMapLocal.y - centerY, half, mapRect);
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
            if (hasPlayerPosition)
                GUILayout.Label($"Ваше положение: восток {playerMapLocal.x:0.0} м · север {playerMapLocal.y:0.0} м (от центра поселения)");
            else
                GUILayout.Label("Положение персонажа пока не передано сервером.");
            var realm = _frame?.Place?.Realm;
            if (realm != null)
            {
                var realmLine = "Земли державы: " + realm.Name + " · столица: " + realm.Capital +
                    " · поселений: " + realm.Size;
                if (!string.IsNullOrWhiteSpace(realm.WarWith)) realmLine += " · война с " + realm.WarWith;
                GUILayout.Label(realmLine);
            }
            else
            {
                GUILayout.Label("Вольная земля · поселение не входит в державу.");
            }
            var nearbyCaravans = _frame?.Place?.Caravans;
            if (nearbyCaravans != null && nearbyCaravans.Count > 0)
            {
                GUILayout.Label("Обозы поблизости");
                foreach (var caravan in nearbyCaravans)
                {
                    if (caravan == null) continue;
                    var cargo = !string.IsNullOrWhiteSpace(caravan.GoodName) ? caravan.GoodName : caravan.Good;
                    GUILayout.Label((cargo ?? "Груз") + " ×" + caravan.Amount.ToString("0", CultureInfo.InvariantCulture) +
                        " · " + caravan.From + " → " + caravan.To + " · " +
                        caravan.DistanceMeters.ToString("0", CultureInfo.InvariantCulture) + " м · " + caravan.Direction);
                }
            }
            GUILayout.Label("Места: К — корчма · Кз — кузница · Кн — конюшня и амбар · Р — рынок · Кл — колодец · С — очаг · О — огород · Рк — река · Л — лес · Ш — рудник");
        }

        private static string WildSiteMapGlyph(string kind)
        {
            switch (kind)
            {
                case "cave": return "П";
                case "tower": return "Б";
                case "ruins": return "Р";
                case "grove": return "Рщ";
                case "spring": return "И";
                default: return "?";
            }
        }

        private static Rect MapViewportRect(double x0, double y0, double x1, double y1,
            double half, Rect viewport)
        {
            var projected = WorldMapProjection.ToMapRect(x0, y0, x1, y1, half, viewport);
            var left = Mathf.Max(viewport.xMin, projected.xMin);
            var top = Mathf.Max(viewport.yMin, projected.yMin);
            var right = Mathf.Min(viewport.xMax, projected.xMax);
            var bottom = Mathf.Min(viewport.yMax, projected.yMax);
            return right <= left || bottom <= top ? new Rect(0f, 0f, 0f, 0f) :
                Rect.MinMaxRect(left, top, right, bottom);
        }

        private static void DrawMapLineClipped(Vector2 start, Vector2 end, float thickness,
            Color color, Rect viewport)
        {
            if (viewport.width <= 0f || viewport.height <= 0f) return;
            // Keep the full stroke inside the map, not just its centerline.
            var inset = Mathf.Min(Mathf.Max(0.5f, thickness * 0.5f),
                Mathf.Min(viewport.width, viewport.height) * 0.5f);
            var x0 = (double)start.x;
            var y0 = (double)start.y;
            var x1 = (double)end.x;
            var y1 = (double)end.y;
            if (!WorldMapLineClipper.TryClip(ref x0, ref y0, ref x1, ref y1,
                viewport.xMin + inset, viewport.yMin + inset,
                viewport.xMax - inset, viewport.yMax - inset)) return;
            DrawMapLine(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1),
                thickness, color);
        }

        private static void DrawMapLine(Vector2 start, Vector2 end, float thickness, Color color)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length < 1f) return;
            var center = (start + end) * 0.5f;
            var previousMatrix = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, center);
                DrawMapFill(new Rect(center.x - length * 0.5f, center.y - thickness * 0.5f, length, thickness), color);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
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
            _mapPoiLabelRightStyle = new GUIStyle(_mapPoiLabelStyle)
            {
                alignment = TextAnchor.MiddleRight
            };
            _mapNeighborLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = true
            };
            _mapNeighborLabelStyle.normal.textColor = new Color(1f, 0.96f, 0.82f, 1f);
            _mapNeighborLabelRightStyle = new GUIStyle(_mapNeighborLabelStyle)
            {
                alignment = TextAnchor.MiddleRight
            };
            _mapNeighborStyle = new GUIStyle(_mapPoiStyle) { fontSize = 8 };
            _mapNeighborStyle.normal.textColor = Color.white;
            _mapPlayerStyle = new GUIStyle(_mapPoiStyle) { fontSize = 11 };
            _mapPlayerStyle.normal.textColor = Color.white;
        }

        private bool ShouldShowMapPoi(string poiId)
        {
            if (_core == null) return true;
            switch (poiId)
            {
                case "river": return _layout != null && _layout.RiverPresent && _core.SupportsFeature("river");
                case "mine": return _core.SupportsFeature("mine");
                case "garden": return true; // a meadow/pasture still exists even when no cultivated field is available
                case "tavern": return _frame?.Place?.Tavern == true;
                default: return true;
            }
        }

        private string MapPoiDisplayName(string poiId)
        {
            if (_core != null && poiId == "woods" && !_core.SupportsFeature("woods"))
                return "Рощица";
            if (_core != null && poiId == "garden" && !_core.SupportsFeature("garden"))
                return "Луг / выгон";
            return MapPoiName(poiId);
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

        private void DrawWorldConnectionsMap()
        {
            GUILayout.Label("МИРОВЫЕ ДОРОГИ И СОСЕДНИЕ ПОСЕЛЕНИЯ");
            var place = _frame?.Place;
            var anchor = place?.Anchor;
            var manifest = _worldManifest;
            if (manifest?.Settlements == null || place == null || anchor == null)
            {
                GUILayout.Label("Глобальная карта дорог пока недоступна от сервера.");
                return;
            }

            EnsureWorldMapNearby(place.Id, anchor.Frame, anchor.East, anchor.North);
            var currentRealm = place.Realm;
            GUILayout.Label(currentRealm == null
                ? "Вольная земля · державной власти здесь нет."
                : "Держава: " + currentRealm.Name + " · столица: " + currentRealm.Capital +
                    " · поселений: " + currentRealm.Size +
                    (string.IsNullOrWhiteSpace(currentRealm.WarWith) ? string.Empty : " · война с " + currentRealm.WarWith));
            if (_worldMapNearby.Count == 0)
            {
                GUILayout.Label("В этом географическом кадре пока нет известных дорог и поселений.");
                return;
            }

            const float mapSize = 320f;
            var rect = GUILayoutUtility.GetRect(mapSize, mapSize,
                GUILayout.Width(mapSize), GUILayout.Height(mapSize));
            DrawMapFill(rect, new Color(0.19f, 0.25f, 0.19f, 1f));
            for (var i = 1; i < 8; i++)
            {
                var x = rect.x + rect.width * i / 8f;
                var y = rect.y + rect.height * i / 8f;
                DrawMapFill(new Rect(x, rect.y, 1f, rect.height), new Color(0.75f, 0.78f, 0.64f, 0.12f));
                DrawMapFill(new Rect(rect.x, y, rect.width, 1f), new Color(0.75f, 0.78f, 0.64f, 0.12f));
            }

            EnsureMapStyles();
            // Draw the authoritative road graph only when both endpoints are in the selected nearby set.
            if (manifest.Roads != null)
            {
                for (var i = 0; i < manifest.Roads.Count; i++)
                {
                    var road = manifest.Roads[i];
                    if (road == null ||
                        !_worldMapNearbyById.TryGetValue(road.From, out var from) ||
                        !_worldMapNearbyById.TryGetValue(road.To, out var to))
                        continue;
                    var a = WorldMapPoint(from, anchor, rect);
                    var b = WorldMapPoint(to, anchor, rect);
                    DrawMapLine(a, b, 2.5f, new Color(0.72f, 0.58f, 0.36f, 0.88f));
                }
            }

            var names = new Dictionary<string, string>();
            var known = _frame.Me?.KnownPlaces;
            if (known != null)
                foreach (var item in known)
                    if (item != null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                        names[item.Id] = item.Name;
            for (var i = 0; i < _worldMapNearby.Count; i++)
            {
                var settlement = _worldMapNearby[i];
                if (settlement == null) continue;
                var current = string.Equals(settlement.Id, place.Id, StringComparison.Ordinal);
                var hasName = current || names.TryGetValue(settlement.Id, out _);
                var label = current ? place.Name :
                    (names.TryGetValue(settlement.Id, out var knownName) ? knownName : "?");
                var point = WorldMapPoint(settlement, anchor, rect);
                var size = current ? 16f : 10f;
                var previousBackground = GUI.backgroundColor;
                var previousContent = GUI.contentColor;
                GUI.backgroundColor = current
                    ? new Color(0.68f, 0.22f, 0.16f, 1f)
                    : hasName ? new Color(0.72f, 0.67f, 0.43f, 1f) : new Color(0.42f, 0.48f, 0.40f, 1f);
                GUI.contentColor = Color.white;
                GUI.Box(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size),
                    current ? "★" : hasName ? "●" : "?", _mapNeighborStyle);
                GUI.backgroundColor = previousBackground;
                GUI.contentColor = previousContent;
                if (current || hasName)
                    GUI.Label(new Rect(point.x + size * 0.5f + 3f, point.y - 8f, 116f, 16f),
                        label, _mapPoiLabelStyle);
            }

            // Global caravan positions are authoritative world E/N/U metres. Draw only
            // those inside this map viewport; cargo/progress remain server-authored.
            if (manifest.Caravans != null)
            {
                for (var i = 0; i < manifest.Caravans.Count; i++)
                {
                    var caravan = manifest.Caravans[i];
                    if (caravan == null || caravan.Frame != anchor.Frame) continue;
                    // /api/world is streaming metadata, not character knowledge. Only reveal
                    // global caravan positions that the server also exposed in this place frame.
                    var visibleToPlayer = false;
                    var nearby = place.Caravans;
                    if (nearby != null)
                    {
                        for (var k = 0; k < nearby.Count; k++)
                        {
                            if (nearby[k] != null && string.Equals(nearby[k].Id, caravan.Id, StringComparison.Ordinal))
                            {
                                visibleToPlayer = true;
                                break;
                            }
                        }
                    }
                    if (!visibleToPlayer) continue;
                    var point = new Vector2(
                        rect.center.x + (float)((caravan.East - anchor.East) / _worldMapRadius) * rect.width * 0.5f,
                        rect.center.y - (float)((caravan.North - anchor.North) / _worldMapRadius) * rect.height * 0.5f);
                    if (!rect.Contains(point)) continue;
                    var marker = new Rect(point.x - 6f, point.y - 6f, 12f, 12f);
                    var previousBackground = GUI.backgroundColor;
                    var previousContent = GUI.contentColor;
                    GUI.backgroundColor = new Color(0.72f, 0.57f, 0.30f, 1f);
                    GUI.contentColor = Color.white;
                    var progressLabel = Mathf.RoundToInt((float)caravan.Progress * 100f);
                    GUI.Box(marker, new GUIContent("◆",
                        "Обоз: " + caravan.Good + " · " + progressLabel + "% пути · " +
                        caravan.From + " → " + caravan.To), _mapNeighborStyle);
                    GUI.backgroundColor = previousBackground;
                    GUI.contentColor = previousContent;
                }
            }

            var player = _frame.Me?.WorldPosition;
            if (player != null)
            {
                var playerPoint = new Vector2(
                    rect.center.x + (float)((player.East - anchor.East) / _worldMapRadius) * rect.width * 0.5f,
                    rect.center.y - (float)((player.North - anchor.North) / _worldMapRadius) * rect.height * 0.5f);
                DrawMapFill(new Rect(playerPoint.x - 2f, playerPoint.y - 2f, 4f, 4f), Color.white);
            }
            GUILayout.Label("━ Дорога · ★ Вы · ● известное поселение · ? неизвестное место · ◆ обоз");
            GUILayout.Label("Карта показывает соседние поселения и реальные связи из мирового реестра; неизвестные места остаются без имени.");
            if (!manifest.Seamless)
                GUILayout.Label("Сервер пока не включил бесшовное перемещение: дороги показаны по мировому реестру, но переходы могут быть ограничены.");
        }

        private void EnsureWorldMapNearby(string currentSettlementId, int frameId, double centerEast, double centerNorth)
        {
            if (ReferenceEquals(_worldMapCachedManifest, _worldManifest) &&
                _worldMapCachedSettlementId == currentSettlementId && _worldMapCachedFrame == frameId)
                return;

            _worldMapCachedManifest = _worldManifest;
            _worldMapCachedSettlementId = currentSettlementId;
            _worldMapCachedFrame = frameId;
            _worldMapNearby.Clear();
            _worldMapNearbyDistances.Clear();
            _worldMapNearbyById.Clear();
            _worldMapRadius = 500d;
            if (_worldManifest?.Settlements == null) return;

            // Keep direct road neighbors even if farther away, then fill remaining slots
            // with the nearest settlements. Work is O(settlements × small fixed window) once per place.
            var directRoadIds = new HashSet<string>();
            if (_worldManifest.Roads != null)
            {
                foreach (var road in _worldManifest.Roads)
                {
                    if (road == null) continue;
                    if (road.From == currentSettlementId && !string.IsNullOrWhiteSpace(road.To))
                        directRoadIds.Add(road.To);
                    else if (road.To == currentSettlementId && !string.IsNullOrWhiteSpace(road.From))
                        directRoadIds.Add(road.From);
                }
            }

            var candidates = new List<WorldSettlementFrame>();
            var distances = new List<double>();
            foreach (var settlement in _worldManifest.Settlements)
            {
                if (settlement == null || settlement.Frame != frameId || string.IsNullOrWhiteSpace(settlement.Id))
                    continue;
                if (settlement.Id == currentSettlementId || directRoadIds.Contains(settlement.Id))
                {
                    _worldMapNearby.Add(settlement);
                    _worldMapNearbyDistances.Add(Math.Sqrt(
                        (settlement.East - centerEast) * (settlement.East - centerEast) +
                        (settlement.North - centerNorth) * (settlement.North - centerNorth)));
                    continue;
                }
                var dx = settlement.East - centerEast;
                var dy = settlement.North - centerNorth;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var insertAt = candidates.Count;
                for (var i = 0; i < distances.Count; i++)
                    if (distance < distances[i]) { insertAt = i; break; }
                if (insertAt >= 12) continue;
                candidates.Insert(insertAt, settlement);
                distances.Insert(insertAt, distance);
                if (candidates.Count > 12)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                    distances.RemoveAt(distances.Count - 1);
                }
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                _worldMapNearby.Add(candidates[i]);
                _worldMapNearbyDistances.Add(distances[i]);
            }
            for (var i = 0; i < _worldMapNearby.Count; i++)
            {
                var settlement = _worldMapNearby[i];
                if (settlement != null && !_worldMapNearbyById.ContainsKey(settlement.Id))
                    _worldMapNearbyById.Add(settlement.Id, settlement);
                if (_worldMapNearbyDistances[i] > _worldMapRadius)
                    _worldMapRadius = _worldMapNearbyDistances[i];
            }
            _worldMapRadius = Math.Max(300d, _worldMapRadius * 1.18d);
        }

        private Vector2 WorldMapPoint(WorldSettlementFrame settlement, WorldAnchorFrame anchor, Rect rect)
        {
            return new Vector2(
                rect.center.x + (float)((settlement.East - anchor.East) / _worldMapRadius) * rect.width * 0.5f,
                rect.center.y - (float)((settlement.North - anchor.North) / _worldMapRadius) * rect.height * 0.5f);
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
                ResetSettingsToDefaults();
        }

        private void ResetSettingsToDefaults()
        {
            foreach (var key in new[]
            {
                "Pogranichye.MasterVolume", "Pogranichye.SfxVolume", "Pogranichye.AmbienceVolume",
                "Pogranichye.MouseSensitivity", "Pogranichye.InvertMouseY", "Pogranichye.ShadowDistance",
                "Pogranichye.LodBias", "Pogranichye.AntiAliasing", "Pogranichye.VSync",
                "Pogranichye.TargetFrameRate", "Pogranichye.QualityLevel", "Pogranichye.Fullscreen",
                "Pogranichye.InputBindingOverrides"
            })
                PlayerPrefs.DeleteKey(key);

            _input?.ResetBindingOverrides();
            _rebindingAction = null;
            _rebindingBindingIndex = -1;
            _bindingStatus = "Стандартное управление восстановлено.";
            _masterVolume = 0.85f; _sfxVolume = 0.9f; _ambienceVolume = 0.75f;
            _mouseSensitivity = 0.035f; _invertMouseY = false; _shadowDistance = 120f;
            _lodBias = 1f; _antiAliasing = 2; _vSync = true; _targetFrameRate = 60;
            AudioListener.volume = _masterVolume;
            _audioDirector?.SetVolumes(_sfxVolume, _ambienceVolume);
            _villageSoundscape?.SetAmbienceVolume(_ambienceVolume);
            _cameraRig?.SetLookSettings(_mouseSensitivity, _invertMouseY);
            if (QualitySettings.names != null && QualitySettings.names.Length > 0)
                QualitySettings.SetQualityLevel(Mathf.Min(2, QualitySettings.names.Length - 1), true);
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
            ApplyGraphicsSettings();
            PlayerPrefs.Save();
            _status = "Настройки сброшены.";
        }

        private void DrawSettingsTabButton(string label, int tab)
        {
            var previous = GUI.color;
            if (_settingsTab == tab) GUI.color = new Color(0.88f, 0.72f, 0.48f);
            if (GUILayout.Button(label, GUILayout.Height(34f)))
            {
                if (_settingsTab != tab && _rebindingAction != null)
                {
                    _input?.CancelRebind();
                    _rebindingAction = null;
                    _rebindingBindingIndex = -1;
                    _bindingStatus = "Переназначение отменено при смене раздела.";
                }
                _settingsTab = tab;
            }
            GUI.color = previous;
        }

        private void DrawOptionsControls()
        {
            GUILayout.BeginHorizontal();
            DrawSettingsTabButton("ЗВУК", 0);
            DrawSettingsTabButton("ГРАФИКА", 1);
            DrawSettingsTabButton("УПРАВЛЕНИЕ", 2);
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            if (_settingsTab == 0)
            {
                GUILayout.Label("ЗВУК");
                DrawVolumeSlider("Общая громкость", ref _masterVolume, "Pogranichye.MasterVolume");
                DrawVolumeSlider("Эффекты: шаги, бой, магия", ref _sfxVolume, "Pogranichye.SfxVolume");
                DrawVolumeSlider("Окружение: ветер, птицы, вода, костёр", ref _ambienceVolume, "Pogranichye.AmbienceVolume");
                _audioDirector?.SetVolumes(_sfxVolume, _ambienceVolume);
                _villageSoundscape?.SetAmbienceVolume(_ambienceVolume);
            }
            else if (_settingsTab == 1)
            {
                GUILayout.Space(8f);
                GUILayout.Label("ГРАФИКА");
                var qualityNames = QualitySettings.names;
                if (qualityNames != null && qualityNames.Length > 0)
                {
                    GUILayout.Label("Общий пресет качества");
                    var quality = QualitySettings.GetQualityLevel();
                    var selected = GUILayout.SelectionGrid(quality, qualityNames, Mathf.Min(3, qualityNames.Length), GUILayout.MinHeight(32f));
                    if (selected != quality && selected >= 0 && selected < qualityNames.Length)
                    {
                        QualitySettings.SetQualityLevel(selected, true);
                        SaveInt("Pogranichye.QualityLevel", selected);
                    }
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
            }
            else
            {
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
                GUILayout.Label("WASD — движение · Alt — лёгкий бег · Shift — быстрый бег · Space — прыжок\nМышь — обзор · E — взаимодействие · F1 — интерфейс\nЛКМ — атака/выстрел · ПКМ — блок · Q — уворот · R — плетение\nZ/X/V/B/C — боевые приёмы · N — стойка · T — чат · Tab — дела и меню");

                GUILayout.Space(10f);
                GUILayout.Label("ПЕРЕНАЗНАЧЕНИЕ КЛАВИШ");
                GUILayout.Label("Нажмите кнопку рядом с действием, затем клавишу или кнопку мыши. Esc отменяет назначение.");
                DrawBindingSection("ПЕРЕДВИЖЕНИЕ", new[]
                {
                    new BindingRow("Вперёд", "Move", 1),
                    new BindingRow("Назад", "Move", 2),
                    new BindingRow("Влево", "Move", 3),
                    new BindingRow("Вправо", "Move", 4),
                    new BindingRow("Прыжок", "Jump"),
                    new BindingRow("Лёгкий бег", "Jog"),
                    new BindingRow("Бег", "Sprint"),
                    new BindingRow("Автоходьба", "ToggleAutoWalk")
                });
                DrawBindingSection("ВЗАИМОДЕЙСТВИЕ И ИНТЕРФЕЙС", new[]
                {
                    new BindingRow("Взаимодействие", "Interact"),
                    new BindingRow("Инвентарь", "OpenInventory"),
                    new BindingRow("Журнал / задания", "OpenJournal"),
                    new BindingRow("Карта", "OpenMap"),
                    new BindingRow("Системное меню", "Menu"),
                    new BindingRow("Скрыть / показать HUD", "ToggleHud"),
                    new BindingRow("Полный экран", "ToggleFullscreen"),
                    new BindingRow("Подсказки", "ToggleHints"),
                    new BindingRow("Техническая диагностика", "ToggleDiagnostics"),
                    new BindingRow("Чат", "Chat"),
                    new BindingRow("Голосовой PTT", "VoicePushToTalk"),
                    new BindingRow("Отмена / назад", "Cancel")
                });
                DrawBindingSection("ДИАЛОГИ И ВЫБОРЫ", new[]
                {
                    new BindingRow("Вариант диалога 1", "JourneyChoice1"),
                    new BindingRow("Вариант диалога 2", "JourneyChoice2"),
                    new BindingRow("Вариант диалога 3", "JourneyChoice3"),
                    new BindingRow("Вариант диалога 4", "JourneyChoice4"),
                    new BindingRow("Вариант диалога 5", "JourneyChoice5"),
                    new BindingRow("Вариант диалога 6", "JourneyChoice6"),
                    new BindingRow("Вариант диалога 7", "JourneyChoice7"),
                    new BindingRow("Вариант диалога 8", "JourneyChoice8"),
                    new BindingRow("Вариант диалога 9", "JourneyChoice9")
                });
                DrawBindingSection("МАГИЯ — ВЫБОР УЗЛОВ", new[]
                {
                    new BindingRow("Узел 1", "WeaveNode1"),
                    new BindingRow("Узел 2", "WeaveNode2"),
                    new BindingRow("Узел 3", "WeaveNode3"),
                    new BindingRow("Узел 4", "WeaveNode4"),
                    new BindingRow("Узел 5", "WeaveNode5"),
                    new BindingRow("Узел 6", "WeaveNode6"),
                    new BindingRow("Узел 7", "WeaveNode7"),
                    new BindingRow("Узел 8", "WeaveNode8"),
                    new BindingRow("Узел 9", "WeaveNode9"),
                    new BindingRow("Узел 10", "WeaveNode10")
                });
                DrawBindingSection("БОЙ И МАГИЯ", new[]
                {
                    new BindingRow("Основная атака", "Primary"),
                    new BindingRow("Вторичное действие / блок", "Secondary"),
                    new BindingRow("Быстрая атака", "QuickAttack"),
                    new BindingRow("Уворот", "Dodge"),
                    new BindingRow("Начать плетение", "WeaveHold"),
                    new BindingRow("Убрать узел плетения", "WeaveRemove"),
                    new BindingRow("Смена стойки", "CycleStance"),
                    new BindingRow("Песчаный приём", "TrickSand"),
                    new BindingRow("Толчок", "TrickShove"),
                    new BindingRow("Обманный выпад", "TrickFeint"),
                    new BindingRow("Приманка", "TrickBait"),
                    new BindingRow("Скрытный приём", "TrickSneak"),
                    new BindingRow("Захват цели", "LockTarget")
                });
                DrawBindingSection("РЕМЁСЛА И ЗАНЯТИЯ", new[]
                {
                    new BindingRow("Начать рубку", "StartWoodcut"),
                    new BindingRow("Начать рыбалку", "StartFishing")
                });
                GUILayout.Label(_bindingStatus);
                if (GUILayout.Button("Сбросить только управление", GUILayout.Height(30f)))
                {
                    _input?.ResetBindingOverrides();
                    _rebindingAction = null;
                    _rebindingBindingIndex = -1;
                    _bindingStatus = "Стандартная раскладка управления восстановлена.";
                }
            }
        }

        private struct BindingRow
        {
            public readonly string Label;
            public readonly string Action;
            public readonly int BindingIndex;

            public BindingRow(string label, string action, int bindingIndex = 0)
            {
                Label = label;
                Action = action;
                BindingIndex = bindingIndex;
            }
        }

        private void DrawBindingSection(string heading, BindingRow[] rows)
        {
            GUILayout.Space(5f);
            GUILayout.Label(heading);
            for (var i = 0; i < rows.Length; i++)
                DrawBindingRow(rows[i]);
        }

        private void DrawBindingRow(BindingRow row)
        {
            if (_input == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(row.Label, GUILayout.MinWidth(145f));
            var capturing = string.Equals(_rebindingAction, row.Action, StringComparison.Ordinal) &&
                _rebindingBindingIndex == row.BindingIndex;
            var current = capturing ? "Нажмите клавишу…" :
                _input.GetBindingDisplayString(row.Action, row.BindingIndex);
            if (GUILayout.Button(current, GUILayout.MinWidth(118f), GUILayout.Height(28f)))
            {
                var started = _input.StartRebind(row.Action, row.BindingIndex,
                    display =>
                    {
                        _rebindingAction = null;
                        _rebindingBindingIndex = -1;
                        var conflict = _input.GetBindingConflict(row.Action, row.BindingIndex);
                        _bindingStatus = row.Label + ": " + display +
                            (string.IsNullOrEmpty(conflict) ? " — сохранено." :
                                " — внимание: также назначено на «" + conflict + "». Проверьте конфликт.");
                    },
                    () =>
                    {
                        _rebindingAction = null;
                        _rebindingBindingIndex = -1;
                        _bindingStatus = "Переназначение отменено.";
                    });
                if (started)
                {
                    _rebindingAction = row.Action;
                    _rebindingBindingIndex = row.BindingIndex;
                    _bindingStatus = "Ожидание ввода: " + row.Label + ". Esc — отмена.";
                }
                else
                {
                    _rebindingAction = null;
                    _rebindingBindingIndex = -1;
                    _bindingStatus = "Не удалось начать переназначение этого действия.";
                }
            }
            GUILayout.EndHorizontal();
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

        private void EnsureStatusTextStyle()
        {
            if (_statusTextStyle != null) return;
            _statusTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ClientUiTypography.HudBody,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
        }

        private void EnsureDialogueStyles()
        {
            if (_dialogueWhoStyle == null)
            {
                _dialogueWhoStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = ClientUiTypography.HudTitle,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                _dialogueWhoStyle.normal.textColor = new Color(0.94f, 0.82f, 0.62f);
            }
            if (_dialogueLineStyle == null)
            {
                _dialogueLineStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = ClientUiTypography.HudBody,
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
                    fontSize = ClientUiTypography.HudBody,
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(14, 12, 6, 6)
                };
            }
            if (_dialogueHintStyle == null)
            {
                _dialogueHintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = ClientUiTypography.HudSecondary,
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
                EnsureDialogueStyles();
                GUILayout.Label(_talkFeedback, _dialogueHintStyle, GUILayout.MinHeight(66f));
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
                if (inviteableCount > 0 && IsAtTavern())
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

        private bool IsNearActivityPoi(string poiId, float radius)
        {
            var position = _frame?.Me?.Position;
            if (position == null || _layout?.Pois == null ||
                !_layout.Pois.TryGetValue(poiId, out var point) || point == null)
                return false;
            var dx = position.X - point.X;
            var dy = position.Y - point.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        // The server's TavernCounterView and me.pos use the same settlement-local coordinates.
        // This is only a presentation gate; BuyBeer always revalidates distance and keeper state.
        private bool IsAtTavernCounter()
        {
            var counter = _frame?.Place?.TavernCounter;
            var position = _frame?.Me?.Position;
            if (counter == null || position == null || counter.Radius <= 0d) return false;
            var dx = position.X - counter.X;
            var dy = position.Y - counter.Y;
            return dx * dx + dy * dy <= counter.Radius * counter.Radius;
        }

        private bool IsInsideTavernBuilding()
        {
            var position = _frame?.Me?.Position;
            var buildings = _layout?.Buildings;
            if (position == null || buildings == null) return false;
            foreach (var building in buildings)
            {
                if (building == null || !string.Equals(building.BuildingId, "tavern", StringComparison.Ordinal))
                    continue;
                if (position.X >= building.X0 && position.X <= building.X1 &&
                    position.Y >= building.Y0 && position.Y <= building.Y1)
                    return true;
            }
            return false;
        }

        private bool IsAtTavern()
        {
            // Mirrors the server's broad social check: exterior tavern POI OR inside its
            // server-projected building. Counter proximity remains a separate purchase rule.
            return IsNearActivityPoi("tavern", 2.5f) || IsInsideTavernBuilding() || IsAtTavernCounter();
        }

        private bool HasInventoryItem(string item)
        {
            return _frame?.Me?.Inventory?.Items != null &&
                _frame.Me.Inventory.Items.TryGetValue(item, out var quantity) && quantity > 0;
        }

        private bool HasStashGood(string good)
        {
            return _frame?.Me?.Stash != null &&
                _frame.Me.Stash.TryGetValue(good, out var quantity) && quantity >= 1d;
        }

        private void QueueQuickItemRequest(string op, string item, string label)
        {
            if (_core == null || !_core.QueueRequest(InventoryRequestLabelPrefix + " " + label,
                op, "item", item))
            {
                _status = "Не удалось отправить действие с предметом. Дождитесь ответа на предыдущее действие.";
                return;
            }
            _status = "Ожидаю подтверждения сервера: " + label + "…";
        }

        private Rect GetContextActionRect()
        {
            if (_frame?.Me == null || _frame.Place == null || _core == null ||
                _activityMiniGame != ActivityMiniGame.None)
                return new Rect(0f, 0f, 0f, 0f);
            if (!_hudCollapsed && Screen.width < 1100f)
                return new Rect(0f, 0f, 0f, 0f);

            var atTavern = _tavernPanelOpen && _frame.Place.Tavern && IsAtTavern();
            var atStable = _stablePanelOpen;
            var atMarket = _marketPanelOpen;
            var atActivity = _activityPanelOpen;
            if (!atTavern && !atMarket && !atStable && !atActivity)
                return new Rect(0f, 0f, 0f, 0f);

            var width = Mathf.Min(560f, Screen.width - 24f);
            var height = 42f;
            if (atMarket) height += 250f;
            if (atStable) height += 280f;
            if (atActivity) height += 380f;
            if (atTavern)
            {
                var rooms = _frame.Place.Rooms?.Items;
                var roomCount = rooms != null ? rooms.Count : 0;
                height += 34f + 112f + roomCount * 32f;
                height += 250f;
                var owned = _frame.Me.Room;
                if (owned != null && owned.Indoor)
                {
                    var inventoryCount = _frame.Me.Inventory?.Items?.Count ?? 0;
                    var chestCount = owned.ChestItems != null ? owned.ChestItems.Count : 0;
                    height += 68f + (inventoryCount + chestCount) * 30f;
                }
            }
            // The panel grows to fit normal use, then becomes a real scroll view on short screens
            // or when a room chest contains many items. Never let the content fall outside its area.
            height = Mathf.Min(height, Mathf.Max(160f, Screen.height - 24f));
            var panelX = _hudCollapsed ? (Screen.width - width) * 0.5f : Mathf.Max(12f, Screen.width - width - 12f);
            return new Rect(panelX, Screen.height - height - 12f, width, height);
        }

        private void DrawContextActions()
        {
            var panel = GetContextActionRect();
            if (panel.width <= 0f) return;

            var atTavern = _tavernPanelOpen && _frame.Place.Tavern && IsAtTavern();
            var atStable = _stablePanelOpen;
            var atMarket = _marketPanelOpen;
            var atActivity = _activityPanelOpen;

            GUILayout.BeginArea(panel);
            _contextActionScroll = GUILayout.BeginScrollView(_contextActionScroll,
                GUILayout.Width(panel.width), GUILayout.Height(panel.height));
            GUILayout.BeginVertical(GUI.skin.box);
            if (atActivity)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.IsNullOrWhiteSpace(_activityTargetTitle) ? "ДЕЙСТВИЯ" : _activityTargetTitle,
                    GUILayout.ExpandWidth(true));
                var closeActivity = GUILayout.Button("× Закрыть", GUILayout.Width(96f));
                if (closeActivity)
                {
                    _activityPanelOpen = false;
                    GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.EndScrollView(); GUILayout.EndArea();
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
                DrawActivityContextActions();
            }
            if (atMarket)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("РЫНОК", GUILayout.ExpandWidth(true));
                var goBack = GUILayout.Button("← Назад", GUILayout.Width(88f));
                var closeWindow = GUILayout.Button("× Закрыть", GUILayout.Width(96f));
                if (goBack || closeWindow)
                {
                    _marketPanelOpen = false;
                    GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.EndScrollView(); GUILayout.EndArea();
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
                _core.DrawMarketContextActions();
            }

            if (atStable)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("КОНЮШНЯ", GUILayout.ExpandWidth(true));
                var goBack = GUILayout.Button("← Назад", GUILayout.Width(88f));
                var closeWindow = GUILayout.Button("× Закрыть", GUILayout.Width(96f));
                if (goBack || closeWindow)
                {
                    _stablePanelOpen = false;
                    GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.EndScrollView(); GUILayout.EndArea();
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
                DrawStableActions();
            }

            if (atTavern)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("КОРЧМА", GUILayout.ExpandWidth(true));
                var goBack = GUILayout.Button("← Назад", GUILayout.Width(88f));
                var closeWindow = GUILayout.Button("× Закрыть", GUILayout.Width(96f));
                if (goBack || closeWindow)
                {
                    _tavernPanelOpen = false;
                    GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.EndScrollView(); GUILayout.EndArea();
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
                if (_frame.Me.Mug)
                {
                    if (GUILayout.Button("🍺 Выпить пиво")) QueueContextItemAction("consume", "beer", "Выпить пиво");
                }
                else
                {
                    var counter = _frame.Place.TavernCounter;
                    if (counter == null)
                    {
                        GUILayout.Label("Стойка корчмы пока не описана сервером.");
                    }
                    else if (!IsAtTavernCounter())
                    {
                        GUILayout.Label("Подойдите к стойке внутри корчмы, чтобы заказать пиво.");
                    }
                    else if (!counter.BartenderPresent)
                    {
                        GUILayout.Label("Корчмарь сейчас не на смене. Попробуйте позже.");
                    }
                    else if (_frame.Place.Beer <= 0d)
                    {
                        GUILayout.Label("Пиво закончилось.");
                    }
                    else if (_frame.Me.Coins < _frame.Place.BeerPrice)
                    {
                        GUILayout.Label("Не хватает монет на кружку.");
                    }
                    else if (GUILayout.Button("🍺 Заказать кружку · " +
                        _frame.Place.BeerPrice.ToString("0.#", CultureInfo.InvariantCulture) + " мон."))
                    {
                        QueueContextItemAction("buy", "beer", "Заказать пиво");
                    }
                }
                DrawCardGamePanel();
                DrawTavernRoomPanel();
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawActivityContextActions()
        {
            GUILayout.Label("Выбери занятие. Материалы, расстояние, силы и результат проверяет сервер.");

            if (_activityTargetId == "smithy")
            {
                if (GUILayout.Button("⚒ Помочь у горна · работа для села"))
                    QueueWorldAction("forge");
                DrawRecipeActions("smithy");
            }
            else if (_activityTargetId == "garden")
            {
                if (GUILayout.Button("🌿 Собрать травы"))
                    QueueWorldAction("gather");
                if (GUILayout.Button("🌾 Помочь в поле · сев или жатва по сезону"))
                    QueueWorldAction("farmwork");
                if (GUILayout.Button("🍯 Помочь на пасеке"))
                    QueueWorldAction("apiary");
            }
            else if (_activityTargetId == "hearth")
            {
                DrawRecipeActions("hearth");
            }
            else if (_activityTargetId == "woods")
            {
                if (!string.IsNullOrEmpty(_activityPrimaryAction))
                {
                    var primaryLabel = _activityPrimaryAction == "gather_brushwood"
                        ? "Собрать хворост" : "Рубить дерево";
                    if (GUILayout.Button("🌲 " + primaryLabel))
                        QueueWorldAction(_activityPrimaryAction);
                }
                if (GUILayout.Button("🏹 Поохотиться на дичь"))
                    QueueWorldAction("hunt");
                if (GUILayout.Button("⚔ Отогнать хищника"))
                    QueueWorldAction("fight");
            }
        }

        private void DrawRecipeActions(string station)
        {
            if (_core != null)
                _core.DrawCraftingContextActions(station);
        }

        private void DrawStableActions()
        {
            var stable = _frame?.Place?.Stable;
            var horse = _frame?.Me?.Horse;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("КОНЮШНЯ У АМБАРА");
            if (horse == null)
            {
                if (stable == null)
                {
                    GUILayout.Label("Сведения о конюшне ещё не получены от сервера.");
                }
                else if (stable.Horses < 1d)
                {
                    GUILayout.Label("Свободных лошадей сейчас нет. Загляните позже.");
                }
                else
                {
                    GUILayout.Label("Лошади: " + stable.Horses.ToString("0", CultureInfo.InvariantCulture) +
                        " · цена: " + stable.Price.ToString("0.#", CultureInfo.InvariantCulture) + " монет.");
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = _frame.Me.Coins >= stable.Price;
                    if (GUILayout.Button("Купить лошадь"))
                        QueueHorseAction("buy", "Купить лошадь");
                    GUI.enabled = previousEnabled;
                    if (_frame.Me.Coins < stable.Price)
                        GUILayout.Label("Не хватает монет. На счету: " +
                            _frame.Me.Coins.ToString("0.#", CultureInfo.InvariantCulture) + ".");
                }
            }
            else
            {
                GUILayout.Label(horse.Name + " · выносливость " +
                    horse.Stamina.ToString("0", CultureInfo.InvariantCulture) + " / 100");
                if (!horse.Here)
                {
                    GUILayout.Label("Лошадь осталась в другом поселении: вернитесь туда, чтобы оседлать её.");
                }
                else
                {
                    if (GUILayout.Button(horse.Mounted ? "Спешиться" : "Оседлать"))
                        QueueHorseAction(horse.Mounted ? "dismount" : "mount",
                            horse.Mounted ? "Спешиться" : "Оседлать лошадь");
                    var hasFeed = HasStashGood("hay") || HasStashGood("wheat") || HasStashGood("veg");
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = hasFeed && horse.Stamina < 100d;
                    if (GUILayout.Button("Покормить лошадь"))
                        QueueHorseAction("feed", "Покормить лошадь");
                    GUI.enabled = previousEnabled;
                    if (!hasFeed) GUILayout.Label("Для корма нужны сено, зерно или овощи в суме.");
                }
            }
            if (horse != null && horse.Here)
            {
                GUILayout.Space(6f);
                GUILayout.Label("СЕДЕЛЬНАЯ СУМКА · " +
                    horse.BagLoad.ToString("0.#", CultureInfo.InvariantCulture) + " / " +
                    horse.BagCapacity.ToString("0.#", CultureInfo.InvariantCulture));
                var stash = _frame.Me.Stash;
                var shownStow = 0;
                if (stash != null)
                {
                    foreach (var item in stash)
                    {
                        if (item.Value < 1d || shownStow >= 5) continue;
                        shownStow++;
                        if (GUILayout.Button("Положить на лошадь: " + item.Key + " ×1"))
                            QueueHorseAction("stow", "Положить в седельную сумку", item.Key, 1);
                    }
                }
                var shownTake = 0;
                if (horse.Bag != null)
                {
                    foreach (var item in horse.Bag)
                    {
                        if (item.Value <= 0d || shownTake >= 5) continue;
                        shownTake++;
                        if (GUILayout.Button("Забрать с лошади: " + item.Key + " ×1"))
                            QueueHorseAction("unstow", "Забрать из седельной сумки", item.Key, 1);
                    }
                }
                if (shownStow == 0 && shownTake == 0)
                    GUILayout.Label("Сумки пока пусты.");
            }
            if (_frame?.Me != null)
                DrawRecipeActions("granary");
            GUILayout.EndVertical();
        }

        private void QueueHorseAction(string action, string label, string good = null, int qty = 0)
        {
            var queued = _core != null && (string.IsNullOrEmpty(good)
                ? _core.QueueRequest(WorldActionLabelPrefix + " " + label, "horse", "action", action)
                : _core.QueueRequest(WorldActionLabelPrefix + " " + label, "horse",
                    "action", action, "good", good, "qty", qty));
            if (!queued)
            {
                _status = "Не удалось отправить действие с лошадью. Дождитесь ответа на предыдущее действие.";
                return;
            }
            _status = "Ожидаю подтверждения сервера: " + label.ToLowerInvariant() + "…";
        }

        private void DrawCardGamePanel()
        {
            var game = _frame?.Me?.CardGame;
            GUILayout.BeginVertical(GUI.skin.box);
            if (game != null && !string.IsNullOrEmpty(game.Game) && _tavernCardGameHidden)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("КАРТОЧНАЯ ПАРТИЯ ИДЁТ", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Вернуться к партии", GUILayout.Width(150f)))
                    _tavernCardGameHidden = false;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }

            if (game != null && !string.IsNullOrEmpty(game.Game))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("КАРТОЧНЫЙ СТОЛ", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("← Назад к корчме", GUILayout.Width(150f)))
                {
                    _tavernCardGameHidden = true;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    return;
                }
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label("КАРТОЧНЫЙ СТОЛ");

            if (game == null || string.IsNullOrEmpty(game.Game))
            {
                GUILayout.Label("Карточные игры доступны в корчме. Место, ставку и возможность начать проверит сервер.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Начать «Очко»"))
                    QueueCardGameAction("ochko", "start");
                if (GUILayout.Button("Начать «Дурак»"))
                    QueueCardGameAction("durak", "start");
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(game.Game == "durak" ? "Подкидной дурак" : "Очко");
                if (!string.IsNullOrEmpty(game.Message))
                    GUILayout.Label(game.Message, GUI.skin.box);
                GUILayout.Label("Ставка: " + game.Stake.ToString("0.#", CultureInfo.InvariantCulture) + " монет");
                if (game.Game == "ochko")
                    DrawOchkoGame(game);
                else if (game.Game == "durak")
                    DrawDurakGame(game);

                if (game.Done)
                {
                    if (GUILayout.Button("Начать новую партию"))
                    {
                        var nextGame = game.Game;
                        QueueCardGameAction(nextGame, "start");
                    }
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawOchkoGame(CardGameFrame game)
        {
            GUILayout.Label("Твои карты");
            DrawCardRow(game.Player);
            GUILayout.Label("Твои очки: " + game.PlayerTotal);
            GUILayout.Label("Корчмарь");
            DrawCardRow(game.Dealer);
            if (!game.Done)
                GUILayout.Label("Скрытых карт корчмаря: " + game.HiddenDealerCards);
            else if (game.DealerTotal.HasValue)
                GUILayout.Label("Очки корчмаря: " + game.DealerTotal.Value);

            if (!game.Done)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ещё карту"))
                    QueueCardGameAction("ochko", "hit");
                if (GUILayout.Button("Хватит"))
                    QueueCardGameAction("ochko", "stand");
                GUILayout.EndHorizontal();
            }
        }

        private void DrawDurakGame(CardGameFrame game)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Козырь: " + (game.Trump ?? "—"));
            GUILayout.FlexibleSpace();
            GUILayout.Label("Колода: " + game.DeckCount + " · у корчмаря: " + game.DealerCardCount);
            GUILayout.EndHorizontal();
            GUILayout.Label("Карты на столе");
            if (game.Table == null || game.Table.Count == 0)
                GUILayout.Label("Стол пока пуст.");
            else
            {
                for (var i = 0; i < game.Table.Count; i++)
                {
                    var pair = game.Table[i];
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label(pair?.Attack?.Code ?? "?", GUILayout.Width(80f));
                    GUILayout.Label("→", GUILayout.Width(24f));
                    GUILayout.Label(pair?.Defence?.Code ?? "—");
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Label("Твои карты");
            DrawCardSelectionRow(game.Player);
            if (game.Done) return;
            if (!game.YourTurn)
            {
                GUILayout.Label("Сейчас ход корчмаря…");
                return;
            }

            var hasSelection = game.Player != null && _selectedCardIndex >= 0 &&
                _selectedCardIndex < game.Player.Count;
            if (game.Role == "attack")
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = hasSelection;
                if (GUILayout.Button("Подкинуть выбранную карту"))
                    QueueCardGameAction("durak", "attack", _selectedCardIndex);
                GUI.enabled = true;
                if (GUILayout.Button("Бито / закончить заход"))
                    QueueCardGameAction("durak", "done");
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = hasSelection;
                if (GUILayout.Button("Отбиться выбранной картой"))
                    QueueCardGameAction("durak", "defend", _selectedCardIndex);
                GUI.enabled = true;
                if (GUILayout.Button("Взять карты"))
                    QueueCardGameAction("durak", "take");
                GUILayout.EndHorizontal();
            }
        }

        private void DrawCardRow(List<CardGameCardFrame> cards)
        {
            if (cards == null || cards.Count == 0)
            {
                GUILayout.Label("Карт нет.");
                return;
            }
            GUILayout.BeginHorizontal();
            for (var i = 0; i < cards.Count; i++)
            {
                var code = cards[i]?.Code ?? "?";
                GUILayout.Label(code, GUI.skin.box, GUILayout.MinWidth(42f), GUILayout.Height(38f));
            }
            GUILayout.EndHorizontal();
        }

        private void DrawCardSelectionRow(List<CardGameCardFrame> cards)
        {
            if (cards == null || cards.Count == 0)
            {
                GUILayout.Label("На руках нет карт.");
                _selectedCardIndex = -1;
                return;
            }
            if (_selectedCardIndex >= cards.Count) _selectedCardIndex = -1;
            GUILayout.BeginHorizontal();
            for (var i = 0; i < cards.Count; i++)
            {
                var previous = GUI.backgroundColor;
                if (_selectedCardIndex == i)
                    GUI.backgroundColor = new Color(0.86f, 0.66f, 0.32f, 1f);
                if (GUILayout.Button(cards[i]?.Code ?? "?", GUILayout.MinWidth(42f), GUILayout.Height(42f)))
                    _selectedCardIndex = i;
                GUI.backgroundColor = previous;
            }
            GUILayout.EndHorizontal();
        }

        private void QueueCardGameAction(string game, string action, int index = -1)
        {
            var queued = index >= 0
                ? _core != null && _core.QueueRequest("Карточная игра: " + action,
                    "card_game", "game", game, "action", action, "index", index)
                : _core != null && _core.QueueRequest("Карточная игра: " + action,
                    "card_game", "game", game, "action", action);
            if (!queued)
            {
                _status = "Не удалось отправить ход. Дождитесь ответа сервера.";
                return;
            }
            _selectedCardIndex = -1;
            if (action == "start") _tavernCardGameHidden = false;
            _status = "Ожидаю подтверждения сервера: " + action + "…";
        }

        private void DrawTavernRoomPanel()
        {
            var rooms = _frame?.Place?.Rooms?.Items;
            var owned = _frame?.Me?.Room;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("КОМНАТЫ НАД КОРЧМОЙ");
            GUILayout.Label("Аренда уже работает. Вход и сундук станут доступны после подключения физического интерьера.");
            if (rooms == null || rooms.Count == 0)
            {
                GUILayout.Label("Каталог комнат пока не получен от сервера.");
                GUILayout.EndVertical();
                return;
            }

            for (var i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null || string.IsNullOrEmpty(room.Id)) continue;
                var isMine = owned != null && string.Equals(owned.Id, room.Id, StringComparison.Ordinal);
                GUILayout.BeginHorizontal();
                var roomNumber = i == 0 ? "I" : i == 1 ? "II" :
                    (i + 1).ToString(CultureInfo.InvariantCulture);
                GUILayout.Label("Комната " + roomNumber + " · " + room.LeaseState + " · " +
                    room.RentPrice.ToString("0.#", CultureInfo.InvariantCulture) + " мон.");
                if (isMine)
                {
                    if (owned.LeaseState == "expired")
                    {
                        var previousEnabled = GUI.enabled;
                        GUI.enabled = _frame.Me.Coins >= owned.RentPrice;
                        if (GUILayout.Button("Продлить"))
                            QueueRoomAction("rent", owned.Id, null, 0, "Продлить аренду");
                        GUI.enabled = previousEnabled;
                    }

                    // Expiration must never trap a tenant who was already inside when the lease ended.
                    // ExitRoom is allowed regardless of lease state; chest access remains blocked.
                    if (owned.Indoor)
                    {
                        if (GUILayout.Button("Выйти"))
                            QueueRoomAction("exit", null, null, 0, "Выйти из комнаты");
                    }
                    else if (owned.LeaseState != "expired")
                    {
                        GUILayout.Label("Интерьер пока не подключён");
                    }
                }
                else if (owned == null && room.Available)
                {
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = _frame.Me.Coins >= room.RentPrice;
                    if (GUILayout.Button("Снять"))
                        QueueRoomAction("rent", room.Id, null, 0, "Снять комнату");
                    GUI.enabled = previousEnabled;
                }
                else
                {
                    GUILayout.Label(room.Available ? "Недоступно" : "Занята / недоступна");
                }
                GUILayout.EndHorizontal();
            }

            if (owned != null && owned.Indoor &&
                (owned.LeaseState == "active" || owned.LeaseState == "grace"))
            {
                GUILayout.Label("Личный сундук · " +
                    owned.ChestLoad.ToString("0.#", CultureInfo.InvariantCulture) + " / " +
                    owned.ChestCapacity.ToString("0.#", CultureInfo.InvariantCulture));
                GUILayout.Label("Положить в сундук (по 1 шт.):");
                var inventory = _frame.Me.Inventory?.Items;
                if (inventory != null)
                {
                    foreach (var pair in inventory)
                    {
                        if (pair.Value <= 0) continue;
                        var previousEnabled = GUI.enabled;
                        GUI.enabled = pair.Value > 0;
                        if (GUILayout.Button("＋ " + pair.Key + " ×" + pair.Value))
                            QueueRoomAction("stow", owned.Id, pair.Key, 1, "Положить в сундук: " + pair.Key);
                        GUI.enabled = previousEnabled;
                    }
                }
                GUILayout.Label("Забрать из сундука (по 1 шт.):");
                if (owned.ChestItems != null)
                {
                    foreach (var pair in owned.ChestItems)
                    {
                        if (pair.Value <= 0) continue;
                        if (GUILayout.Button("− " + pair.Key + " ×" + pair.Value))
                            QueueRoomAction("take", owned.Id, pair.Key, 1, "Забрать из сундука: " + pair.Key);
                    }
                }
            }
            else
            {
                GUILayout.Label("Для доступа к сундуку нужна своя действующая аренда и вход в комнату.");
            }
            GUILayout.EndVertical();
        }

        private void QueueRoomAction(string action, string target, string item, int quantity, string label)
        {
            if (_core == null)
            {
                _status = "Мир ещё не подключён.";
                return;
            }

            bool queued;
            if (item != null)
                queued = _core.QueueRequest(label, "room", "action", action, "target", target,
                    "item", item, "qty", quantity);
            else if (target != null)
                queued = _core.QueueRequest(label, "room", "action", action, "target", target);
            else
                queued = _core.QueueRequest(label, "room", "action", action);

            _status = queued
                ? "Ожидаю подтверждения сервера: " + label + "…"
                : "Не удалось отправить действие: дождитесь ответа на предыдущее.";
        }

        private void QueueContextItemAction(string op, string item, string label)
        {
            if (_core == null || !_core.QueueRequest(label, op, "item", item))
            {
                _status = "Не удалось отправить действие: дождитесь ответа на предыдущее.";
                return;
            }
            _status = "Ожидаю подтверждения сервера: " + label + "…";
        }

        private void DrawContextToolAction(string item, int price, string label,
            ActivityMiniGame game, string hotkey)
        {
            if (!HasInventoryItem(item))
            {
                var previous = GUI.enabled;
                GUI.enabled = _frame.Me.Coins >= price;
                if (GUILayout.Button("Купить " + label + " · " + price + " мон."))
                    QueueQuickItemRequest("take", item, "покупка " + label.ToLowerInvariant());
                GUI.enabled = previous;
                return;
            }

            if (!string.Equals(_frame.Me.Inventory.Hand, item, StringComparison.Ordinal))
            {
                if (GUILayout.Button("Взять " + label.ToLowerInvariant() + " в руку"))
                    QueueQuickItemRequest("equip", item, "экипировка " + label.ToLowerInvariant());
                return;
            }

            if (GUILayout.Button((game == ActivityMiniGame.Woodcutting ? "Рубить" : "Закинуть удочку") +
                " [" + hotkey + "]"))
                StartActivityMiniGame(game);
        }

        private void StartActivityMiniGame(ActivityMiniGame game)
        {
            if (_connectionState != WorldConnectionState.Active || _core == null)
            {
                _activityMiniGameFeedback = "Сначала подключитесь к миру.";
                _status = _activityMiniGameFeedback;
                return;
            }
            var poi = game == ActivityMiniGame.Woodcutting ? "woods" : "river";
            var feature = game == ActivityMiniGame.Woodcutting ? "woods" : "river";
            var tool = game == ActivityMiniGame.Woodcutting ? "axe" : "rod";
            if (!_core.SupportsFeature(feature))
            {
                _activityMiniGameFeedback = game == ActivityMiniGame.Woodcutting
                    ? "В этих угодьях нет подходящего леса."
                    : "В этом поселении нет подходящего водоёма.";
                _status = _activityMiniGameFeedback;
                return;
            }
            if (!IsNearActivityPoi(poi, 10f))
            {
                _activityMiniGameFeedback = game == ActivityMiniGame.Woodcutting
                    ? "Подойдите ближе к лесу, чтобы рубить."
                    : "Подойдите к берегу, чтобы рыбачить.";
                _status = _activityMiniGameFeedback;
                return;
            }
            if (!string.Equals(_frame?.Me?.Inventory?.Hand, tool, StringComparison.Ordinal))
            {
                _activityMiniGameFeedback = "Сначала купите " + (tool == "axe" ? "топор" : "удочку") +
                    " и возьмите его в руку.";
                _status = _activityMiniGameFeedback;
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
                GUILayout.Label("Точные удары: " + _woodcuttingGoodHits + " / 3 · Space — ударить · Esc — отойти");
                DrawTimingTrack(phase, 0.40f, 0.18f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ударить [Space]", GUILayout.Height(34f)))
                    HandleActivityMiniGamePrimaryAction();
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
                    GUILayout.Label("Ждите поклёвку. Space — подсечь, когда клюнет · Esc — свернуть.");
                }
                else if (elapsed <= biteAt + biteDuration)
                {
                    GUILayout.Label("ПОКЛЁВКА! Подсекайте!");
                    GUILayout.HorizontalSlider((biteAt + biteDuration - elapsed) / biteDuration, 0f, 1f);
                    if (GUILayout.Button("Подсечь! [Space]", GUILayout.Height(38f)))
                        HandleActivityMiniGamePrimaryAction();
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

        private void HandleActivityMiniGamePrimaryAction()
        {
            if (_activityMiniGame == ActivityMiniGame.None) return;
            var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _activityMiniGameStartedAt);
            if (_activityMiniGame == ActivityMiniGame.Woodcutting)
            {
                var phase = Mathf.PingPong(elapsed * 0.85f, 1f);
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
                return;
            }

            const float biteAt = 2.0f;
            const float biteDuration = 2.6f;
            if (elapsed < biteAt)
            {
                _activityMiniGameFeedback = "Пока рано — дождитесь поклёвки.";
                return;
            }
            if (elapsed <= biteAt + biteDuration)
            {
                _views?.PlayPlayerActionGesture("fish_reel");
                _activityMiniGame = ActivityMiniGame.None;
                _activityMiniGameFeedback = "Подсечка выполнена. Сервер проверяет водоём, снасти и улов…";
                QueueWorldAction("fish");
            }
            else
            {
                _activityMiniGameFeedback = "Рыба ушла. Забросьте удочку ещё раз.";
            }
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
                var op = payload != null && payload.TryGetValue("op", out var opValue) ? Convert.ToString(opValue, CultureInfo.InvariantCulture) : "unknown";
                var reqid = payload != null && payload.TryGetValue("reqid", out var reqValue) ? Convert.ToString(reqValue, CultureInfo.InvariantCulture) : "";
                ClientDiagnosticDumps.RecordEvent("intent.send", "op=" + op + (string.IsNullOrEmpty(reqid) ? "" : " reqid=" + reqid));
                await socket.SendPayloadAsync(payload, _lifetime.Token);
            }
            catch (Exception ex)
            {
                ClientDiagnosticDumps.RecordEvent("intent.error", ex.GetType().Name + ": " + ex.Message);
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

        private void OnDrinkPresented()
        {
            _audioDirector?.SetPlayerPosition(_views?.PlayerTransform != null ? _views.PlayerTransform.position : transform.position);
            _audioDirector?.DrinkBeer();
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
                // Invite/accept/decline ACKs are not drinking events. Audio is triggered
                // separately by EntityViewRegistry only when the server publishes a drink transition.
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

        private void UpdateServerAudioAnchors(Transform player)
        {
            if (_audioDirector == null) return;
            var poiMap = _layout != null ? _layout.Pois : null;
            var mePosition = _frame?.Me?.Position;
            var layoutMatchesPlace = _frame?.Place != null &&
                PoiPositionPolicy.IsLayoutForSettlement(_layoutSettlementId, _frame.Place.Id);
            if (player == null || mePosition == null || poiMap == null || !layoutMatchesPlace)
            {
                _villageSoundscape?.SetServerAnchor("hearth", Vector3.zero, false);
                _villageSoundscape?.SetServerAnchor("forge", Vector3.zero, false);
                _villageSoundscape?.SetServerAnchor("tavern", Vector3.zero, false);
                _villageSoundscape?.SetServerAnchor("water", Vector3.zero, false);
                return;
            }

            // Layout coordinates are settlement-local metres (+X east, +Y north).
            // Offset from the server-positioned player, so no sound is tied to a scene origin.
            SetServerSoundAnchor(player, mePosition.X, mePosition.Y, "hearth",
                position => _villageSoundscape?.SetServerAnchor("hearth", position, true));
            SetServerSoundAnchor(player, mePosition.X, mePosition.Y, "smithy",
                position => _villageSoundscape?.SetServerAnchor("forge", position, true));
            SetServerSoundAnchor(player, mePosition.X, mePosition.Y, "tavern",
                position => _villageSoundscape?.SetServerAnchor("tavern", position, true));
            SetServerSoundAnchor(player, mePosition.X, mePosition.Y, "river",
                position => _villageSoundscape?.SetServerAnchor("water", position, true));
        }

        private void SetServerSoundAnchor(Transform player, double playerX, double playerY,
            string poiId, Action<Vector3> apply)
        {
            if (apply == null) return;
            if (_layout?.Pois == null || !_layout.Pois.TryGetValue(poiId, out var poi) || poi == null)
            {
                _villageSoundscape?.SetServerAnchor(
                    poiId == "smithy" ? "forge" : poiId == "river" ? "water" : poiId,
                    Vector3.zero, false);
                return;
            }
            var delta = new Vector3((float)(poi.X - playerX), 0f, (float)(poi.Y - playerY));
            apply(player.position + delta);
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
            if (IsTavernInteractionPoi(poi))
            {
                var open = !_tavernPanelOpen;
                _marketPanelOpen = false;
                _stablePanelOpen = false;
                _activityPanelOpen = false;
                _tavernPanelOpen = open;
                if (_tavernPanelOpen) CollapseHudForContextWindow();
                _status = _tavernPanelOpen ? "Корчма открыта." : "Окно корчмы закрыто.";
                return;
            }
            if (IsMarketInteractionPoi(poi))
            {
                var open = !_marketPanelOpen;
                _stablePanelOpen = false;
                _tavernPanelOpen = false;
                _activityPanelOpen = false;
                _marketPanelOpen = open;
                if (_marketPanelOpen) CollapseHudForContextWindow();
                _status = _marketPanelOpen
                    ? "Лавка открыта. Покупки и продажи подтверждает сервер."
                    : "Лавка закрыта.";
                return;
            }
            if (IsStableInteractionPoi(poi))
            {
                var open = !_stablePanelOpen;
                _marketPanelOpen = false;
                _tavernPanelOpen = false;
                _activityPanelOpen = false;
                _stablePanelOpen = open;
                if (_stablePanelOpen) CollapseHudForContextWindow();
                _status = _stablePanelOpen
                    ? "Конюшня открыта. Покупку и уход за лошадью подтверждает сервер."
                    : "Конюшня закрыта.";
                return;
            }
            if (_connectionState != WorldConnectionState.Active)
            {
                _status = "Сначала подключитесь к миру, затем выбирайте точки деревни.";
                return;
            }

            var poiId = (poi.TargetId ?? string.Empty).ToLowerInvariant();
            if (poiId == "smithy" || poiId == "garden" || poiId == "hearth" || poiId == "woods")
            {
                _marketPanelOpen = false;
                _stablePanelOpen = false;
                _tavernPanelOpen = false;
                _activityTargetId = poiId;
                _activityTargetTitle = poi.Title;
                _activityPrimaryAction = poi.ActionIntent;
                _activityPanelOpen = true;
                CollapseHudForContextWindow();
                _status = "Открыты действия: " + poi.Title + ". Результат каждого действия подтвердит сервер.";
                return;
            }

            // The current Go server does not implement op:"interact". Never send
            // interaction_id as a command; descriptors are translated by the target builder
            // only when their target ID has a supported v1 action/horse mapping.
            if (!string.IsNullOrEmpty(poi.InteractionId))
            {
                _status = "Эта точка пока не поддерживает серверное действие.";
                return;
            }

            if (!string.IsNullOrEmpty(poi.ActionIntent))
            {
                QueueWorldAction(poi.ActionIntent);
                return;
            }

            // E is for an immediate interaction at this place. Long-lived character goals
            // are configured only from the character UI, never as a side effect of touching a POI.
            _status = "Для этой точки пока нет отдельного действия. Долгосрочные приоритеты задаются в разделе персонажа.";
        }

        private void CollapseHudForContextWindow()
        {
            _hudCollapsed = true;
            _core?.SetExternalCharacterMenuOpen(false);
        }

        private static bool IsTavernInteractionPoi(WorldPoi poi)
        {
            if (poi == null) return false;
            var type = (poi.TargetType ?? string.Empty).ToLowerInvariant();
            var id = (poi.TargetId ?? string.Empty).ToLowerInvariant();
            var title = (poi.Title ?? string.Empty).ToLowerInvariant();
            return type == "tavern" || type == "inn" || id == "tavern" ||
                title.Contains("корчм") || title.Contains("трактир");
        }

        private static bool IsMarketInteractionPoi(WorldPoi poi)
        {
            if (poi == null) return false;
            var type = (poi.TargetType ?? string.Empty).ToLowerInvariant();
            var id = (poi.TargetId ?? string.Empty).ToLowerInvariant();
            var title = (poi.Title ?? string.Empty).ToLowerInvariant();
            return type == "market" || type == "stall" || type == "shop" ||
                id == "market" || id == "stall" || id == "market_stall" ||
                title.Contains("рынок") || title.Contains("лавк") || title.Contains("прилав");
        }

        private static bool IsStableInteractionPoi(WorldPoi poi)
        {
            if (poi == null) return false;
            var type = (poi.TargetType ?? string.Empty).ToLowerInvariant();
            var id = (poi.TargetId ?? string.Empty).ToLowerInvariant();
            var title = (poi.Title ?? string.Empty).ToLowerInvariant();
            return type == "stable" || type == "barn" || id == "granary" ||
                id == "stable" || title.Contains("конюш") || title.Contains("амбар");
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

        private void RequestOfflinePriority(string goal)
        {
            // Goals are long-lived character priorities, not one-shot activities or tutorial progress.
            _priorityToSend = goal;
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
            _layoutRequestGeneration++;
            _worldManifestGeneration++;
            _worldManifestRefreshInFlight = false;
            _layoutPrefetchInFlight.Clear();
            _layoutPrefetchRetryAfter.Clear();
            _nextWorldManifestRefreshAt = Time.unscaledTime + WorldManifestRefreshSeconds;
            _nextSettlementPrefetchAt = 0f;
            _worldManifestLastPlaceId = null;
            _layoutRequestedFor = null;
            _layoutFailedFor = null;
            _marketPanelOpen = false;
            _stablePanelOpen = false;
            _tavernPanelOpen = false;
            _layoutRetryAfter = 0f;
            // Cache keys are settlement IDs, not world/session IDs. Never reuse roots from
            // a previous login: another seed/account may reuse the same settlement ID.
            _layoutCache?.Clear();
            _worldManifest = null;
            _worldMapCachedManifest = null;
            _prototypeSettlementId = null;
            _layoutSettlementId = null;
            _layout = null;
            _starterLayout = null;
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
                // v1.22 static catalogue: optional for backward-compatible world entry.
                try
                {
                    var catalog = await http.GetCatalogAsync(_lifetime.Token);
                    Enqueue(() =>
                    {
                        _worldCatalog = catalog;
                        _core?.SetCatalog(catalog);
                    });
                }
                catch (Exception catalogError)
                {
                    Enqueue(() => Debug.LogWarning("Каталог v1.22 пока недоступен; игровые кадры продолжают работать: " + catalogError.Message));
                }
                _layout = layout;
                _starterLayout = SettlementLayoutSelectionPolicy.IsTutorialLayout(layout.Plan) ? layout : null;
                // v1.19: read global map metadata before enabling the socket. Older compatible servers
                // may not expose it, so this optional layer must never block the settlement demo.
                try
                {
                    _worldManifest = await http.GetWorldManifestAsync();
                    _worldManifestGeneration++;
                    _nextWorldManifestRefreshAt = Time.unscaledTime + WorldManifestRefreshSeconds;
                }
                catch (Exception worldError)
                {
                    _worldManifest = null;
                    Enqueue(() => Debug.LogWarning("Карта мира v1.19 пока недоступна; остаёмся в локальном режиме поселения: " + worldError.Message));
                }
                WorldWildSitePresentationBuilder.Clear();
                _terrainStreamGeneration++;
                CancelAllTerrainChunkRequests();
                _terrainChunks.Clear();
                _spaceChunks.Clear();
                _floatingOriginAnchor = null;
                _terrainChunkRequests.Clear();
                _terrainChunkRetryAfter.Clear();
                _hasTerrainCenter = false;
                WorldTerrainChunkBuilder.Clear(_presentationRoot);
                WorldSpacePresentationBuilder.Clear(_presentationRoot);
                WorldFrontierBiomeBuilder.Clear(_presentationRoot);
                CauldronRimPresentationBuilder.Clear(_presentationRoot);
                _cameraRig?.SetWorldHorizonDistance(0f);
                _terrainAppliedSettlementId = null;
                _terrainStreamSettlementId = null;
                _terrainAppliedAnchorEast = double.NaN;
                _terrainAppliedAnchorNorth = double.NaN;
                _terrainAppliedAnchorUp = double.NaN;
                // After a reconnect another settlement's layout may still be on screen: start again from
                // the default one; EnsureLayoutForPlace switches to the right settlement on the first frame.
                var hasExistingLayout = _presentationRoot != null &&
                    _presentationRoot.Find("ServerLayoutV2") != null;
                _layoutSettlementId = null;
                _prototypeSettlementId = null;
                _layoutRequestedFor = null;
                if (hasExistingLayout) WorldLayoutPresentationBuilder.Rebuild(_presentationRoot, layout);
                else WorldLayoutPresentationBuilder.Build(_presentationRoot, layout);
                var presentationCamera = _cameraRig != null
                    ? _cameraRig.GetComponent<Camera>() : Camera.main;
                RuralHorizonBackdrop.Build(_presentationRoot, presentationCamera, (float)layout.WorldHalf);
                _initialFullFrameBuffer.Begin(socket, DateTime.UtcNow.AddSeconds(5));
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
                    _initialFullFrameBuffer.End(socket);
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

            _initialFullFrameBuffer.End(socket);
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
            // Fast frames are explicit validated partial patches, never full snapshots.
            // Malformed fast messages are dropped here and are not retried as v1 snapshots.
            try
            {
                if (FastFrameParser.TryParse(json, out var fastPatch))
                {
                    ClientDiagnosticDumps.RecordFrame("fast", json);
                    Enqueue(() =>
                    {
                        if (ReferenceEquals(_socket, socket) && _connectionState == WorldConnectionState.Active && _core != null)
                            _core.ApplyFastFrame(fastPatch);
                    });
                    return;
                }
            }
            catch (FormatException ex)
            {
                ClientDiagnosticDumps.RecordFrame("rejected", json);
                ClientDiagnosticDumps.RecordEvent("frame.rejected.fast", ex.Message);
                Debug.LogWarning("[Pogranichye] Отклонён некорректный быстрый кадр: " + ex.Message);
                return;
            }

            // Buffer only the socket that owns the current connection attempt. A stale
            // callback from a replaced socket falls through and is rejected by the normal
            // ReferenceEquals guard in ProcessFullWorldFrame's main-thread continuation.
            if (_initialFullFrameBuffer.TryBuffer(socket, json, DateTime.UtcNow))
                return;

            ProcessFullWorldFrame(socket, json);
        }

        private void ProcessFullWorldFrame(WorldSocketClient socket, string json)
        {
            try
            {
                var frame = WorldFrameParser.Parse(json, out var frameDocument);
                ClientDiagnosticDumps.RecordFrame("full", json);
                Enqueue(() =>
                {
                    // Ignore frames queued by a socket that has already been replaced or closed.
                    if (!ReferenceEquals(_socket, socket) || _connectionState == WorldConnectionState.Closed)
                        return;

                    var enteringWorld = _connectionState != WorldConnectionState.Active;
                    _frame = frame;
                    // Keep presentation lighting aligned with authoritative world time.
                    // Legacy v1 servers without hour retain the local visual preview cycle.
                    if (frame.Hour.HasValue) WorldEnvironmentBuilder.SetServerHour(frame.Hour.Value);
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
                        // Immersive default: enter the world with the character journal closed.
                        // Tab opens it; Esc opens the separate system menu.
                        if (enteringWorld) _hudCollapsed = true;
                        _connectionState = WorldConnectionState.Active;
                        _views.Apply(frame, ResolveWorldAnchor(frame));
                        if (_core != null) _core.ApplyFrame(frame, frameDocument);
                        if (frame.Me.Position != null) ReconcileMovement(frame.Me);
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
                ClientDiagnosticDumps.RecordFrame("rejected", json);
                ClientDiagnosticDumps.RecordEvent("frame.rejected.full", ex.GetType().Name + ": " + ex.Message);
                Enqueue(() =>
                {
                    if (!ReferenceEquals(_socket, socket)) return;

                    // A rejected first full frame happens before the player can open F3.
                    // Save the already-captured rejected payload automatically so startup
                    // protocol failures always leave a local diagnostic artifact.
                    SaveClientDiagnosticDump();
                    _status = "Ошибка кадра: " + ex.Message +
                        " | " + _lastDiagnosticDumpStatus;
                });
            }
        }

        private JArray BuildTerrainDiagnosticSnapshot()
        {
            var snapshot = new JArray();
            foreach (var pair in _terrainChunks)
            {
                var chunk = pair.Value;
                if (chunk == null) continue;

                double minHeight = double.PositiveInfinity;
                double maxHeight = double.NegativeInfinity;
                if (chunk.Heights != null)
                {
                    foreach (var height in chunk.Heights)
                    {
                        if (height < minHeight) minHeight = height;
                        if (height > maxHeight) maxHeight = height;
                    }
                }

                var item = new JObject
                {
                    ["cache_key"] = pair.Key,
                    ["id"] = chunk.Id,
                    ["frame"] = chunk.Frame,
                    ["chunk_x"] = chunk.Chunk != null && chunk.Chunk.Length == 2
                        ? (JToken)new JValue(chunk.Chunk[0]) : JValue.CreateNull(),
                    ["chunk_z"] = chunk.Chunk != null && chunk.Chunk.Length == 2
                        ? (JToken)new JValue(chunk.Chunk[1]) : JValue.CreateNull(),
                    ["origin_east"] = chunk.Origin != null ? (JToken)new JValue(chunk.Origin.East) : JValue.CreateNull(),
                    ["origin_north"] = chunk.Origin != null ? (JToken)new JValue(chunk.Origin.North) : JValue.CreateNull(),
                    ["size_m"] = chunk.SizeMeters,
                    ["step_m"] = chunk.StepMeters,
                    ["side"] = chunk.Side,
                    ["height_samples"] = chunk.Heights != null ? chunk.Heights.Count : 0,
                    ["height_min_m"] = double.IsInfinity(minHeight) ? (JToken)JValue.CreateNull() : new JValue(minHeight),
                    ["height_max_m"] = double.IsInfinity(maxHeight) ? (JToken)JValue.CreateNull() : new JValue(maxHeight),
                    ["generator_version"] = chunk.GeneratorVersion.HasValue
                        ? (JToken)new JValue(chunk.GeneratorVersion.Value) : JValue.CreateNull(),
                    ["seed"] = chunk.Seed.HasValue ? (JToken)new JValue(chunk.Seed.Value) : JValue.CreateNull(),
                    ["biome"] = chunk.Biome,
                    ["roads"] = chunk.Roads != null ? chunk.Roads.Count : 0,
                    ["water"] = chunk.Water != null ? chunk.Water.Count : 0,
                    ["resources"] = chunk.Resources != null ? chunk.Resources.Count : 0,
                    ["wild_hints"] = chunk.Wild != null ? chunk.Wild.Count : 0,
                    ["settlements"] = chunk.Settlements != null ? chunk.Settlements.Count : 0,
                    ["valid"] = chunk.IsValid
                };

                if (chunk.Cover != null)
                {
                    item["cover"] = new JObject
                    {
                        ["forest"] = chunk.Cover.Forest,
                        ["grass"] = chunk.Cover.Grass,
                        ["rock"] = chunk.Cover.Rock,
                        ["wet"] = chunk.Cover.Wet
                    };
                }
                else item["cover"] = JValue.CreateNull();

                snapshot.Add(item);
            }
            return snapshot;
        }

        private JArray BuildTerrainRequestSnapshot()
        {
            var snapshot = new JArray();
            foreach (var requestId in _terrainChunkRequests)
                snapshot.Add(requestId);
            return snapshot;
        }

        private void SaveClientDiagnosticDump()
        {
            try
            {
                var me = _frame != null ? _frame.Me : null;
                var position = me != null ? me.Position : null;
                var worldPosition = me != null ? me.WorldPosition : null;
                var place = _frame != null ? _frame.Place : null;
                var counter = place != null ? place.TavernCounter : null;
                var neighborSummary = new Newtonsoft.Json.Linq.JArray();
                if (place?.Neighbors != null)
                {
                    foreach (var neighbor in place.Neighbors)
                    {
                        if (neighbor == null) continue;
                        var neighborPosition = neighbor.Position;
                        neighborSummary.Add(new Newtonsoft.Json.Linq.JObject
                        {
                            ["id"] = neighbor.Id,
                            ["player"] = neighbor.Controlled,
                            ["role"] = neighbor.Role,
                            ["act_code"] = neighbor.ActivityCode,
                            ["pos"] = neighborPosition == null
                                ? (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.Linq.JValue.CreateNull()
                                : new Newtonsoft.Json.Linq.JObject
                                {
                                    ["x"] = neighborPosition.X,
                                    ["y"] = neighborPosition.Y,
                                    ["facing"] = neighborPosition.Facing
                                }
                        });
                    }
                }
                var context = new Newtonsoft.Json.Linq.JObject
                {
                    ["unity_version"] = Application.unityVersion,
                    ["platform"] = Application.platform.ToString(),
                    ["device_model"] = SystemInfo.deviceModel,
                    ["server"] = ClientDiagnosticDumps.SafeServerAddress(serverUrl),
                    ["connection_state"] = _connectionState.ToString(),
                    ["socket_connected"] = _socket != null && _socket.IsConnected,
                    ["place_id"] = _frame?.Place?.Id,
                    ["place_name"] = _frame?.Place?.Name,
                    ["day"] = _frame != null ? (JToken)new JValue(_frame.Day) : JValue.CreateNull(),
                    ["year"] = _frame != null ? (JToken)new JValue(_frame.Year) : JValue.CreateNull(),
                    ["season"] = _frame?.Season,
                    ["hour"] = _frame != null && _frame.Hour.HasValue ? (JToken)_frame.Hour.Value : JValue.CreateNull(),
                    ["scene_position"] = position == null ? JValue.CreateNull() : new Newtonsoft.Json.Linq.JObject
                    {
                        ["x"] = position.X, ["y"] = position.Y, ["facing"] = position.Facing
                    },
                    ["world_position"] = worldPosition == null ? JValue.CreateNull() : new Newtonsoft.Json.Linq.JObject
                    {
                        ["east"] = worldPosition.East, ["north"] = worldPosition.North, ["frame"] = worldPosition.Frame
                    },
                    ["neighbor_count"] = place?.Neighbors?.Count ?? 0,
                    // Compact tavern field snapshot: no names, chat, voice or credentials.
                    ["tavern"] = place != null && place.Tavern,
                    ["beer_stock"] = place != null ? (Newtonsoft.Json.Linq.JToken)place.Beer : Newtonsoft.Json.Linq.JValue.CreateNull(),
                    ["beer_price"] = place != null ? (Newtonsoft.Json.Linq.JToken)place.BeerPrice : Newtonsoft.Json.Linq.JValue.CreateNull(),
                    ["mug"] = me != null && me.Mug,
                    ["tavern_counter"] = counter == null
                        ? (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.Linq.JValue.CreateNull()
                        : new Newtonsoft.Json.Linq.JObject
                        {
                            ["x"] = counter.X, ["y"] = counter.Y, ["radius"] = counter.Radius,
                            ["bartender_on_shift"] = counter.BartenderPresent
                        },
                    ["neighbors"] = neighborSummary,
                    ["remote_view_count"] = _views?.RemoteViewCount ?? 0,
                    ["terrain_chunks"] = _terrainChunks.Count,
                    ["terrain_chunks_detail"] = BuildTerrainDiagnosticSnapshot(),
                    ["terrain_requests_in_flight"] = _terrainChunkRequests.Count,
                    ["terrain_request_ids"] = BuildTerrainRequestSnapshot(),
                    ["move_queue"] = _moveSendQueue.Count,
                    ["move_pending"] = _pendingMoveIntents.Count,
                    ["move_dropped"] = _discardedMoveIntentCount,
                    ["realtime_seconds"] = Time.realtimeSinceStartup
                };
                var savedPath = ClientDiagnosticDumps.SaveDump(context);
                _lastDiagnosticDumpStatus = "Сохранено: " + savedPath;
                ClientDiagnosticDumps.RecordEvent("dump.saved", "Manual F3 capture");
                Debug.Log("[Pogranichye] Диагностический дамп сохранён: " + savedPath);
            }
            catch (Exception ex)
            {
                _lastDiagnosticDumpStatus = "Ошибка дампа: " + ex.Message;
                ClientDiagnosticDumps.RecordEvent("dump.error", ex.GetType().Name + ": " + ex.Message);
                Debug.LogError("[Pogranichye] Не удалось сохранить диагностический дамп: " + ex);
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

            // In third-person the body performs the visible jump while the camera stays
            // level; moving both together made the jump read as a camera bob instead of a
            // character leaving the ground. First-person keeps the camera offset for eye height.
            if (_views != null) _views.SetPlayerPresentationOffset(Vector3.up * _jumpOffset);
            if (_cameraRig != null) _cameraRig.SetJumpOffset(_firstPerson ? _jumpOffset : 0f);
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
            // Manual directional input always takes priority and cancels auto-walk.
            if (_autoWalk && keyboardInput.sqrMagnitude > 0.01f)
                _autoWalk = false;
            if (_autoWalk)
                keyboardInput = Vector2.up;
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
            // Do not play a generic impact on raw mouse/key input. Weapon-specific SFX
            // will be triggered only from a server-confirmed combat outcome.
            if (_input != null && _input.WeavePressed) _audioDirector?.Magic();
            var runRequested = moving && _input != null && (_input.JogHeld || _input.SprintHeld);
            var fastRequested = runRequested && _input != null && _input.SprintHeld;
            var canRun = runRequested && GetPredictedStamina() > 0f;
            var movementProfile = _worldManifest != null ? _worldManifest.Movement : null;
            var walkSpeed = movementProfile != null && movementProfile.WalkMetersPerSecond > 0d
                ? (float)movementProfile.WalkMetersPerSecond : 1.4f;
            var jogSpeed = movementProfile != null && movementProfile.JogMetersPerSecond > 0d
                ? (float)movementProfile.JogMetersPerSecond : 3.2f;
            var runSpeed = movementProfile != null && movementProfile.RunMetersPerSecond > 0d
                ? (float)movementProfile.RunMetersPerSecond : 6.0f;
            var speed = canRun ? (fastRequested ? runSpeed : jogSpeed) : walkSpeed;
            if (moving)
                speed *= ResolveMovementTerrainMultiplier(player.position,
                    new Vector2(worldMove.x, worldMove.z), movementProfile);
            if (moving) _audioDirector?.Footsteps(canRun);
            var motion = player.GetComponent<EntityMotion>();
            if (motion != null)
                motion.SetLocomotion(moving ? Mathf.Clamp01(speed / Mathf.Max(0.1f, runSpeed)) : 0f);

            if (moving)
            {
                var movementDelta = Mathf.Clamp(Time.deltaTime, 0f, 0.25f);
                var next = player.position + worldMove.normalized * speed * movementDelta;
                var origin = new Vector2(player.position.x, player.position.z);
                var desired = new Vector2(next.x, next.z);
                // Never apply a previous settlement's local collision layout after a server
                // handover. Until the matching layout arrives, the server alone validates movement.
                var layoutMatchesPlace = PoiPositionPolicy.IsLayoutForSettlement(
                    _layoutSettlementId, _frame?.Place?.Id);
                var activeLayout = layoutMatchesPlace ? _layout : null;
                var allowSeamlessExit = _worldManifest != null && _worldManifest.Seamless;
                var flat = activeLayout != null
                    ? activeLayout.ResolveMove(origin, desired, allowSeamlessExit)
                    : desired;
                // Preserve the last known elevation until a server height sample is available.
                // Resetting Y to zero every frame made the terrain appear to sink while moving.
                player.position = new Vector3(flat.x, player.position.y, flat.y);

                if (!_firstPerson)
                {
                    var facing = Mathf.Atan2(worldMove.z, worldMove.x) * Mathf.Rad2Deg;
                    var targetRotation = Quaternion.Euler(0f, EntityViewRegistry.FacingToUnityYaw(facing), 0f);
                    player.rotation = Quaternion.RotateTowards(player.rotation, targetRotation, 720f * Time.deltaTime);
                }
            }

            ApplyMovementCorrection(player);
            ApplyTerrainGrounding(player);

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

        private void ApplyTerrainGrounding(Transform player)
        {
            if (player == null) return;
            var anchor = _frame?.Place?.Anchor;
            if (anchor == null) return;

            // WorldPos U is absolute terrain elevation; Unity player Y is relative to the
            // active settlement anchor. Use the exact server heightfield already streamed
            // for this global chunk, and keep the previous Y when the sample is not cached.
            var east = anchor.East + player.position.x;
            var north = anchor.North + player.position.z;
            if (!TrySampleTerrainHeight(east, north, anchor.Frame, out var ground)) return;
            var y = ground - anchor.Up;
            if (double.IsNaN(y) || double.IsInfinity(y) || y < -10000d || y > 10000d) return;

            var position = player.position;
            position.y = (float)y;
            player.position = position;
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

        private void ReconcileMovement(CharacterFrame character)
        {
            var position = character != null ? character.Position : null;
            if (position == null || _views?.PlayerTransform == null) return;
            _serverStamina = Mathf.Clamp01((float)position.Stamina);
            Vector3 authoritativePosition;
            if (!WorldPositionProjection.TryToUnityPosition(
                character.WorldPosition, ResolveWorldAnchor(_frame), out authoritativePosition))
                authoritativePosition = EntityViewRegistry.ToUnityPosition(position);
            var currentPlaceId = _frame?.Place?.Id;
            var placeChanged = !string.Equals(_movementPlaceId, currentPlaceId, StringComparison.Ordinal);
            if (!_hasMovementBaseline || placeChanged)
            {
                // StepLands may change settlement-local coordinates without changing the
                // movement sequence. A place handover is therefore a new coordinate frame:
                // snap to the server's new local position and discard old-place prediction.
                _movementPlaceId = currentPlaceId;
                _moveSequence = Math.Max(_moveSequence, position.Sequence);
                _lastConfirmedMoveSequence = position.Sequence;
                _hasMovementBaseline = true;
                _pendingMoveIntents.Clear();
                _moveSendQueue.Clear();
                _movementCorrection = Vector3.zero;
                _movementCorrectionRemaining = 0f;
                _views.PlayerTransform.position = authoritativePosition;
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

            var replayedPosition = authoritativePosition;
            var replayedStamina = _serverStamina;
            var replayedFacing = position.Facing;
            var activeLayout = PoiPositionPolicy.IsLayoutForSettlement(
                _layoutSettlementId, _frame?.Place?.Id) ? _layout : null;
            var allowSeamlessExit = _worldManifest != null && _worldManifest.Seamless;
            for (var i = 0; i < _pendingMoveIntents.Count; i++)
            {
                var intent = _pendingMoveIntents[i];
                PredictServerMove(ref replayedPosition, ref replayedStamina, intent,
                    activeLayout, allowSeamlessExit);
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
                PredictServerMove(ref unusedPosition, ref stamina, _pendingMoveIntents[i], null, true);
            return stamina;
        }

        private void PredictServerMove(ref Vector3 position, ref float stamina,
            PendingMoveIntent intent, WorldLayoutFrame layout, bool allowOutsideSettlement)
        {
            var dt = Mathf.Clamp(intent.DeltaTime, 0f, 0.25f);
            var direction = new Vector2(intent.Mx, intent.My);
            var length = direction.magnitude;
            if (length > 0.000001f)
            {
                direction /= length;
                var canRun = intent.Run && stamina > 0f;
                var movement = _worldManifest != null ? _worldManifest.Movement : null;
                var walkSpeed = movement != null && movement.WalkMetersPerSecond > 0d
                    ? (float)movement.WalkMetersPerSecond : 1.4f;
                var jogSpeed = movement != null && movement.JogMetersPerSecond > 0d
                    ? (float)movement.JogMetersPerSecond : 3.2f;
                var runSpeed = movement != null && movement.RunMetersPerSecond > 0d
                    ? (float)movement.RunMetersPerSecond : 6.0f;
                var speed = canRun ? (intent.Fast ? runSpeed : jogSpeed) : walkSpeed;
                if (canRun) stamina = Mathf.Max(0f, stamina - 0.10f * dt);
                else stamina = Mathf.Min(1f, stamina + 0.10f * dt);

                speed *= ResolveMovementTerrainMultiplier(position, direction, movement);

                var origin = new Vector2(position.x, position.z);
                var desired = origin + direction * speed * dt;
                var flat = layout != null
                    ? layout.ResolveMove(origin, desired, allowOutsideSettlement)
                    : desired;
                position.x = flat.x;
                position.z = flat.y;
            }
            else
            {
                // Match server stamRegen; this remains prediction for queued intents only.
                stamina = Mathf.Min(1f, stamina + 0.10f * dt);
            }
        }

        private float ResolveMovementTerrainMultiplier(Vector3 localPosition, Vector2 direction,
            WorldMovementFrame movement)
        {
            var anchor = _frame?.Place?.Anchor;
            if (anchor == null || direction.sqrMagnitude < 0.000001f) return 1f;
            direction.Normalize();
            var multiplier = 1f;
            var slope = movement != null ? movement.Slope : null;
            var outsideSettlementGround = true;
            if (slope != null)
            {
                var flatBoundary = Math.Max(0d, slope.FlatRadiusMeters - slope.ProbeMeters);
                outsideSettlementGround = localPosition.x * localPosition.x + localPosition.z * localPosition.z >=
                    flatBoundary * flatBoundary;
                if (outsideSettlementGround)
                {
                    var probe = Math.Max(0.01d, slope.ProbeMeters);
                    var east = anchor.East + localPosition.x;
                    var north = anchor.North + localPosition.z;
                    var aheadEast = east + direction.x * probe;
                    var aheadNorth = north + direction.y * probe;
                    if (TrySampleTerrainHeight(east, north, anchor.Frame, out var ground) &&
                        TrySampleTerrainHeight(aheadEast, aheadNorth, anchor.Frame, out var ahead))
                    {
                        var grade = (ahead - ground) / probe;
                        multiplier *= (float)ServerSlopeMovementPolicy.Multiplier(grade,
                            slope.UphillFactor, slope.DownhillFactor, slope.EasyDownGrade,
                            slope.EasyDownBoost, slope.CliffGrade);
                    }
                    // If terrain samples are not cached yet, omit slope prediction rather than
                    // inventing a client-only height. The next authoritative snapshot corrects it.
                }
            }

            // v1.27: match sim.TrailAt using the server's width and reach values.
            // Only loaded server-authored terrain polylines can grant the prediction bonus.
            if (outsideSettlementGround && movement?.Trail != null)
            {
                var boost = ResolveTrailBoost(anchor.East + localPosition.x,
                    anchor.North + localPosition.z, anchor.Frame, movement.Trail);
                multiplier *= boost;
            }
            return multiplier;
        }

        private float ResolveTrailBoost(double east, double north, int frame, WorldTrailMovementFrame settings)
        {
            if (settings == null) return 1f;
            var reach = Math.Max(0d, settings.ReachMeters);
            var bestBoost = 1d;
            foreach (var chunk in _terrainChunks.Values)
            {
                if (chunk == null || chunk.Frame != frame || chunk.Origin == null || chunk.Roads == null)
                    continue;
                foreach (var path in chunk.Roads)
                {
                    if (path == null || path.Points == null || path.Points.Count < 2) continue;
                    var isRoad = string.Equals(path.Kind, "road", StringComparison.OrdinalIgnoreCase);
                    var width = path.WidthMeters > 0d ? path.WidthMeters :
                        (isRoad ? settings.RoadWidthMeters : settings.TrailWidthMeters);
                    if (!IsFiniteMovementValue(width) || width <= 0d) continue;
                    var threshold = width * 0.5d + reach;
                    for (var i = 1; i < path.Points.Count; i++)
                    {
                        var a = path.Points[i - 1];
                        var b = path.Points[i];
                        if (a == null || b == null) continue;
                        var ax = chunk.Origin.East + a.X;
                        var ay = chunk.Origin.North + a.Z;
                        var bx = chunk.Origin.East + b.X;
                        var by = chunk.Origin.North + b.Z;
                        if (DistanceToSegment(east, north, ax, ay, bx, by) > threshold) continue;
                        var boost = isRoad ? settings.RoadBoost : settings.TrailBoost;
                        if (IsFiniteMovementValue(boost) && boost > bestBoost) bestBoost = boost;
                        break;
                    }
                }
            }
            return (float)bestBoost;
        }

        private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.0000001d) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            var t = Math.Max(0d, Math.Min(1d, ((px - ax) * dx + (py - ay) * dy) / lengthSquared));
            var x = ax + dx * t;
            var y = ay + dy * t;
            return Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
        }

        private static bool IsFiniteMovementValue(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private bool TrySampleTerrainHeight(double east, double north, int frame, out double height)
        {
            height = 0d;
            foreach (var pair in _terrainChunks)
            {
                var chunk = pair.Value;
                if (chunk == null || chunk.Frame != frame || chunk.Origin == null ||
                    chunk.StepMeters <= 0 || chunk.Side < 2 || chunk.Heights == null ||
                    chunk.Heights.Count != chunk.Side * chunk.Side)
                    continue;

                var gridX = (east - chunk.Origin.East) / chunk.StepMeters;
                var gridZ = (north - chunk.Origin.North) / chunk.StepMeters;
                if (gridX < -0.000001d || gridZ < -0.000001d ||
                    gridX > chunk.Side - 1 + 0.000001d || gridZ > chunk.Side - 1 + 0.000001d)
                    continue;

                var x0 = Math.Min(chunk.Side - 2, Math.Max(0, (int)Math.Floor(gridX)));
                var z0 = Math.Min(chunk.Side - 2, Math.Max(0, (int)Math.Floor(gridZ)));
                var tx = Math.Max(0d, Math.Min(1d, gridX - x0));
                var tz = Math.Max(0d, Math.Min(1d, gridZ - z0));
                var h00 = chunk.Heights[z0 * chunk.Side + x0];
                var h10 = chunk.Heights[z0 * chunk.Side + x0 + 1];
                var h01 = chunk.Heights[(z0 + 1) * chunk.Side + x0];
                var h11 = chunk.Heights[(z0 + 1) * chunk.Side + x0 + 1];
                if (double.IsNaN(h00) || double.IsInfinity(h00) ||
                    double.IsNaN(h10) || double.IsInfinity(h10) ||
                    double.IsNaN(h01) || double.IsInfinity(h01) ||
                    double.IsNaN(h11) || double.IsInfinity(h11))
                    return false;

                var lower = h00 + (h10 - h00) * tx;
                var upper = h01 + (h11 - h01) * tx;
                height = lower + (upper - lower) * tz;
                return true;
            }
            return false;
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

        private async Task SendCharacterPriorityAsync(string goal)
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
                        _status = "Запрос долгосрочного приоритета отправлен: " + goal + ". Серверный протокол v1 пока не возвращает отдельное подтверждение для op:goal.";
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
            _layoutRequestGeneration++;
            _layoutRequestedFor = null;
            _voiceChat?.StopVoice();
            var socket = _socket;
            _socket = null;
            _connectionState = WorldConnectionState.Closed;
            _frame = null;
            _worldManifestGeneration++;
            _worldManifestRefreshInFlight = false;
            _layoutPrefetchInFlight.Clear();
            _layoutPrefetchRetryAfter.Clear();
            _worldManifest = null;
            CauldronRimPresentationBuilder.Clear(_presentationRoot);
            _cameraRig?.SetWorldHorizonDistance(0f);
            _nextWorldManifestRefreshAt = 0f;
            _nextSettlementPrefetchAt = 0f;
            _worldManifestLastPlaceId = null;
            _priorityToSend = null;
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
            // Keep the configured first-person default across reconnects.
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
        /// Streams a bounded 3x3 terrain window around the authoritative player position. Server coordinates
        /// stay double precision; only final chunk mesh vertices are rebased to the settlement anchor.
        /// Seamless server travel remains disabled until neighboring settlement presentation and collision are complete.
        /// </summary>
        private void EnsureTerrainChunkForFrame(WorldFrame frame)
        {
            if (frame == null || frame.Place == null) return;

            var placeId = frame.Place.Id;
            if (!string.Equals(_terrainStreamSettlementId, placeId, StringComparison.Ordinal))
            {
                // Terrain chunks are identified by canonical frame/chunk coordinates, not by
                // settlement. Keep the overlap cache and in-flight global requests alive when
                // Place.Id changes; the anchor rebase below moves every cached chunk into the
                // new local presentation origin without changing its world-space location.
                _terrainStreamSettlementId = placeId;
                _terrainChunkRetryAfter.Clear();
            }

            if (_worldManifest == null || frame.Me == null || frame.Place.Anchor == null ||
                frame.Me.WorldPosition == null || !frame.Me.WorldPosition.IsValid)
                return;

            var anchor = frame.Place.Anchor;
            var position = frame.Me.WorldPosition;
            if (anchor.Frame != position.Frame || position.Frame != 0) return;

            // Optional distant mountain LOD is built only from the server's rim profile.
            // It has no collider; the server must enforce the cauldron boundary.
            CauldronRimPresentationBuilder.BuildOrUpdate(_presentationRoot, _worldManifest.Rim, anchor);
            _cameraRig?.SetWorldHorizonDistance(
                CauldronRimPresentationBuilder.RequiredFarClip(_worldManifest.Rim));
            var centerX = position.Chunk[0];
            var centerZ = position.Chunk[1];
            var centerChanged = !_hasTerrainCenter || position.Frame != _terrainCenterFrame ||
                centerX != _terrainCenterX || centerZ != _terrainCenterZ;
            if (centerChanged)
            {
                _hasTerrainCenter = true;
                _terrainCenterFrame = position.Frame;
                _terrainCenterX = centerX;
                _terrainCenterZ = centerZ;
                // A moved window gets a fresh retry budget; old chunk failures must not accumulate.
                _terrainChunkRetryAfter.Clear();
                var stale = new List<KeyValuePair<string, TerrainChunkFrame>>();
                foreach (var pair in _terrainChunks)
                {
                    var candidate = new WorldChunkCoordinate(pair.Value.Frame,
                        pair.Value.Chunk[0], pair.Value.Chunk[1]);
                    if (!WorldChunkStreamingPolicy.IsWithinRetention(
                        new WorldChunkCoordinate(position.Frame, centerX, centerZ),
                        candidate, TerrainChunkRetentionRadius))
                        stale.Add(pair);
                }
                foreach (var stalePair in stale)
                {
                    var id = stalePair.Key;
                    var staleChunk = stalePair.Value;
                    _terrainChunks.Remove(id);
                    if (_spaceChunks.TryGetValue(id, out var staleSpaces))
                    {
                        WorldSpacePresentationBuilder.Remove(_presentationRoot, staleSpaces);
                        _spaceChunks.Remove(id);
                    }
                    WorldTerrainChunkBuilder.Remove(_presentationRoot, id);
                    RebuildTerrainChunkAndNeighbors(staleChunk, anchor);
                    WorldFrontierBiomeBuilder.Remove(_presentationRoot, id);
                }
                CancelTerrainChunkRequestsOutsideRetention(position.Frame, centerX, centerZ);
            }

            var changedAnchor = _terrainAppliedSettlementId != frame.Place.Id ||
                Math.Abs(_terrainAppliedAnchorEast - anchor.East) > 0.001 ||
                Math.Abs(_terrainAppliedAnchorNorth - anchor.North) > 0.001 ||
                Math.Abs(_terrainAppliedAnchorUp - anchor.Up) > 0.001;
            if (changedAnchor)
            {
                foreach (var pair in _terrainChunks)
                {
                    WorldTerrainChunkBuilder.Apply(_presentationRoot, pair.Value, anchor, _terrainChunks.Values);
                    WorldFrontierBiomeBuilder.Apply(_presentationRoot, pair.Value, anchor);
                }
                WorldSpacePresentationBuilder.Clear(_presentationRoot);
                foreach (var pair in _spaceChunks)
                {
                    try
                    {
                        WorldSpacePresentationBuilder.Apply(_presentationRoot, pair.Value, anchor);
                    }
                    catch (Exception spaceError)
                    {
                        Debug.LogWarning("Не удалось перестроить серверное пространство " + pair.Key +
                            ": " + spaceError.Message);
                    }
                }
                _terrainAppliedSettlementId = frame.Place.Id;
                _terrainAppliedAnchorEast = anchor.East;
                _terrainAppliedAnchorNorth = anchor.North;
                _terrainAppliedAnchorUp = anchor.Up;
            }

            // Keep the request budget bounded even if the player crosses several chunks while
            // older HTTP requests are still in flight. Request the center first, then its neighbors.
            var centerChunk = new WorldChunkCoordinate(position.Frame, centerX, centerZ);
            var requestPlan = WorldChunkStreamingPolicy.BuildRequestPlan(
                centerChunk, TerrainChunkRequestRadius);
            for (var i = 0; i < requestPlan.Count; i++)
            {
                var chunk = requestPlan[i];
                TryRequestTerrainChunk(chunk.X, chunk.Z, chunk.Frame);
            }
        }

        private void RebuildTerrainChunkAndNeighbors(TerrainChunkFrame changed, WorldAnchorFrame anchor)
        {
            if (changed == null || changed.Chunk == null || changed.Chunk.Length != 2 ||
                anchor == null || _presentationRoot == null) return;

            foreach (var candidate in _terrainChunks.Values)
            {
                if (candidate == null || candidate.Chunk == null || candidate.Chunk.Length != 2 ||
                    candidate.Frame != changed.Frame) continue;
                var dx = Math.Abs(candidate.Chunk[0] - changed.Chunk[0]);
                var dz = Math.Abs(candidate.Chunk[1] - changed.Chunk[1]);
                if (dx + dz > 1) continue;
                WorldTerrainChunkBuilder.Apply(_presentationRoot, candidate, anchor, _terrainChunks.Values);
            }
        }

        private void TryRequestTerrainChunk(long chunkX, long chunkZ, int frame)
        {
            if (_terrainChunkRequests.Count >= MaxConcurrentTerrainChunkRequests) return;
            var chunkId = frame + ":" + chunkX + ":" + chunkZ;
            if (_terrainChunks.ContainsKey(chunkId) || _terrainChunkRequests.Contains(chunkId)) return;
            if (_terrainChunkRetryAfter.TryGetValue(chunkId, out var retryAfter) && Time.unscaledTime < retryAfter) return;
            _terrainChunkRetryAfter.Remove(chunkId);
            if (!_terrainChunkRequests.Add(chunkId)) return;
            var cancellation = new CancellationTokenSource();
            _terrainChunkCancellation[chunkId] = cancellation;
            _ = LoadTerrainChunkAsync(chunkX, chunkZ, frame, chunkId, _terrainStreamGeneration, cancellation);
        }

        private void CancelAllTerrainChunkRequests()
        {
            foreach (var cancellation in _terrainChunkCancellation.Values)
                cancellation.Cancel();
            _terrainChunkCancellation.Clear();
        }

        private void CancelTerrainChunkRequestsOutsideRetention(int frame, long centerX, long centerZ)
        {
            var stale = new List<string>();
            var center = new WorldChunkCoordinate(frame, centerX, centerZ);
            foreach (var pair in _terrainChunkCancellation)
            {
                var parts = pair.Key.Split(':');
                if (parts.Length != 3 ||
                    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestFrame) ||
                    !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                    !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) ||
                    !WorldChunkStreamingPolicy.IsWithinRequest(center,
                        new WorldChunkCoordinate(requestFrame, x, z), TerrainChunkRequestRadius))
                    stale.Add(pair.Key);
            }

            foreach (var id in stale)
            {
                if (_terrainChunkCancellation.TryGetValue(id, out var cancellation))
                {
                    _terrainChunkCancellation.Remove(id);
                    _terrainChunkRequests.Remove(id);
                    cancellation.Cancel();
                }
            }
        }

        private async Task LoadTerrainChunkAsync(long chunkX, long chunkZ, int expectedFrame, string chunkId,
            int generation, CancellationTokenSource cancellation)
        {
            try
            {
                var http = new HttpAuthClient(serverUrl);
                var chunk = await http.GetTerrainChunkAsync(chunkX, chunkZ, 8, cancellation.Token);
                if (chunk.Frame != expectedFrame || chunk.Id != chunkId || !chunk.IsValid)
                    throw new InvalidOperationException("Сервер вернул чанк не того кадра, индекса или начала координат.");
                WorldSpaceChunkFrame spaces = null;
                try
                {
                    spaces = await http.GetWorldSpacesAsync(chunkX, chunkZ, cancellation.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception spaceError)
                {
                    // Terrain and ordinary settlement play remain available if optional space
                    // streaming is temporarily unavailable; the next terrain refresh can retry.
                    Enqueue(() => Debug.LogWarning("Многоуровневые пространства пока недоступны для чанка " +
                        chunkId + ": " + spaceError.Message));
                }
                Enqueue(() =>
                {
                    if (generation != _terrainStreamGeneration) return;
                    if (!_hasTerrainCenter || expectedFrame != _terrainCenterFrame ||
                        !WorldChunkStreamingPolicy.IsWithinRetention(
                            new WorldChunkCoordinate(_terrainCenterFrame, _terrainCenterX, _terrainCenterZ),
                            new WorldChunkCoordinate(expectedFrame, chunkX, chunkZ), TerrainChunkRetentionRadius) ||
                        _frame == null || _frame.Place == null || _frame.Place.Anchor == null)
                        return;
                    _terrainChunkRetryAfter.Remove(chunkId);
                    _terrainChunks[chunkId] = chunk;
                    RebuildTerrainChunkAndNeighbors(chunk, _frame.Place.Anchor);
                    if (spaces != null)
                    {
                        try
                        {
                            _spaceChunks[chunkId] = spaces;
                            WorldSpacePresentationBuilder.Apply(_presentationRoot, spaces, _frame.Place.Anchor);
                        }
                        catch (Exception spaceError)
                        {
                            _spaceChunks.Remove(chunkId);
                            Debug.LogWarning("Не удалось отобразить серверные пространства чанка " +
                                chunkId + ": " + spaceError.Message);
                        }
                    }
                    else
                    {
                        if (_spaceChunks.TryGetValue(chunkId, out var previousSpaces))
                            WorldSpacePresentationBuilder.Remove(_presentationRoot, previousSpaces);
                        _spaceChunks.Remove(chunkId);
                    }
                    WorldFrontierBiomeBuilder.Apply(_presentationRoot, chunk, _frame.Place.Anchor);
                    _terrainAppliedSettlementId = _frame.Place.Id;
                    _terrainAppliedAnchorEast = _frame.Place.Anchor.East;
                    _terrainAppliedAnchorNorth = _frame.Place.Anchor.North;
                    _terrainAppliedAnchorUp = _frame.Place.Anchor.Up;
                });
            }
            catch (OperationCanceledException)
            {
                // A stale chunk no longer blocks the three request slots after the player moves away.
            }
            catch (Exception exception)
            {
                Enqueue(() =>
                {
                    if (generation != _terrainStreamGeneration) return;
                    if (_hasTerrainCenter && expectedFrame == _terrainCenterFrame &&
                        WorldChunkStreamingPolicy.IsWithinRetention(
                            new WorldChunkCoordinate(_terrainCenterFrame, _terrainCenterX, _terrainCenterZ),
                            new WorldChunkCoordinate(expectedFrame, chunkX, chunkZ), TerrainChunkRetentionRadius))
                    {
                        _terrainChunkRetryAfter[chunkId] = Time.unscaledTime + TerrainChunkRetryDelaySeconds;
                        Debug.LogWarning("Не удалось загрузить чанк рельефа " + chunkId + "; повтор через " +
                            TerrainChunkRetryDelaySeconds.ToString("0") + " с: " + exception.Message);
                    }
                });
            }
            finally
            {
                Enqueue(() =>
                {
                    if (_terrainChunkCancellation.TryGetValue(chunkId, out var active) &&
                        ReferenceEquals(active, cancellation))
                    {
                        _terrainChunkCancellation.Remove(chunkId);
                        _terrainChunkRequests.Remove(chunkId);
                    }
                });
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// Refreshes the server world manifest while playing. Frontier settlements are created
        /// server-side when an outer settlement is reached, so a one-time login manifest is stale.
        /// The refresh is metadata-only and never resets terrain, entities or the current layout.
        /// </summary>
        private void UpdateWorldStreaming()
        {
            if (_connectionState != WorldConnectionState.Active || _frame?.Me?.WorldPosition == null ||
                !_frame.Me.WorldPosition.IsValid || _frame.Place?.Anchor == null)
                return;

            var currentPlaceId = _frame.Place.Id;
            if (!string.Equals(currentPlaceId, _worldManifestLastPlaceId, StringComparison.Ordinal))
            {
                _worldManifestLastPlaceId = currentPlaceId;
                _nextWorldManifestRefreshAt = 0f; // A place handover may have opened a new frontier ring.
                _nextSettlementPrefetchAt = 0f;
            }

            if (!_worldManifestRefreshInFlight && Time.unscaledTime >= _nextWorldManifestRefreshAt)
            {
                _worldManifestRefreshInFlight = true;
                _ = RefreshWorldManifestAsync(_worldManifestGeneration);
            }

            if (_worldManifest != null && Time.unscaledTime >= _nextSettlementPrefetchAt)
            {
                _nextSettlementPrefetchAt = Time.unscaledTime + 2f;
                PrefetchNearbySettlementLayouts(_frame, _worldManifest);
            }
        }

        private async Task RefreshWorldManifestAsync(int generation)
        {
            WorldManifestFrame manifest = null;
            Exception failure = null;
            try { manifest = await new HttpAuthClient(serverUrl).GetWorldManifestAsync(); }
            catch (Exception exception) { failure = exception; }

            Enqueue(() =>
            {
                if (generation != _worldManifestGeneration) return;
                _worldManifestRefreshInFlight = false;
                _nextWorldManifestRefreshAt = Time.unscaledTime +
                    (failure == null ? WorldManifestRefreshSeconds : 10f);
                if (_connectionState != WorldConnectionState.Active) return;
                if (manifest == null)
                {
                    if (failure != null)
                        Debug.LogWarning("Не удалось обновить карту мира; используем последнюю версию: " + failure.Message);
                    return;
                }

                _worldManifest = manifest;
                _worldMapCachedManifest = null;
                _layoutCache?.Update(_frame);
                // The next UpdateWorldStreaming pass sees newly opened frontier settlements
                // and schedules their nearby layouts without a scene reload.
            });
        }

        private void PrefetchNearbySettlementLayouts(WorldFrame frame, WorldManifestFrame manifest)
        {
            var player = frame?.Me?.WorldPosition;
            if (player == null || manifest?.Settlements == null || _layoutCache == null) return;
            var candidates = new List<WorldSettlementFrame>();
            var radiusSquared = SettlementLayoutPrefetchRadiusMeters * SettlementLayoutPrefetchRadiusMeters;
            foreach (var settlement in manifest.Settlements)
            {
                if (settlement == null || settlement.Frame != player.Frame ||
                    string.IsNullOrWhiteSpace(settlement.Id) ||
                    string.Equals(settlement.Id, frame.Place?.Id, StringComparison.Ordinal))
                    continue;

                var dx = settlement.East - player.East;
                var dz = settlement.North - player.North;
                if (dx * dx + dz * dz > radiusSquared) continue;
                if (_layoutCache.Contains(settlement.Id)) continue;
                if (_layoutPrefetchInFlight.Contains(settlement.Id)) continue;
                if (_layoutPrefetchRetryAfter.TryGetValue(settlement.Id, out var retryAt) &&
                    Time.unscaledTime < retryAt) continue;
                candidates.Add(settlement);
            }

            candidates.Sort((a, b) =>
            {
                var adx = a.East - player.East;
                var adz = a.North - player.North;
                var bdx = b.East - player.East;
                var bdz = b.North - player.North;
                return (adx * adx + adz * adz).CompareTo(bdx * bdx + bdz * bdz);
            });

            var scheduled = 0;
            for (var i = 0; i < candidates.Count &&
                _layoutPrefetchInFlight.Count < MaxConcurrentLayoutPrefetches &&
                scheduled < MaxNearbyLayoutsToPrefetch; i++)
            {
                var settlement = candidates[i];
                if (!_layoutPrefetchInFlight.Add(settlement.Id)) continue;
                scheduled++;
                _ = LoadNearbySettlementLayoutAsync(settlement, _worldManifestGeneration);
            }
        }

        private async Task LoadNearbySettlementLayoutAsync(WorldSettlementFrame settlement, int generation)
        {
            WorldLayoutFrame layout = null;
            Exception failure = null;
            try { layout = await new HttpAuthClient(serverUrl).GetLayoutAsync(settlement.Id, _lifetime.Token); }
            catch (Exception exception) { failure = exception; }

            Enqueue(() =>
            {
                _layoutPrefetchInFlight.Remove(settlement.Id);
                if (generation != _worldManifestGeneration ||
                    _connectionState != WorldConnectionState.Active || _frame == null ||
                    _frame.Place?.Anchor == null || _layoutCache == null)
                    return;

                if (layout == null)
                {
                    _layoutPrefetchRetryAfter[settlement.Id] = Time.unscaledTime + 30f;
                    if (failure != null)
                        Debug.LogWarning("Не удалось заранее подгрузить поселение " + settlement.Id + ": " + failure.Message);
                    return;
                }

                var expectedFrame = settlement.Frame;
                var expectedAnchor = layout.Anchor;
                if (expectedAnchor == null || expectedAnchor.Frame != expectedFrame ||
                    Math.Abs(expectedAnchor.East - settlement.East) > 0.002d ||
                    Math.Abs(expectedAnchor.North - settlement.North) > 0.002d ||
                    Math.Abs(expectedAnchor.Up - settlement.Up) > 0.002d ||
                    (!string.IsNullOrEmpty(layout.Settlement) &&
                     !string.Equals(layout.Settlement, settlement.Id, StringComparison.Ordinal)))
                {
                    _layoutPrefetchRetryAfter[settlement.Id] = Time.unscaledTime + 30f;
                    Debug.LogWarning("Пропущена раскладка поселения " + settlement.Id +
                        ": якорь не совпадает с обновлённой картой мира.");
                    return;
                }

                if (!_layoutCache.Store(settlement.Id, layout, _frame))
                    _layoutPrefetchRetryAfter[settlement.Id] = Time.unscaledTime + 30f;
                else
                    _layoutPrefetchRetryAfter.Remove(settlement.Id);
            });
        }

        private void EnsureLayoutForPlace(WorldFrame frame)
        {
            var placeId = frame != null && frame.Place != null ? frame.Place.Id : null;
            if (string.IsNullOrEmpty(placeId)) return;

            // The default /api/layout document is the tutorial settlement's server geometry.
            // Keep its collision, POI and map data active; generated settlements use cached per-place roots.
            var isTutorialSettlement = SettlementLayoutSelectionPolicy.IsTutorialSettlement(
                frame.Place.Plan, placeId, _prototypeSettlementId, _layoutSettlementId != null);
            if (isTutorialSettlement)
            {
                if (string.IsNullOrEmpty(_prototypeSettlementId) &&
                    SettlementLayoutSelectionPolicy.IsTutorialLayout(frame.Place.Plan))
                    _prototypeSettlementId = placeId;
                if (_layoutRequestedFor != null)
                {
                    _layoutRequestGeneration++;
                    _layoutRequestedFor = null;
                }
                _layoutSettlementId = placeId;
                _layout = SettlementLayoutSelectionPolicy.SelectForSettlement(
                    isTutorialSettlement, _starterLayout, _layout);
                _layoutCache?.Update(frame);
                WorldLayoutPresentationBuilder.SetVisible(_presentationRoot, true);
                return;
            }

            if (_layoutCache != null && _layoutCache.TryGet(placeId, frame, out var cachedLayout))
            {
                _layout = cachedLayout;
                _layoutSettlementId = placeId;
                if (_layoutRequestedFor != null)
                {
                    _layoutRequestGeneration++;
                    _layoutRequestedFor = null;
                }
                WorldLayoutPresentationBuilder.SetVisible(_presentationRoot, false);
                _layoutFailedFor = null;
                _status = "Поселение из кэша: " +
                    (!string.IsNullOrEmpty(frame.Place.Name) ? frame.Place.Name : placeId);
                return;
            }

            _layoutCache?.Update(frame); // Keep nearby cached settlements at their true global offsets.
            if (placeId == _layoutRequestedFor) return;
            _status = "Подгружается соседнее поселение… прежняя геометрия сохранена.";
            if (_layoutFailedFor == placeId && Time.unscaledTime < _layoutRetryAfter) return;
            _layoutRequestedFor = placeId;
            _ = ReloadLayoutAsync(placeId, _layoutRequestGeneration);
        }

        private async Task ReloadLayoutAsync(string placeId, int generation)
        {
            try
            {
                var layout = await new HttpAuthClient(serverUrl).GetLayoutAsync(placeId);
                Enqueue(() =>
                {
                    if (generation != _layoutRequestGeneration || _layoutRequestedFor != placeId ||
                        _frame == null || _frame.Place == null || _frame.Place.Id != placeId) return;
                    var expectedAnchor = _frame.Place.Anchor;
                    var anchorMismatch = expectedAnchor != null &&
                        (layout.Anchor == null || layout.Anchor.Frame != expectedAnchor.Frame ||
                         Math.Abs(layout.Anchor.East - expectedAnchor.East) > 0.002d ||
                         Math.Abs(layout.Anchor.North - expectedAnchor.North) > 0.002d ||
                         Math.Abs(layout.Anchor.Up - expectedAnchor.Up) > 0.002d);
                    var settlementMismatch = !string.IsNullOrEmpty(layout.Settlement) &&
                        !string.Equals(layout.Settlement, placeId, StringComparison.Ordinal);
                    if (anchorMismatch || settlementMismatch)
                    {
                        _layoutRequestedFor = null;
                        _layoutFailedFor = placeId;
                        _layoutRetryAfter = Time.unscaledTime + 10f;
                        _status = "Раскладка не совпала с поселением или якорем сервера. Повтор через 10 секунд.";
                        Debug.LogWarning("Раскладка поселения " + placeId +
                            " не совпадает с идентификатором/якорем полного кадра; старая геометрия не будет заменена.");
                        return;
                    }

                    if (_layoutCache == null || !_layoutCache.Store(placeId, layout, _frame))
                    {
                        _layoutRequestedFor = null;
                        _layoutFailedFor = placeId;
                        _layoutRetryAfter = Time.unscaledTime + 10f;
                        _status = "Не удалось собрать геометрию поселения; прежняя геометрия сохранена.";
                        return;
                    }
                    _layout = layout;
                    _layoutSettlementId = placeId;
                    _layoutCache.Update(_frame);
                    WorldLayoutPresentationBuilder.SetVisible(_presentationRoot, false);
                    _layoutRequestedFor = null;
                    _layoutFailedFor = null;
                    _layoutRetryAfter = 0f;
                    _status = "Поселение загружено: " + (_frame != null && _frame.Place != null && !string.IsNullOrEmpty(_frame.Place.Name) ? _frame.Place.Name : placeId);
                });
            }
            catch (Exception exception)
            {
                Enqueue(() =>
                {
                    if (generation != _layoutRequestGeneration || _layoutRequestedFor != placeId) return;
                    _layoutRequestedFor = null;
                    _layoutFailedFor = placeId;
                    _layoutRetryAfter = Time.unscaledTime + 10f;
                    _status = "Не удалось загрузить поселение; повтор через 10 секунд. " + exception.Message;
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
            var listeners = FindObjectsByType<AudioListener>();
            for (var i = 0; i < listeners.Length; i++)
                if (listeners[i] != null && listeners[i] != listener)
                    listeners[i].enabled = false;

            camera.transform.position = new Vector3(0, 22, -28);
            camera.transform.rotation = Quaternion.Euler(35, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.58f, 0.70f, 0.78f);
            camera.fieldOfView = 48f;
            var urpCamera = camera.GetComponent<UniversalAdditionalCameraData>();
            if (urpCamera == null) urpCamera = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urpCamera.renderPostProcessing = true;
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
            _interactionProbe.BindInput(_input, _core);
            _cameraRig.SetDefaultFieldOfView(camera.fieldOfView);
            _cameraRig.SetFirstPerson(_firstPerson);

            // WorldEnvironmentBuilder exclusively owns sun, ambient, sky and fog.
            // Keeping scene lighting in one owner makes its RenderSettings snapshot reversible.

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
            WorldWildSitePresentationBuilder.Clear();
            _layoutCache?.Dispose();
            _layoutCache = null;
            WorldLayoutPresentationBuilder.Cleanup();
            ProceduralVillageBuilder.Cleanup();
        }
    }
}
