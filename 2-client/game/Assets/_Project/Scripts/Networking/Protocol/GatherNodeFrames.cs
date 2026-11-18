using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class GatherNodeFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("good")] public string Good { get; set; }
        [JsonProperty("good_name")] public string GoodName { get; set; }
        [JsonProperty("left")] public double Remaining { get; set; }
        [JsonProperty("max")] public double Maximum { get; set; }
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
        [JsonProperty("u")] public double Up { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("dist")] public double DistanceMeters { get; set; }
        [JsonProperty("dir")] public string Direction { get; set; }
        [JsonProperty("here")] public bool Here { get; set; }
    }

    /// <summary>
    /// Optional v1.30 presentation data. Invalid nodes are hidden individually; they never
    /// invalidate the authoritative world frame or change server-side pickup eligibility.
    /// </summary>
    public static class GatherNodeFrameParser
    {
        public static JArray Normalize(JToken token)
        {
            var source = token as JArray;
            var normalized = new JArray();
            if (source == null) return normalized;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in source)
            {
                var entry = candidate as JObject;
                if (entry == null) continue;

                string id, kind, name, good, goodName, direction;
                if (!TryString(entry["id"], out id) ||
                    !TryString(entry["kind"], out kind) ||
                    !TryString(entry["name"], out name) ||
                    !TryString(entry["good"], out good) ||
                    !TryString(entry["good_name"], out goodName) ||
                    !TryString(entry["dir"], out direction))
                    continue;

                double east, north, up, x, y, distance, left, maximum;
                if (!TryFiniteNumber(entry["e"], out east) ||
                    !TryFiniteNumber(entry["n"], out north) ||
                    !TryFiniteNumber(entry["u"], out up) ||
                    !TryFiniteNumber(entry["x"], out x) ||
                    !TryFiniteNumber(entry["y"], out y) ||
                    !TryFiniteNumber(entry["dist"], out distance) ||
                    !TryFiniteNumber(entry["left"], out left) ||
                    !TryFiniteNumber(entry["max"], out maximum))
                    continue;

                if (distance < 0d || left < 0d || maximum <= 0d || left > maximum + 0.001d)
                    continue;

                var here = entry["here"];
                if (here == null || here.Type != JTokenType.Boolean || !seen.Add(id))
                    continue;

                normalized.Add(entry.DeepClone());
            }

            return normalized;
        }

        private static bool TryString(JToken token, out string value)
        {
            value = token != null && token.Type == JTokenType.String ? token.Value<string>() : null;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryFiniteNumber(JToken token, out double value)
        {
            value = 0d;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                return false;
            return JsonNumberReader.TryReadFiniteDouble(token, out value);
        }
    }
}
