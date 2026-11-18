using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class CharacterModeFrame
    {
        [JsonProperty("selected")] public string Selected { get; set; }
        [JsonProperty("goal")] public string Goal { get; set; }
        [JsonProperty("act")] public string Activity { get; set; }
        [JsonProperty("act_code")] public string ActivityCode { get; set; }
        [JsonProperty("deviates")] public bool Deviates { get; set; }
        [JsonProperty("options")] public List<CharacterModeOptionFrame> Options { get; set; }
    }

    [Serializable]
    public sealed class CharacterModeOptionFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("hint")] public string Hint { get; set; }
    }

    [Serializable]
    public sealed class CharacterVigorFrame
    {
        [JsonProperty("energy")] public double Energy { get; set; }
        [JsonProperty("per_hour")] public double PerHour { get; set; }
        [JsonProperty("why")] public string Why { get; set; }
        [JsonProperty("tips")] public List<string> Tips { get; set; }
        [JsonProperty("full_in_hours")] public double? FullInHours { get; set; }
        [JsonProperty("empty_in_hours")] public double? EmptyInHours { get; set; }
        [JsonProperty("warning")] public string Warning { get; set; }
        [JsonProperty("stamina")] public CharacterStaminaVigorFrame Stamina { get; set; }
    }

    [Serializable]
    public sealed class CharacterStaminaVigorFrame
    {
        [JsonProperty("now")] public double Now { get; set; }
        [JsonProperty("regen_per_s")] public double RegenPerSecond { get; set; }
        [JsonProperty("drain_per_s")] public double DrainPerSecond { get; set; }
        [JsonProperty("full_in_s")] public double? FullInSeconds { get; set; }
    }

    public static class CharacterModeFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            ValidateOptionalString(obj["selected"], path + ".selected");
            ValidateOptionalString(obj["goal"], path + ".goal");
            ValidateOptionalString(obj["act"], path + ".act");
            ValidateOptionalString(obj["act_code"], path + ".act_code");
            if (obj["deviates"] != null && obj["deviates"].Type != JTokenType.Boolean)
                throw new FormatException(path + ".deviates должен быть boolean.");
            var options = obj["options"];
            if (options == null || options.Type == JTokenType.Null) return;
            var array = options as JArray;
            if (array == null) throw new FormatException(path + ".options должен быть массивом.");
            for (var i = 0; i < array.Count; i++)
            {
                var option = array[i] as JObject;
                if (option == null) throw new FormatException(path + ".options[" + i + "] должен быть объектом.");
                RequireString(option["id"], path + ".options[" + i + "].id");
                RequireString(option["name"], path + ".options[" + i + "].name");
                ValidateOptionalString(option["hint"], path + ".options[" + i + "].hint");
            }
        }

        private static void RequireString(JToken token, string path)
        {
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new FormatException(path + " должен быть непустой строкой.");
        }

        private static void ValidateOptionalString(JToken token, string path)
        {
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(path + " должен быть строкой или null.");
        }
    }

    public static class CharacterVigorFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");
            RequireFiniteNumber(obj["energy"], path + ".energy");
            // Older/mixed server builds may send per_hour as JSON null. The rate is
            // presentation metadata; default to a neutral rate instead of rejecting the whole frame.
            if (obj["per_hour"] == null || obj["per_hour"].Type == JTokenType.Null)
                obj["per_hour"] = 0d;
            RequireFiniteNumber(obj["per_hour"], path + ".per_hour");
            ValidateOptionalString(obj["why"], path + ".why");
            ValidateStringArray(obj["tips"], path + ".tips");
            ValidateOptionalNumber(obj["full_in_hours"], path + ".full_in_hours");
            ValidateOptionalNumber(obj["empty_in_hours"], path + ".empty_in_hours");
            ValidateOptionalString(obj["warning"], path + ".warning");
            var stamina = obj["stamina"];
            if (stamina == null || stamina.Type == JTokenType.Null) return;
            var st = stamina as JObject;
            if (st == null) throw new FormatException(path + ".stamina должен быть объектом или null.");
            RequireFiniteNumber(st["now"], path + ".stamina.now");
            RequireFiniteNumber(st["regen_per_s"], path + ".stamina.regen_per_s");
            RequireFiniteNumber(st["drain_per_s"], path + ".stamina.drain_per_s");
            ValidateOptionalNumber(st["full_in_s"], path + ".stamina.full_in_s");
        }

        private static void ValidateStringArray(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) throw new FormatException(path + " должен быть массивом строк.");
            foreach (var item in array)
                if (item == null || item.Type != JTokenType.String)
                    throw new FormatException(path + " должен содержать только строки.");
        }

        private static void ValidateOptionalString(JToken token, string path)
        {
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(path + " должен быть строкой или null.");
        }

        private static void ValidateOptionalNumber(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            RequireFiniteNumber(token, path);
        }

        private static void RequireFiniteNumber(JToken token, string path)
        {
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException(path + " должно быть конечным числом.");
        }
    }
}
