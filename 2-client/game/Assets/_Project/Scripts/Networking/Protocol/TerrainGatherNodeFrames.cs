using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class TerrainGatherNodeFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("good")] public string Good { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("left")] public double Remaining { get; set; }
    }

    /// <summary>
    /// Optional /api/terrain.gather[] data. A malformed presentation node is discarded
    /// individually so it cannot invalidate the authoritative heightfield or road geometry.
    /// </summary>
    public static class TerrainGatherNodeFrameParser
    {
        public static JArray Normalize(JToken token, double sizeMeters = 256d)
        {
            var source = token as JArray;
            var normalized = new JArray();
            if (source == null || !IsFinite(sizeMeters) || sizeMeters <= 0d)
                return normalized;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in source)
            {
                var entry = candidate as JObject;
                if (entry == null) continue;

                string id, kind, good;
                if (!TryString(entry["id"], out id) || !IsGatherId(id) ||
                    !TryString(entry["kind"], out kind) ||
                    !TryString(entry["good"], out good))
                    continue;

                double x, z, remaining;
                if (!TryFiniteNumber(entry["x"], out x) ||
                    !TryFiniteNumber(entry["z"], out z) ||
                    !TryFiniteNumber(entry["left"], out remaining))
                    continue;

                if (x < 0d || z < 0d || x >= sizeMeters || z >= sizeMeters ||
                    remaining < 0d || !seen.Add(id))
                    continue;

                normalized.Add(entry.DeepClone());
            }

            return normalized;
        }

        private static bool IsGatherId(string value)
        {
            var parts = value.Split(':');
            long cellX, cellZ;
            return parts.Length == 3 && parts[0] == "g" &&
                long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellX) &&
                long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellZ);
        }

        private static bool TryString(JToken token, out string value)
        {
            value = token != null && token.Type == JTokenType.String
                ? token.Value<string>() : null;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryFiniteNumber(JToken token, out double value)
        {
            return JsonNumberReader.TryReadFiniteDouble(token, out value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
