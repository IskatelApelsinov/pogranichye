using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class StatsFrame
    {
        [JsonProperty("strength")] public double Strength { get; set; }
        [JsonProperty("agility")] public double Agility { get; set; }
        [JsonProperty("endurance")] public double Endurance { get; set; }
        [JsonProperty("intellect")] public double Intellect { get; set; }
        [JsonProperty("perception")] public double Perception { get; set; }
        [JsonProperty("will")] public double Will { get; set; }

        public double Get(string key)
        {
            switch (key)
            {
                case "strength": return Strength;
                case "agility": return Agility;
                case "endurance": return Endurance;
                case "intellect": return Intellect;
                case "perception": return Perception;
                case "will": return Will;
                default: return 0d;
            }
        }
    }

    [Serializable]
    public sealed class CarryBagFrame
    {
        [JsonProperty("load")] public double Load { get; set; }
        [JsonProperty("cap")] public double Capacity { get; set; }
        [JsonProperty("over")] public bool Overloaded { get; set; }
    }

    [Serializable]
    public sealed class HorseFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("stam")] public double Stamina { get; set; }
        [JsonProperty("mounted")] public bool Mounted { get; set; }
        [JsonProperty("here")] public bool Here { get; set; }
        [JsonProperty("active")] public bool Active { get; set; }
        [JsonProperty("settlement_id")] public string SettlementId { get; set; }
        [JsonProperty("settlement_name")] public string SettlementName { get; set; }
        [JsonProperty("realm_name")] public string RealmName { get; set; }
        [JsonProperty("bag")] public Dictionary<string, double> Bag { get; set; }
        [JsonProperty("bag_load")] public double BagLoad { get; set; }
        [JsonProperty("bag_cap")] public double BagCapacity { get; set; }
    }

    public static class StatsFrameParser
    {
        private static readonly string[] Keys = { "strength", "agility", "endurance", "intellect", "perception", "will" };

        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            foreach (var key in Keys) RequireFiniteNumber(obj[key], path + "." + key);
        }

        private static void RequireFiniteNumber(JToken token, string path)
        {
            // Characteristics have no gameplay hard cap and may legitimately be negative
            // during migration/legacy-state recovery. Validate only wire shape and finiteness;
            // never reject an otherwise usable world frame because of a stat range.
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException(path + " должно быть конечным числом.");
        }
    }

    public static class CarryBagFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            // me.bag was added additively in v1.13; older v1 servers may omit it.
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            RequireNumber(obj["load"], path + ".load");
            RequireNumber(obj["cap"], path + ".cap");
            if (obj["over"] == null || obj["over"].Type != JTokenType.Boolean)
                throw new FormatException(path + ".over должен быть boolean.");
        }

        private static void RequireNumber(JToken token, string path)
        {
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > 1000000000d)
                throw new FormatException(path + " должен быть конечным неотрицательным числом.");
        }
    }

    public static class HorseFrameParser
    {
        public static void ValidateArray(JToken token, string path)
        {
            // v1.32 is additive: older servers may omit me.horses entirely.
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) throw new FormatException(path + " должен быть массивом.");
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] == null || array[i].Type == JTokenType.Null)
                    throw new FormatException(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "] должен быть объектом лошади.");
                Validate(array[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
            }
        }

        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            RequireString(obj["id"], path + ".id", true);
            RequireString(obj["name"], path + ".name", true);
            RequireNumber(obj["stam"], path + ".stam", 0d, 100d);
            RequireBool(obj["mounted"], path + ".mounted");
            RequireBool(obj["here"], path + ".here");
            if (obj["active"] != null) RequireBool(obj["active"], path + ".active");
            ValidateOptionalString(obj["settlement_id"], path + ".settlement_id");
            ValidateOptionalString(obj["settlement_name"], path + ".settlement_name");
            ValidateOptionalString(obj["realm_name"], path + ".realm_name");
            RequireNumber(obj["bag_load"], path + ".bag_load", 0d, 1000000000d);
            RequireNumber(obj["bag_cap"], path + ".bag_cap", 0d, 1000000000d);
            var bag = obj["bag"] as JObject;
            if (bag == null) throw new FormatException(path + ".bag должен быть объектом товаров.");
            foreach (var property in bag.Properties())
                RequireNumber(property.Value, path + ".bag." + property.Name, 0d, 1000000000d);
        }

        private static void ValidateOptionalString(JToken token, string path)
        {
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(path + " должен быть строкой или null.");
        }

        private static void RequireString(JToken token, string path, bool nonEmpty)
        {
            if (token == null || token.Type != JTokenType.String ||
                (nonEmpty && string.IsNullOrWhiteSpace(token.Value<string>())))
                throw new FormatException(path + " должен быть строкой.");
        }

        private static void RequireBool(JToken token, string path)
        {
            if (token == null || token.Type != JTokenType.Boolean)
                throw new FormatException(path + " должен быть boolean.");
        }

        private static void RequireNumber(JToken token, string path, double min, double max)
        {
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new FormatException(path + " содержит некорректное число.");
        }
    }
}
