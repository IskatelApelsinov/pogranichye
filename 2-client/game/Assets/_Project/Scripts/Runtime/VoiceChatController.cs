using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Pogranichye.Client.Networking;
using SimpleWebRTC;
using UnityEngine;
using Unity.WebRTC;

namespace Pogranichye.Client
{
    /// <summary>
    /// Two-person proximity voice for the demo. Joining voice never opens the microphone;
    /// microphone capture starts only after the player explicitly enables it.
    /// </summary>
    public sealed class VoiceChatController : MonoBehaviour
    {
        private const string Room = "pogranichye-nearby";
        private static bool _webRtcInitialized;
        private WorldSocketClient _socket;
        private CancellationToken _cancellationToken;
        private string _entityId;
        private GameObject _voiceRoot;
        private WebRTCPeerManager _manager;
        private PogranichyeVoiceTransport _transport;
        private AudioSender _sender;
        private bool _joined;
        private bool _microphoneEnabled;
        private bool _pushToTalkHeld;
        private bool _streaming;

        public bool IsJoined => _joined;
        public bool IsMicrophoneEnabled => _microphoneEnabled;
        public string Status { get; private set; } = "Голос отключён.";

        public void Bind(WorldSocketClient socket, string entityId, CancellationToken cancellationToken)
        {
            if (_socket == socket && _entityId == entityId) return;
            StopVoice();
            _socket = socket;
            _entityId = entityId;
            _cancellationToken = cancellationToken;
        }

        public void ToggleVoice()
        {
            if (_joined) StopVoice();
            else StartVoice();
        }

