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
    public sealed class QuestFrame
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
        [JsonProperty("have")] public int? Have { get; set; }
        [JsonProperty("ready")] public bool? Ready { get; set; }
        [JsonProperty("settlement_id")] public string SettlementId { get; set; }
        [JsonProperty("settlement_name")] public string SettlementName { get; set; }
        [JsonProperty("realm_name")] public string RealmName { get; set; }
    }

    [Serializable]
    public sealed class QuestsFrame
    {
        [JsonProperty("offers")] public List<QuestFrame> Offers { get; set; }
        [JsonProperty("active")] public List<QuestFrame> Active { get; set; }
        [JsonProperty("done")] public int Done { get; set; }
        [JsonProperty("rep")] public int Reputation { get; set; }
    }
}

namespace Pogranichye.Client.Networking
{
    using Pogranichye.Client.Networking.Protocol;

    /// <summary>Validates only the public JourneyView projection, never the server's hidden route plan.</summary>
    public static class JourneyFrameParser
    {
        public static void Validate(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(fieldName + " must be an object or null.");
            RequireString(obj, "to", fieldName, false);
            RequireString(obj, "to_name", fieldName, true);
            RequireString(obj, "scene", fieldName, true);
            RequireString(obj, "note", fieldName, true);
            RequireBool(obj, "follow", fieldName);
            RequireNumber(obj, "progress", fieldName, 0d, 1d);
            RequireNumber(obj, "eta", fieldName, 0d, double.MaxValue);
            RequireStringArray(obj["via"], fieldName + ".via", false);
            ValidateEvent(obj["event"], fieldName + ".event");
        }

        public static JourneyFrame Parse(JToken token, string fieldName)
        {
            Validate(token, fieldName);
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.ToObject<JourneyFrame>(); }
            catch (JsonException ex) { throw new FormatException("Failed to convert " + fieldName + ".", ex); }
        }

        private static void ValidateEvent(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " must be an object or null.");
            RequireString(obj, "kind", path, false);
            RequireString(obj, "foe", path, true);
            RequireString(obj, "text", path, true);
            RequireStringArray(obj["options"], path + ".options", true);
        }

        private static void RequireString(JObject obj, string key, string path, bool allowEmpty)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.String ||
                (!allowEmpty && string.IsNullOrEmpty(token.Value<string>())))
                throw new FormatException(path + "." + key + " must be a string.");
        }

        private static void RequireBool(JObject obj, string key, string path)
        {
            if (obj[key] == null || obj[key].Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " must be a boolean.");
        }

        private static void RequireNumber(JObject obj, string key, string path, double min, double max)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException(path + "." + key + " must be a number.");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new FormatException(path + "." + key + " is outside the supported range.");
        }

        private static void RequireStringArray(JToken token, string path, bool allowNull)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (allowNull) return;
                throw new FormatException(path + " must be an array of strings.");
            }
            var array = token as JArray;
            if (array == null) throw new FormatException(path + " must be an array of strings.");
            for (var i = 0; i < array.Count; i++)
                if (array[i] == null || array[i].Type != JTokenType.String)
                    throw new FormatException(path + "[" + i + "] must be a string.");
        }
    }

    /// <summary>Validates the public quest view; hidden server-only quest fields are not projected.</summary>
    public static class QuestsFrameParser
    {
        public static void Validate(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(fieldName + " must be an object or null.");
            ValidateQuestArray(obj["offers"], fieldName + ".offers", false);
            ValidateQuestArray(obj["active"], fieldName + ".active", true);
            RequireInteger(obj, "done", fieldName, true);
            RequireInteger(obj, "rep", fieldName, false);
        }

        public static QuestsFrame Parse(JToken token, string fieldName)
        {
            Validate(token, fieldName);
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.ToObject<QuestsFrame>(); }
            catch (JsonException ex) { throw new FormatException("Failed to convert " + fieldName + ".", ex); }
        }

        private static void ValidateQuestArray(JToken token, string path, bool active)
        {
            var array = token as JArray;
            if (array == null) throw new FormatException(path + " must be an array.");
            for (var i = 0; i < array.Count; i++)
            {
                var obj = array[i] as JObject;
                var itemPath = path + "[" + i + "]";
                if (obj == null) throw new FormatException(itemPath + " must be an object.");
                RequireString(obj, "id", itemPath, false);
                RequireString(obj, "kind", itemPath, false);
                RequireString(obj, "via", itemPath, false);
                RequireString(obj, "giver", itemPath, true);
                RequireString(obj, "giver_name", itemPath, true);
                RequireString(obj, "text", itemPath, false);
                ValidateOptionalString(obj, "good", itemPath);
                ValidateOptionalString(obj, "species", itemPath);
                RequireInteger(obj, "qty", itemPath, true);
                RequireNumber(obj, "coins", itemPath);
                ValidateOptionalString(obj, "settlement_id", itemPath);
                ValidateOptionalString(obj, "settlement_name", itemPath);
                ValidateOptionalString(obj, "realm_name", itemPath);
                if (active)
                {
                    RequireInteger(obj, "have", itemPath, false);
                    RequireBool(obj, "ready", itemPath);
                    RequireString(obj, "settlement_id", itemPath, true);
                    RequireString(obj, "settlement_name", itemPath, true);
                    RequireString(obj, "realm_name", itemPath, true);
                }
            }
        }

        private static void RequireString(JObject obj, string key, string path, bool allowEmpty)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.String ||
                (!allowEmpty && string.IsNullOrEmpty(token.Value<string>())))
                throw new FormatException(path + "." + key + " must be a string.");
        }

        private static void ValidateOptionalString(JObject obj, string key, string path)
        {
            var token = obj[key];
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(path + "." + key + " must be a string or null.");
        }

        private static void RequireBool(JObject obj, string key, string path)
        {
            if (obj[key] == null || obj[key].Type != JTokenType.Boolean)
                throw new FormatException(path + "." + key + " must be a boolean.");
        }

        private static void RequireNumber(JObject obj, string key, string path)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException(path + "." + key + " must be a number.");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new FormatException(path + "." + key + " must be a finite non-negative number.");
        }

        private static void RequireInteger(JObject obj, string key, string path, bool nonNegative)
        {
            var token = obj[key];
            long value;
            if (token == null || token.Type != JTokenType.Integer ||
                !long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                value < int.MinValue || value > int.MaxValue || (nonNegative && value < 0))
                throw new FormatException(path + "." + key + " must be an integer in the int32 range.");
        }
    }
}
