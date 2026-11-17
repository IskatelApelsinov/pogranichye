using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Pogranichye.Client.Networking
{
    public sealed class WorldSocketClient : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private const int MaxMessageBytes = 1024 * 1024;

        private ClientWebSocket _socket;
        private CancellationTokenSource _lifetime;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);

        public event Action<string> MessageReceived;
        public event Action<string> ChatReceived;
        public event Action<string> VoiceSignalReceived;
        public event Action<string> Closed;
        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public async Task ConnectAsync(string httpBaseUrl, string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Токен не задан.", nameof(token));

            var uri = new Uri(httpBaseUrl.TrimEnd('/'));
            var scheme = uri.Scheme == "https" ? "wss" : "ws";
            var builder = new UriBuilder(uri) { Scheme = scheme, Port = uri.IsDefaultPort ? (scheme == "wss" ? 443 : 80) : uri.Port, Path = "/ws",
                // fast=8: between full 1 Hz snapshots the server also sends light "fast" frames (body, combat, foes)
                // for responsive combat; they are marked "fast":true and routed to CoreGameplay only.
                Query = "token=" + Uri.EscapeDataString(token) + "&fast=8" };

            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(builder.Uri, _lifetime.Token).ConfigureAwait(false);
            _ = ReceiveLoopAsync(_lifetime.Token);
        }

        public Task SendChatAsync(string channel, string text, string target,
            CancellationToken cancellationToken)
        {
            if (channel != "local" && channel != "whisper")
                throw new ArgumentException("Неизвестный канал чата.", nameof(channel));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Сообщение пустое.", nameof(text));
            var payload = new System.Collections.Generic.Dictionary<string, object>
            {
                ["v"] = 1, ["op"] = "chat", ["channel"] = channel, ["text"] = text
            };
            if (!string.IsNullOrWhiteSpace(target)) payload["target"] = target;
            return SendPayloadAsync(payload, cancellationToken);
        }

        public Task SendVoiceSignalAsync(string action, string target, string sdp, string candidate,
            string sdpMid, int sdpMLineIndex, CancellationToken cancellationToken)
        {
            if (action != "join" && action != "leave" && action != "offer" &&
                action != "answer" && action != "ice" && action != "ice_complete")
                throw new ArgumentException("Неизвестное действие голосовой связи.", nameof(action));
            var payload = new System.Collections.Generic.Dictionary<string, object>
            {
                ["v"] = 1, ["op"] = "voice", ["action"] = action
            };
            if (!string.IsNullOrWhiteSpace(target)) payload["target"] = target;
            if (!string.IsNullOrEmpty(sdp)) payload["sdp"] = sdp;
            if (!string.IsNullOrEmpty(candidate)) payload["candidate"] = candidate;
            if (!string.IsNullOrEmpty(sdpMid)) payload["sdp_mid"] = sdpMid;
            if (sdpMLineIndex >= 0) payload["sdp_mline_index"] = sdpMLineIndex;
            return SendPayloadAsync(payload, cancellationToken);
        }

        public async Task SendGoalAsync(string goal, CancellationToken cancellationToken)
        {
            if (!IsConnected)
                throw new InvalidOperationException("WebSocket не подключён.");

            var json = "{\"v\":1,\"op\":\"goal\",\"goal\":" + JsonConvert.SerializeObject(goal ?? string.Empty) + "}";
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }


        public Task SendTalkAsync(string target, CancellationToken cancellationToken) =>
            SendIntentAsync("talk", target, null, cancellationToken);

        public Task SendSayAsync(string line, CancellationToken cancellationToken) =>
            SendIntentAsync("say", null, line, cancellationToken);

        public Task SendEndTalkAsync(CancellationToken cancellationToken) =>
            SendIntentAsync("endtalk", null, null, cancellationToken);

        public Task SendAcceptDrinkAsync(bool accept, CancellationToken cancellationToken) =>
            SendIntentAsync(accept ? "accept" : "declinedrink", null, null, cancellationToken);

        public Task SendInviteDrinkAsync(string target, CancellationToken cancellationToken) =>
            SendIntentAsync("invite", target, null, cancellationToken);

        public async Task SendItemIntentAsync(string op, string item, CancellationToken cancellationToken)
        {
            if (op != "equip" && op != "wear" && op != "take" && op != "unwear")
                throw new ArgumentException("Неподдерживаемое действие с предметом.", nameof(op));
            if (string.IsNullOrWhiteSpace(item))
                throw new ArgumentException("Не задан предмет.", nameof(item));
            if (!IsConnected)
                throw new InvalidOperationException("WebSocket не подключён.");

            var payload = new System.Collections.Generic.Dictionary<string, object>
            {
                ["v"] = 1,
                ["op"] = op,
                ["item"] = item
            };
            // take and unwear require the server's bounded reqid dedup and correlated result.
            // equip/wear are state-setting intents and are naturally idempotent.
            if (op == "take" || op == "unwear")
                payload["reqid"] = Guid.NewGuid().ToString("N");

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    cancellationToken).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        public async Task SendActAsync(string action, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Не задано действие.", nameof(action));
            if (!IsConnected) throw new InvalidOperationException("WebSocket не подключён.");
            var payload = new System.Collections.Generic.Dictionary<string, object>
            {
                ["v"] = 1,
                ["op"] = "act",
                ["action"] = action,
                ["reqid"] = Guid.NewGuid().ToString("N")
            };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
            finally { _sendGate.Release(); }
        }

        /// <summary>Generic intent of protocol v1.10 (attack, weave, quest, craft, journey, party…).</summary>
        public async Task SendPayloadAsync(System.Collections.Generic.Dictionary<string, object> payload,
            CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!IsConnected) throw new InvalidOperationException("WebSocket не подключён.");
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
            finally { _sendGate.Release(); }
        }

        private async Task SendIntentAsync(string op, string target, string line, CancellationToken cancellationToken)
        {
            if (!IsConnected) throw new InvalidOperationException("WebSocket не подключён.");
            var payload = new System.Collections.Generic.Dictionary<string, object> { ["v"] = 1, ["op"] = op };
            if (!string.IsNullOrEmpty(target)) payload["target"] = target;
            if (!string.IsNullOrEmpty(line)) payload["line"] = line;
            // v1.8: say:drink mutates server state and requires a stable request ID.
            if (op == "say" && string.Equals(line, "drink", StringComparison.Ordinal))
                payload["reqid"] = Guid.NewGuid().ToString("N");
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
            finally { _sendGate.Release(); }
        }

        public async Task SendMoveAsync(float mx, float my, bool run, bool fast, float face, float dt,
            long sequence, CancellationToken cancellationToken)
        {
            if (!IsConnected)
                throw new InvalidOperationException("WebSocket не подключён.");

            var payload = new
            {
                v = 1,
                op = "move",
                mx,
                my,
                run,
                fast,
                face,
                dt,
                seq = sequence
            };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                Closed?.Invoke("Сервер закрыл WebSocket.");
                                return;
                            }
                            if (result.MessageType != WebSocketMessageType.Text)
                                throw new InvalidOperationException("Ожидался текстовый JSON-кадр.");

                            if (message.Length + result.Count > MaxMessageBytes)
                                throw new InvalidOperationException("Размер WebSocket-кадра превышает 1 МиБ.");

                            message.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        var json = StrictUtf8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                        // Chat uses a separate event envelope, not a world snapshot.
                        // Route it before strict frame parsing so it cannot invalidate the world view.
                        // Server emits compact JSON. A cheap discriminator avoids parsing every
                        // 8 Hz combat/snapshot frame into a second object graph (and extra GC).
                        if (json.IndexOf("\"type\":\"chat\"", StringComparison.Ordinal) >= 0)
                        {
                            ChatReceived?.Invoke(json);
                            continue;
                        }
                        if (json.IndexOf("\"type\":\"voice_signal\"", StringComparison.Ordinal) >= 0)
                        {
                            VoiceSignalReceived?.Invoke(json);
                            continue;
                        }
                        MessageReceived?.Invoke(json);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Closed?.Invoke(ex.Message);
            }
        }

        public void Dispose()
        {
            try { _lifetime?.Cancel(); } catch { }
            try { _socket?.Abort(); _socket?.Dispose(); } catch { }
            _lifetime?.Dispose();
        }
    }
}