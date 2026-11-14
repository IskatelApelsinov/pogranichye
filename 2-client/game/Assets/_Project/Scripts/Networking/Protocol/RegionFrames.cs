using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class RegionFrame
    {
        [JsonProperty("nodes")] public List<RegionNodeFrame> Nodes { get; set; }
        [JsonProperty("roads")] public List<RegionRoadFrame> Roads { get; set; }
    }

    [Serializable]
    public sealed class RegionNodeFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("tier")] public string Tier { get; set; }
    }

    [Serializable]
    public sealed class RegionRoadFrame
    {
        [JsonProperty("a")] public string A { get; set; }
        [JsonProperty("b")] public string B { get; set; }
        [JsonProperty("len")] public double Length { get; set; }
    }

    public static class RegionFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            // RegionView was added additively; older v1 snapshots may omit it.
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            var nodesToken = obj["nodes"];
            var roadsToken = obj["roads"];
            if (nodesToken == null || nodesToken.Type != JTokenType.Array)
                throw new FormatException(path + ".nodes должен быть массивом.");
            if (roadsToken == null || roadsToken.Type != JTokenType.Array)
                throw new FormatException(path + ".roads должен быть массивом.");

            var nodes = (JArray)nodesToken;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hereCount = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i] as JObject;
                var itemPath = path + ".nodes[" + i + "]";
                if (node == null) throw new FormatException(itemPath + " должен быть объектом.");
                var id = RequireString(node["id"], itemPath + ".id", true);
                if (!ids.Add(id)) throw new FormatException(itemPath + ".id дублируется.");
                RequireNumber(node["x"], itemPath + ".x");
                RequireNumber(node["y"], itemPath + ".y");
                var state = RequireString(node["state"], itemPath + ".state", true);
                var name = RequireString(node["name"], itemPath + ".name", false);
                var tier = RequireString(node["tier"], itemPath + ".tier", false);
                switch (state)
                {
                    case "here":
                        hereCount++;
                        break;
                    case "visited":
                    case "known":
                        break;
                    case "unknown":
                        // The server intentionally strips identifying text from unknown places.
                        if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(tier))
                            throw new FormatException(itemPath + " раскрывает имя/тип неизвестного места.");
                        break;
                    default:
                        throw new FormatException(itemPath + ".state имеет неизвестное значение.");
                }
            }
            if (nodes.Count > 0 && hereCount != 1)
                throw new FormatException(path + " должен содержать ровно один узел here.");

            var roads = (JArray)roadsToken;
            var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < roads.Count; i++)
            {
                var road = roads[i] as JObject;
                var itemPath = path + ".roads[" + i + "]";
                if (road == null) throw new FormatException(itemPath + " должен быть объектом.");
                var a = RequireString(road["a"], itemPath + ".a", true);
                var b = RequireString(road["b"], itemPath + ".b", true);
                if (a == b || !ids.Contains(a) || !ids.Contains(b))
                    throw new FormatException(itemPath + " ссылается на отсутствующий или тот же узел.");
                var edgeKey = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
                if (!edgeKeys.Add(edgeKey)) throw new FormatException(itemPath + " дублирует дорогу.");
                RequireNumber(road["len"], itemPath + ".len");
                if (road["len"].Value<double>() < 0d)
                    throw new FormatException(itemPath + ".len не может быть отрицательной.");
            }
        }

        private static string RequireString(JToken token, string path, bool nonEmpty)
        {
            if (token == null || token.Type != JTokenType.String ||
                (nonEmpty && string.IsNullOrWhiteSpace(token.Value<string>())))
                throw new FormatException(path + " должен быть строкой.");
            return token.Value<string>();
        }

        private static void RequireNumber(JToken token, string path)
        {
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException(path + " должен быть конечным числом.");
        }
    }
}
