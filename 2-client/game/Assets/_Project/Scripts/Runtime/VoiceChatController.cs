using System;
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
        private AudioReceiver _receiver;
        private AudioSource _microphoneSource;
        private bool _joined;
        private bool _microphoneEnabled;

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
                _sender.StopStreaming();
                _microphoneEnabled = false;
                Status = "Микрофон выключен. Вы слышите собеседников.";
                return;
            }
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Status = "Микрофон не найден или запрещён системой.";
                return;
            }
            _sender.StartStreaming();
            _microphoneEnabled = true;
            Status = "Микрофон включён. Нажмите ещё раз, чтобы заглушить.";
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
                _microphoneSource = microphoneObject.AddComponent<AudioSource>();
                _microphoneSource.playOnAwake = false;
                _microphoneSource.loop = true;

                _sender = _voiceRoot.AddComponent<AudioSender>();
                _sender.AudioSource = _microphoneSource;
                _sender.UseMicrophone = true;
                var playLocally = typeof(AudioSender).GetField("playLocally",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (playLocally != null) playLocally.SetValue(_sender, false);

                _receiver = _voiceRoot.AddComponent<AudioReceiver>();
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
            _joined = false;
            try { _sender?.StopStreaming(); } catch { }
            try { _manager?.Disconnect(); } catch { }
            if (_voiceRoot != null) Destroy(_voiceRoot);
            _voiceRoot = null;
            _manager = null;
            _transport = null;
            _sender = null;
            _receiver = null;
            _microphoneSource = null;
            Status = "Голос отключён.";
        }

        private void OnDisable() => StopVoice();
        private void OnDestroy() => StopVoice();
    }
}
