using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class JourneyFrame
    {
        [JsonProperty("to")] public string To { get; set; }
        [JsonProperty("to_name")] public string ToName { get; set; }
        [JsonProperty("progress")] public double Progress { get; set; }
        [JsonProperty("eta")] public double Eta { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("note")] public string Note { get; set; }
        [JsonProperty("follow")] public bool Follow { get; set; }
        [JsonProperty("event")] public RoadEventFrame Event { get; set; }
        [JsonProperty("via")] public List<string> Via { get; set; }
    }

    [Serializable]
    public sealed class RoadEventFrame
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("foe")] public string Foe { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("options")] public List<string> Options { get; set; }
    }

    [Serializable]
    public sealed class QuestListFrame
    {
        [JsonProperty("offers")] public List<QuestOfferFrame> Offers { get; set; }
        [JsonProperty("active")] public List<ActiveQuestFrame> Active { get; set; }
        [JsonProperty("done")] public int Done { get; set; }
        [JsonProperty("rep")] public int Reputation { get; set; }
    }

    [Serializable]
    public sealed class QuestOfferFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("via")] public string Via { get; set; }
        [JsonProperty("giver")] public string Giver { get; set; }
        [JsonProperty("giver_name")] public string GiverName { get; set; }
        [JsonProperty("good")] public string Good { get; set; }
        [JsonProperty("species")] public string Species { get; set; }
        [JsonProperty("qty")] public int Quantity { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("coins")] public double Coins { get; set; }
    }

    [Serializable]
    public sealed class ActiveQuestFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("via")] public string Via { get; set; }
        [JsonProperty("giver")] public string Giver { get; set; }
        [JsonProperty("giver_name")] public string GiverName { get; set; }
        [JsonProperty("good")] public string Good { get; set; }
        [JsonProperty("species")] public string Species { get; set; }
        [JsonProperty("qty")] public int Quantity { get; set; }
        [JsonProperty("have")] public double Have { get; set; }
        [JsonProperty("ready")] public bool Ready { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("coins")] public double Coins { get; set; }
        [JsonProperty("settlement_id")] public string SettlementId { get; set; }
        [JsonProperty("settlement_name")] public string SettlementName { get; set; }
        [JsonProperty("realm_name")] public string RealmName { get; set; }
    }

    public static class JourneyFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            RequireString(obj, "to", path, true);
            RequireString(obj, "to_name", path, false);
            RequireNumber(obj, "progress", path, 0d, 1d);
            RequireNumber(obj, "eta", path, 0d, double.MaxValue);
            RequireString(obj, "scene", path, false);
            RequireString(obj, "note", path, false);
            RequireBool(obj, "follow", path);
            ValidateEvent(obj["event"], path + ".event");
            ValidateStrings(obj["via"], path + ".via");
        }

        private static void ValidateEvent(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            RequireString(obj, "kind", path, true);
            RequireString(obj, "foe", path, false);
            RequireString(obj, "text", path, false);
            ValidateStrings(obj["options"], path + ".options");
        }

        private static void ValidateStrings(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null)
                throw new FormatException(path + " должен быть массивом строк.");
            var array = token as JArray;
            if (array == null) throw new FormatException(path + " должен быть массивом строк.");
            for (var i = 0; i < array.Count; i++)
                if (array[i] == null || array[i].Type != JTokenType.String)
                    throw new FormatException(path + "[" + i + "] должен быть строкой.");
        }

        private static void RequireString(JObject obj, string key, string path, bool nonEmpty)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.String ||
                (nonEmpty && string.IsNullOrWhiteSpace(token.Value<string>())))
                throw new FormatException(path + "." + key + " должен быть " + (nonEmpty ? "непустой " : "") + "строкой.");
        }

        private static void RequireBool(JObject obj, string key, string path)
        {
            if (obj[key] == null || obj[key].Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " должен быть boolean.");
        }

        private static void RequireNumber(JObject obj, string key, string path, double min, double max)
        {
            var token = obj[key];
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new FormatException(path + "." + key + " содержит некорректное число.");
        }
    }

    public static class QuestListFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            ValidateArray(obj["offers"], path + ".offers", ValidateOffer);
            ValidateArray(obj["active"], path + ".active", ValidateActive);
            RequireInteger(obj, "done", path);
            RequireInteger(obj, "rep", path);
        }

        private static void ValidateArray(JToken token, string path, Action<JObject, string> validate)
        {
            if (token == null || token.Type != JTokenType.Array)
                throw new FormatException(path + " должен быть массивом.");
            var array = (JArray)token;
            for (var i = 0; i < array.Count; i++)
            {
                var obj = array[i] as JObject;
                if (obj == null) throw new FormatException(path + "[" + i + "] должен быть объектом.");
                validate(obj, path + "[" + i + "]");
            }
        }

        private static void ValidateOffer(JObject obj, string path)
        {
            RequireString(obj, "id", path, true);
            RequireString(obj, "kind", path, true);
            RequireString(obj, "via", path, true);
            RequireString(obj, "giver", path, false);
            RequireString(obj, "giver_name", path, false);
            OptionalString(obj, "good", path);
            OptionalString(obj, "species", path);
            RequireInteger(obj, "qty", path);
            RequireString(obj, "text", path, false);
            RequireMoney(obj, "coins", path);
        }

        private static void ValidateActive(JObject obj, string path)
        {
            RequireString(obj, "id", path, true);
            RequireString(obj, "kind", path, true);
            RequireString(obj, "via", path, true);
            RequireString(obj, "giver", path, false);
            RequireString(obj, "giver_name", path, false);
            RequireString(obj, "good", path, false);
            RequireString(obj, "species", path, false);
            RequireInteger(obj, "qty", path);
            RequireMoney(obj, "have", path);
            RequireBool(obj, "ready", path);
            RequireString(obj, "text", path, false);
            RequireMoney(obj, "coins", path);
            RequireString(obj, "settlement_id", path, false);
            RequireString(obj, "settlement_name", path, false);
            RequireString(obj, "realm_name", path, false);
        }

        private static void RequireString(JObject obj, string key, string path, bool nonEmpty)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.String ||
                (nonEmpty && string.IsNullOrWhiteSpace(token.Value<string>())))
                throw new FormatException(path + "." + key + " должен быть строкой.");
        }

        private static void OptionalString(JObject obj, string key, string path)
        {
            var token = obj[key];
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(path + "." + key + " должен быть строкой, null или отсутствовать.");
        }

        private static void RequireBool(JObject obj, string key, string path)
        {
            if (obj[key] == null || obj[key].Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " должен быть boolean.");
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

        private static void RequireMoney(JObject obj, string key, string path)
        {
            var token = obj[key];
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > 1000000000d)
                throw new FormatException(path + "." + key + " содержит некорректное неотрицательное число.");
        }
    }
}