        public void ToggleMicrophone()
        {
            if (!_joined || _sender == null)
            {
                Status = "Сначала войдите в голосовой канал.";
                return;
            }
            if (_microphoneEnabled)
            {
                _microphoneEnabled = false;
                UpdateMicrophoneStreamState();
                Status = _pushToTalkHeld
                    ? "Открытый микрофон выключен; P — говорить по нажатию."
                    : "Микрофон выключен. Вы слышите собеседников; удерживайте P, чтобы говорить.";
                return;
            }
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                Status = "Разрешите доступ к микрофону и нажмите кнопку ещё раз.";
                return;
            }
#endif
#if UNITY_IOS
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                StartCoroutine(RequestIOSMicrophonePermission());
                return;
            }
#endif
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Status = "Микрофон не найден или запрещён системой.";
                return;
            }
            _microphoneEnabled = true;
            UpdateMicrophoneStreamState();
            Status = "Открытый микрофон включён. Нажмите ещё раз, чтобы заглушить; P — режим нажатия.";
        }

        /// <summary>Push-to-talk while joined; the persistent mic toggle remains an optional open-mic mode.</summary>
        public void SetPushToTalkHeld(bool held)
        {
            if (_pushToTalkHeld == held) return;
            _pushToTalkHeld = held;
            if (!_joined || _sender == null || _microphoneEnabled) return;
            if (held && !HasMicrophonePermission())
            {
                Status = "Для PTT сначала нажмите «Включить микрофон», чтобы выдать разрешение.";
                return;
            }
            UpdateMicrophoneStreamState();
            if (!_microphoneEnabled)
                Status = held ? "Говорите — PTT активен." : "Микрофон заглушён. Удерживайте P, чтобы говорить.";
        }

        private static bool HasMicrophonePermission()
        {
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                return false;
#endif
#if UNITY_IOS
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone)) return false;
#endif
            return Microphone.devices != null && Microphone.devices.Length > 0;
        }

        private void UpdateMicrophoneStreamState()
        {
            var shouldStream = _joined && _sender != null && (_microphoneEnabled || _pushToTalkHeld);
            if (shouldStream == _streaming) return;
            if (shouldStream)
            {
                _sender.StartStreaming();
                _streaming = true;
            }
            else
            {
                _sender.StopStreaming();
                _streaming = false;
            }
        }

        private IEnumerator RequestIOSMicrophonePermission()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            Status = Application.HasUserAuthorization(UserAuthorization.Microphone)
                ? "Разрешение получено. Нажмите включение микрофона ещё раз."
                : "Доступ к микрофону запрещён.";
        }

        private void StartVoice()
        {
            if (_socket == null || !_socket.IsConnected || string.IsNullOrWhiteSpace(_entityId))
            {
                Status = "Сначала подключитесь к миру.";
                return;
            }

            try
            {
                EnsureWebRTCInitialized();
                _voiceRoot = new GameObject("[Pogranichye] Voice");
                _transport = _voiceRoot.AddComponent<PogranichyeVoiceTransport>();
                _transport.Bind(_socket, _entityId, _cancellationToken);

                _manager = _voiceRoot.AddComponent<WebRTCPeerManager>();
                var randomSuffix = typeof(WebRTCPeerManager).GetField("appendRandomSuffix",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (randomSuffix != null) randomSuffix.SetValue(_manager, false);
                _manager.SetLocalPeerId(_entityId);
                _manager.SetRoom(Room);
                _manager.SetSignaling(_transport);

                var microphoneObject = new GameObject("Microphone input");
                microphoneObject.transform.SetParent(_voiceRoot.transform, false);
                var microphoneSource = microphoneObject.AddComponent<AudioSource>();
                microphoneSource.playOnAwake = false;
                microphoneSource.loop = true;

                _sender = _voiceRoot.AddComponent<AudioSender>();
                _sender.AudioSource = microphoneSource;
                _sender.UseMicrophone = true;
                var playLocally = typeof(AudioSender).GetField("playLocally",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (playLocally != null) playLocally.SetValue(_sender, false);

                _voiceRoot.AddComponent<AudioReceiver>();
                _manager.SignalingStateChanged.AddListener(OnSignalingStateChanged);
                _manager.PeerConnected.AddListener(OnPeerConnected);
                _manager.PeerLeft.AddListener(OnPeerLeft);
                _manager.Connect();
                _joined = true;
                _microphoneEnabled = false;
                Status = "Вы в голосовом канале поблизости. Микрофон выключен.";
            }
            catch (Exception ex)
            {
                Status = "Не удалось запустить голос: " + ex.Message;
                StopVoice();
            }
        }

        private static void EnsureWebRTCInitialized()
        {
            if (_webRtcInitialized) return;
            // FDGS keeps the native bootstrap internal. Initialize it before the peer manager
            // creates RTCPeerConnection; WebRTCPeerManager owns the per-frame Update runner.
            var webRtcType = typeof(RTCPeerConnection).Assembly.GetType("Unity.WebRTC.WebRTC");
            var initialize = webRtcType?.GetMethod("InitializeInternal",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (initialize == null)
                throw new InvalidOperationException("WebRTC package has no InitializeInternal bootstrap.");
            try
            {
                initialize.Invoke(null, new object[] { true, false, NativeLoggingSeverity.Info });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            {
                // Another subsystem already initialized the shared native context.
            }
            _webRtcInitialized = true;
            Application.quitting -= DisposeWebRTC;
            Application.quitting += DisposeWebRTC;
        }

        private static void DisposeWebRTC()
        {
            if (!_webRtcInitialized) return;
            var webRtcType = typeof(RTCPeerConnection).Assembly.GetType("Unity.WebRTC.WebRTC");
            var dispose = webRtcType?.GetMethod("DisposeInternal", BindingFlags.Static | BindingFlags.NonPublic);
            try { dispose?.Invoke(null, null); }
            catch (Exception ex) { Debug.LogWarning("WebRTC shutdown: " + ex.Message); }
            _webRtcInitialized = false;
        }

        private void OnSignalingStateChanged(SignalingState state)
        {
            if (state == SignalingState.Disconnected && _joined)
                Status = "Голосовая связь потеряна. Попробуйте переподключиться.";
        }

        private void OnPeerConnected(string peerId)
        {
            Status = "Голосовая связь установлена.";
        }

        private void OnPeerLeft(string peerId)
        {
            Status = "Собеседник отключился от голосового канала.";
        }

        public void StopVoice()
        {
            _microphoneEnabled = false;
            _pushToTalkHeld = false;
            _streaming = false;
            _joined = false;
            try { _sender?.StopStreaming(); } catch { }
            try { _manager?.Disconnect(); } catch { }
            if (_voiceRoot != null) Destroy(_voiceRoot);
            _voiceRoot = null;
            _manager = null;
            _transport = null;
            _sender = null;
            Status = "Голос отключён.";
        }

        private void OnDisable() => StopVoice();
        private void OnDestroy() => StopVoice();
    }
}
