using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine.Networking;

namespace Pogranichye.Client.Networking
{
    public sealed class HttpAuthClient
    {
        private readonly string _baseUrl;

        public HttpAuthClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Адрес сервера не задан.", nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Fetches settlement geometry. Without an id the server returns the tutorial village (pre-v1.19
        /// behaviour); with <paramref name="settlementId"/> (place.id) it returns that settlement's own layout.
        /// </summary>
        public async Task<WorldLayoutFrame> GetLayoutAsync(string settlementId = null)
        {
            var url = _baseUrl + "/api/layout";
            if (!string.IsNullOrEmpty(settlementId))
                url += "?settlement=" + UnityWebRequest.EscapeURL(settlementId);
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Не удалось получить /api/layout: " + request.error);
                var layout = JsonConvert.DeserializeObject<WorldLayoutFrame>(request.downloadHandler.text);
                if (layout == null || layout.Version != 2 || layout.WorldHalf <= 0 ||
                    layout.Buildings == null || layout.River == null || layout.Pois == null ||
                    layout.Buildings.Count < 1 || !layout.Pois.ContainsKey("tavern") ||
                    !layout.Pois.ContainsKey("woods") || !layout.Pois.ContainsKey("garden") ||
                    !layout.Pois.ContainsKey("mine") || !layout.Pois.ContainsKey("river") ||
                    !IsFinite(layout.WorldHalf) || !IsFinite(layout.VillageHalf) ||
                    !IsFinite(layout.River.Y0) || !IsFinite(layout.River.Y1) ||
                    !IsFinite(layout.River.BridgeX0) || !IsFinite(layout.River.BridgeX1) ||
                    layout.River.Y0 >= layout.River.Y1 || layout.River.BridgeX0 >= layout.River.BridgeX1)
                    throw new InvalidOperationException("Сервер вернул неподдерживаемую или неполную геометрию Layout v2.");

                foreach (var building in layout.Buildings)
                {
                    if (building == null || !IsFinite(building.X0) || !IsFinite(building.Y0) ||
                        !IsFinite(building.X1) || !IsFinite(building.Y1) ||
                        building.X0 >= building.X1 || building.Y0 >= building.Y1 ||
                        building.X0 < -layout.WorldHalf || building.X1 > layout.WorldHalf ||
                        building.Y0 < -layout.WorldHalf || building.Y1 > layout.WorldHalf)
                        throw new InvalidOperationException("Layout v2 содержит некорректный footprint здания.");
                }
                return layout;
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public Task<AuthResponse> RegisterAsync(string login, string password)
        {
            return PostAsync("/api/register", login, password);
        }

        public Task<AuthResponse> LoginAsync(string login, string password)
        {
            return PostAsync("/api/login", login, password);
        }

        private async Task<AuthResponse> PostAsync(string path, string login, string password)
        {
            var body = JsonConvert.SerializeObject(new AuthRequest
            {
                Login = login,
                Password = password
            });

            using (var request = new UnityWebRequest(_baseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.timeout = 15;

                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;

                var responseText = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(responseText))
                    throw new InvalidOperationException(
                        $"Сервер вернул пустой ответ ({request.responseCode}): {request.error}");

                AuthResponse response;
                try
                {
                    response = JsonConvert.DeserializeObject<AuthResponse>(responseText);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("Ответ авторизации не является корректным JSON.", ex);
                }

                if (request.result != UnityWebRequest.Result.Success && response?.Ok != true)
                    throw new InvalidOperationException(response?.Message ?? request.error ?? "Ошибка HTTP.");

                return response ?? throw new InvalidOperationException("Пустой ответ авторизации.");
            }
        }
    }
}
