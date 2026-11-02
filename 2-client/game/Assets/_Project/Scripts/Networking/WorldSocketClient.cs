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
        public event Action<string> Closed;
        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public async Task ConnectAsync(string httpBaseUrl, string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Токен не задан.", nameof(token));

            var uri = new Uri(httpBaseUrl.TrimEnd('/'));
            var scheme = uri.Scheme == "https" ? "wss" : "ws";
            var builder = new UriBuilder(uri) { Scheme = scheme, Port = uri.IsDefaultPort ? (scheme == "wss" ? 443 : 80) : uri.Port, Path = "/ws", Query = "token=" + Uri.EscapeDataString(token) };

            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(builder.Uri, _lifetime.Token).ConfigureAwait(false);
            _ = ReceiveLoopAsync(_lifetime.Token);
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


        public async Task SendMoveAsync(float mx, float my, bool run, float face, float dt,
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