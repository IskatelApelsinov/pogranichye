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

        /// <summary>Loads the v1.19 world manifest. This is streaming metadata, not player knowledge.</summary>
        public async Task<WorldManifestFrame> GetWorldManifestAsync()
        {
            using (var request = UnityWebRequest.Get(_baseUrl + "/api/world"))
            {
                request.timeout = 15;
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Не удалось получить /api/world: " + request.error);
                var manifest = JsonConvert.DeserializeObject<WorldManifestFrame>(request.downloadHandler.text);
                if (manifest == null || manifest.Scheme != "yof-tangent" || manifest.Version != 1 ||
                    manifest.Units != "m" || manifest.ChunkMeters != 256 || manifest.LayerMeters != 64 ||
                    manifest.Frames == null || manifest.Settlements == null || manifest.Roads == null)
                    throw new InvalidOperationException("Сервер вернул неподдерживаемую карту мира v1.19.");
                foreach (var settlement in manifest.Settlements)
                    if (settlement == null || string.IsNullOrEmpty(settlement.Id) ||
                        !IsFinite(settlement.East) || !IsFinite(settlement.North) ||
                        !IsFinite(settlement.Up) || !IsFinite(settlement.Half))
                        throw new InvalidOperationException("Карта мира содержит некорректное поселение.");
                return manifest;
            }
        }

        /// <summary>
        /// Loads one deterministic 256 m terrain chunk. Heights are south-to-north rows,
        /// west-to-east within each row; shared border samples make adjacent chunks seamless.
        /// </summary>
        public async Task<TerrainChunkFrame> GetTerrainChunkAsync(long chunkX, long chunkZ, int stepMeters = 16)
        {
            if (stepMeters != 8 && stepMeters != 16 && stepMeters != 32 && stepMeters != 64)
                throw new ArgumentOutOfRangeException(nameof(stepMeters), "Шаг рельефа: 8, 16, 32 или 64 м.");
            var url = _baseUrl + "/api/terrain?cx=" +
                chunkX.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "&cz=" + chunkZ.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "&step=" + stepMeters.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Не удалось получить /api/terrain: " + request.error);
                var chunk = JsonConvert.DeserializeObject<TerrainChunkFrame>(request.downloadHandler.text);
                if (chunk == null || !chunk.IsValid || chunk.Chunk[0] != chunkX ||
                    chunk.Chunk[1] != chunkZ || chunk.StepMeters != stepMeters)
                    throw new InvalidOperationException("Сервер вернул некорректный или неожиданный чанк рельефа.");
                return chunk;
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
