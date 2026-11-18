using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class CombatFrame
    {
        [JsonProperty("stance")] public string Stance { get; set; }
        [JsonProperty("guard")] public bool Guard { get; set; }
        [JsonProperty("sneak")] public bool Sneak { get; set; }
        [JsonProperty("feint")] public bool Feint { get; set; }
        [JsonProperty("cd")] public double Cooldown { get; set; }
        [JsonProperty("shield")] public double Shield { get; set; }
        [JsonProperty("light")] public double Light { get; set; }
        [JsonProperty("arrows")] public int Arrows { get; set; }
        [JsonProperty("weapon")] public string Weapon { get; set; }
        // CombatC.Kills is a species -> count map; CombatC.KO is an integer counter.
        [JsonProperty("kills")] public Dictionary<string, int> Kills { get; set; }
        [JsonProperty("ko")] public int Knockouts { get; set; }
    }

    [Serializable]
    public sealed class FoeFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("facing")] public double Facing { get; set; }
        [JsonProperty("hp")] public double Hp { get; set; }
        [JsonProperty("max")] public double Max { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("aware")] public bool Aware { get; set; }
        [JsonProperty("slow")] public bool? Slow { get; set; }
        [JsonProperty("weak")] public bool? Weak { get; set; }
        [JsonProperty("blind")] public bool? Blind { get; set; }
        [JsonProperty("stun")] public bool? Stun { get; set; }
        [JsonProperty("windup")] public double? Windup { get; set; }
    }
}

namespace Pogranichye.Client.Networking
{
    using Pogranichye.Client.Networking.Protocol;

    /// <summary>Validates the exact server CombatView shape without inventing combat state.</summary>
    public static class CombatFrameParser
    {
        public static void Validate(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(fieldName + " должен быть объектом или null.");

            RequireString(obj, "stance", fieldName, false);
            RequireBool(obj, "guard", fieldName);
            RequireBool(obj, "sneak", fieldName);
            RequireBool(obj, "feint", fieldName);
            RequireNumber(obj, "cd", fieldName, true);
            RequireNumber(obj, "shield", fieldName, true);
            RequireNumber(obj, "light", fieldName, true);
            RequireInteger(obj, "arrows", fieldName);
            RequireString(obj, "weapon", fieldName, true);
            RequireIntegerMap(obj, "kills", fieldName);
            RequireInteger(obj, "ko", fieldName);
        }

        public static CombatFrame Parse(JToken token, string fieldName)
        {
            Validate(token, fieldName);
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.ToObject<CombatFrame>(); }
            catch (JsonException ex) { throw new FormatException("Не удалось преобразовать " + fieldName + ".", ex); }
        }

        private static void RequireString(JObject obj, string key, string path, bool allowEmpty)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.String ||
                (!allowEmpty && string.IsNullOrEmpty(token.Value<string>())))
                throw new FormatException(path + "." + key + " должен быть строкой.");
        }

        private static void RequireBool(JObject obj, string key, string path)
        {
            if (obj[key] == null || obj[key].Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " должен быть boolean.");
        }

        private static void RequireNumber(JObject obj, string key, string path, bool nonNegative)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException(path + "." + key + " должен быть числом.");
            double value;
            if (!JsonNumberReader.TryReadFiniteDouble(token, out value) || (nonNegative && value < 0d))
                throw new FormatException(path + "." + key + " должен быть конечным неотрицательным числом.");
        }

        private static void RequireInteger(JObject obj, string key, string path)
        {
            var token = obj[key];
            long value;
            if (token == null || token.Type != JTokenType.Integer ||
                !long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                value < 0 || value > int.MaxValue)
                throw new FormatException(path + "." + key + " должен быть неотрицательным int32.");
        }

        private static void RequireIntegerMap(JObject obj, string key, string path)
        {
            var map = obj[key] as JObject;
            if (map == null) throw new FormatException(path + "." + key + " должен быть объектом счётчиков.");
            foreach (var property in map.Properties())
            {
                long value;
                if (property.Value.Type != JTokenType.Integer ||
                    !long.TryParse(property.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                    value < 0 || value > int.MaxValue)
                    throw new FormatException(path + "." + key + "." + property.Name + " должен быть неотрицательным int32.");
            }
        }
    }

    /// <summary>Validates server-owned foe snapshots shared by full and fast frames.</summary>
    public static class FoeFrameParser
    {
        public static void Validate(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var items = token as JArray;
            if (items == null) throw new FormatException(fieldName + " должен быть массивом или null.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < items.Count; i++)
            {
                var obj = items[i] as JObject;
                var path = fieldName + "[" + i + "]";
                if (obj == null) throw new FormatException(path + " должен быть объектом.");
                var id = RequireString(obj, "id", path, false);
                RequireString(obj, "kind", path, false);
                RequireString(obj, "name", path, true);
                RequireString(obj, "state", path, false);
                RequireBool(obj, "aware", path);
                RequireNumber(obj, "x", path, false);
                RequireNumber(obj, "y", path, false);
                RequireNumber(obj, "facing", path, false);
                RequireNumber(obj, "hp", path, true);
                RequireNumber(obj, "max", path, true);
                if (obj["max"].Value<double>() <= 0d)
                    throw new FormatException(path + ".max должен быть больше нуля.");
                ValidateOptionalBool(obj, "slow", path);
                ValidateOptionalBool(obj, "weak", path);
                ValidateOptionalBool(obj, "blind", path);
                ValidateOptionalBool(obj, "stun", path);
                ValidateOptionalNumber(obj, "windup", path);
                if (!ids.Add(id)) throw new FormatException(path + ".id дублирует ID другого врага.");
            }
        }

        public static List<FoeFrame> Parse(JToken token, string fieldName)
        {
            Validate(token, fieldName);
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.ToObject<List<FoeFrame>>(); }
            catch (JsonException ex) { throw new FormatException("Не удалось преобразовать " + fieldName + ".", ex); }
        }

        private static string RequireString(JObject obj, string key, string path, bool allowEmpty)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.String ||
                (!allowEmpty && string.IsNullOrEmpty(token.Value<string>())))
                throw new FormatException(path + "." + key + " должен быть строкой.");
            return token.Value<string>();
        }

        private static void RequireBool(JObject obj, string key, string path)
        {
            if (obj[key] == null || obj[key].Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " должен быть boolean.");
        }

        private static void RequireNumber(JObject obj, string key, string path, bool nonNegative)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException(path + "." + key + " должен быть числом.");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || (nonNegative && value < 0d))
                throw new FormatException(path + "." + key + " должен быть конечным числом в допустимом диапазоне.");
        }

        private static void ValidateOptionalBool(JObject obj, string key, string path)
        {
            var token = obj[key];
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " должен быть boolean или null.");
        }

        private static void ValidateOptionalNumber(JObject obj, string key, string path)
        {
            var token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new FormatException(path + "." + key + " должен быть числом или null.");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new FormatException(path + "." + key + " должен быть конечным неотрицательным числом.");
        }
    }
}
