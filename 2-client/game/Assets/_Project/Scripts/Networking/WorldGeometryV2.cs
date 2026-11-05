using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Pogranichye.Client.Networking
{
    /// <summary>
    /// Client-side movement-prediction mirror of server sim/world_geometry.go.
    /// Runtime collision data is loaded from GET /api/layout; server frames remain authoritative.
    /// </summary>
    public static class WorldGeometryV2
    {
        public const int SupportedVersion = 2;
        public static int Version { get; private set; }
        public static float WorldHalf { get; private set; } = 150f;
        public static float VillageHalf { get; private set; } = 50f;
        public static bool HasServerLayout { get; private set; }

        private struct Rect
        {
            public readonly float X0, Y0, X1, Y1;
            public Rect(float x0, float y0, float x1, float y1)
            { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public bool Contains(float x, float y)
            { return x >= X0 && x <= X1 && y >= Y0 && y <= Y1; }
        }

        private static Rect[] _buildings =
        {
            new Rect(-40, 14, -26, 26),
            new Rect(22, 16, 36, 28),
            new Rect(-10, 30, 6, 42),
            new Rect(26, -30, 40, -18),
            new Rect(-40, -30, -28, -20),
            new Rect(-14, -34, -2, -24),
            new Rect(6, -36, 18, -26),
            new Rect(-6, 6, 8, 16)
        };

        private static float _riverY0 = -90f;
        private static float _riverY1 = -78f;
        private static float _bridgeX0 = -6f;
        private static float _bridgeX1 = 6f;

        public static async Task FetchAsync(string httpBaseUrl, CancellationToken cancellationToken)
        {
            var url = (httpBaseUrl ?? string.Empty).TrimEnd('/') + "/api/layout";
            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                using (cancellationToken.Register(() => completion.TrySetCanceled()))
                {
                    operation.completed += _ => completion.TrySetResult(true);
                    await completion.Task;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Не удалось загрузить /api/layout: " + request.error);

                ApplyServerLayout(request.downloadHandler.text);
            }
        }

        public static void ApplyServerLayout(string json)
        {
            var root = JObject.Parse(json);
            var version = root.Value<int?>("version") ?? 0;
            if (version != SupportedVersion)
                throw new NotSupportedException("Геометрия сервера v" + version +
                    " не поддерживается клиентом (ожидается v" + SupportedVersion + ").");

            var worldHalf = root.Value<float?>("world_half") ?? 0f;
            var villageHalf = root.Value<float?>("village_half") ?? 0f;
            var buildingsToken = root["buildings"] as JArray;
            var river = root["river"] as JObject;
            if (worldHalf <= 0f || villageHalf <= 0f || buildingsToken == null || river == null)
                throw new FormatException("В /api/layout отсутствуют обязательные границы, здания или река.");

            var parsedBuildings = new List<Rect>();
            foreach (var token in buildingsToken)
            {
                var building = token as JObject;
                if (building == null) throw new FormatException("Некорректная запись здания в /api/layout.");
                var x0 = building.Value<float?>("x0");
                var y0 = building.Value<float?>("y0");
                var x1 = building.Value<float?>("x1");
                var y1 = building.Value<float?>("y1");
                if (!x0.HasValue || !y0.HasValue || !x1.HasValue || !y1.HasValue ||
                    x0.Value >= x1.Value || y0.Value >= y1.Value)
                    throw new FormatException("Некорректные координаты здания в /api/layout.");
                parsedBuildings.Add(new Rect(x0.Value, y0.Value, x1.Value, y1.Value));
            }

            var riverY0 = river.Value<float?>("y0");
            var riverY1 = river.Value<float?>("y1");
            var bridgeX0 = river.Value<float?>("bridge_x0");
            var bridgeX1 = river.Value<float?>("bridge_x1");
            if (!riverY0.HasValue || !riverY1.HasValue || !bridgeX0.HasValue || !bridgeX1.HasValue ||
                riverY0.Value >= riverY1.Value || bridgeX0.Value >= bridgeX1.Value)
                throw new FormatException("Некорректная полоса реки/моста в /api/layout.");

            Version = version;
            WorldHalf = worldHalf;
            VillageHalf = villageHalf;
            _buildings = parsedBuildings.ToArray();
            _riverY0 = riverY0.Value;
            _riverY1 = riverY1.Value;
            _bridgeX0 = bridgeX0.Value;
            _bridgeX1 = bridgeX1.Value;
            HasServerLayout = true;
        }

        public static bool IsBlocked(Vector2 point)
        {
            if (point.x < -WorldHalf || point.x > WorldHalf ||
                point.y < -WorldHalf || point.y > WorldHalf) return true;
            for (var i = 0; i < _buildings.Length; i++)
                if (_buildings[i].Contains(point.x, point.y)) return true;
            if (point.y >= _riverY0 && point.y <= _riverY1 &&
                (point.x < _bridgeX0 || point.x > _bridgeX1)) return true;
            return false;
        }

        /// <summary>Exact axis-slide fallback order used by server ResolveMove.</summary>
        public static Vector2 ResolveMove(Vector2 origin, Vector2 desired)
        {
            if (!IsBlocked(desired)) return desired;
            if (!IsBlocked(new Vector2(desired.x, origin.y))) return new Vector2(desired.x, origin.y);
            if (!IsBlocked(new Vector2(origin.x, desired.y))) return new Vector2(origin.x, desired.y);
            return origin;
        }
    }
}
