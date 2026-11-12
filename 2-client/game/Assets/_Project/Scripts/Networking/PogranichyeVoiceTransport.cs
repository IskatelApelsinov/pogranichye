using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleWebRTC;
using UnityEngine;

namespace Pogranichye.Client.Networking
{
    /// <summary>
    /// Adapts SimpleWebRTC's v2 signaling messages to the authenticated Pogranichye
    /// WebSocket voice envelope. The server remains authoritative for room/range filtering.
    /// </summary>
    public sealed class PogranichyeVoiceTransport : SignalingTransport
    {
        [Serializable]
        private sealed class VoiceEnvelope
        {
            public string type;
            public string action;
            public string signaling_type;
            public string from_id;
            public string room;
            public string sdp;
            public string candidate;
            public string sdp_mid;
            public int sdp_mline_index;
        }

        private WorldSocketClient _socket;
        private string _localEntityId;
        private const string PeerManagerRoom = "pogranichye-nearby";
        private CancellationToken _cancellationToken;
        private bool _disposed;

        public void Bind(WorldSocketClient socket, string localEntityId, CancellationToken cancellationToken)
        {
            if (_socket != null) _socket.VoiceSignalReceived -= OnVoiceSignalReceived;
            _socket = socket;
            _localEntityId = localEntityId;
            _cancellationToken = cancellationToken;
            if (_socket != null) _socket.VoiceSignalReceived += OnVoiceSignalReceived;
        }

        public override void Connect()
        {
            if (_disposed || _socket == null || !_socket.IsConnected ||
                string.IsNullOrWhiteSpace(_localEntityId) || State != SignalingState.Disconnected)
                return;
            SetState(SignalingState.Connecting);
            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            try
            {
                await _socket.SendVoiceSignalAsync("join", null, null, null, null, -1, _cancellationToken);
                SetState(SignalingState.Connected);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Pogranichye voice join failed: " + ex.Message);
                SetState(SignalingState.Disconnected);
            }
        }

        public override void Disconnect()
        {
            if (State == SignalingState.Connected && _socket != null && _socket.IsConnected)
                _ = SendServerAsync("leave", null, null, null, null, -1);
            SetState(SignalingState.Disconnected);
        }

        public override void Send(string raw)
        {
            if (!SignalingMessage.TryParse(raw, out var message)) return;
            switch (message.type)
            {
                case SignalingMessageType.Join:
                case SignalingMessageType.Welcome:
                    // The authenticated server already performed join discovery.
                    return;
                case SignalingMessageType.Leave:
                    _ = SendServerAsync("leave", null, null, null, null, -1);
                    return;
                case SignalingMessageType.Offer:
                case SignalingMessageType.Answer:
                    var description = SessionDescription.FromJSON(message.payload);
                    if (description != null && !string.IsNullOrWhiteSpace(description.sdp))
                        _ = SendServerAsync(message.type, message.to, description.sdp, null, null, -1);
                    return;
                case SignalingMessageType.Candidate:
                    var candidate = CandidateInit.FromJSON(message.payload);
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                        _ = SendServerAsync("ice", message.to, null, candidate.candidate,
                            candidate.sdpMid, candidate.sdpMLineIndex);
                    return;
            }
        }

        private async Task SendServerAsync(string action, string target, string sdp,
            string candidate, string mid, int mline)
        {
            try
            {
                if (_socket != null && _socket.IsConnected)
                    await _socket.SendVoiceSignalAsync(action, target, sdp, candidate, mid, mline, _cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Pogranichye voice signaling failed: " + ex.Message);
            }
        }

        private void OnVoiceSignalReceived(string raw)
        {
            if (_disposed || string.IsNullOrEmpty(raw)) return;
            VoiceEnvelope envelope;
            try { envelope = JsonUtility.FromJson<VoiceEnvelope>(raw); }
            catch { return; }
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.from_id)) return;

            string type;
            string payload = "";
            switch (envelope.action)
            {
                case "peer_joined":
                    type = envelope.signaling_type;
                    if (type != SignalingMessageType.Join && type != SignalingMessageType.Welcome) return;
                    break;
                case "peer_left":
                    type = SignalingMessageType.Leave;
                    break;
                case "offer":
                case "answer":
                    type = envelope.action;
                    payload = new SessionDescription { type = envelope.action, sdp = envelope.sdp }.ConvertToJSON();
                    break;
                case "ice":
                    type = SignalingMessageType.Candidate;
                    payload = new CandidateInit
                    {
                        candidate = envelope.candidate,
                        sdpMid = envelope.sdp_mid,
                        sdpMLineIndex = envelope.sdp_mline_index
                    }.ConvertToJSON();
                    break;
                case "ice_complete":
                    return; // SimpleWebRTC treats the empty end-of-candidates marker as optional.
                default:
                    return;
            }

            ReceiveMessage(new SignalingMessage(type, PeerManagerRoom, envelope.from_id,
                _localEntityId, payload).ToJson());
        }

        protected override void OnDestroy()
        {
            Disconnect();
            if (_socket != null) _socket.VoiceSignalReceived -= OnVoiceSignalReceived;
            _socket = null;
            _disposed = true;
            base.OnDestroy();
        }
    }
}
