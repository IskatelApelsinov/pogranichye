using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class TavernCounterFrame
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("radius")] public double Radius { get; set; }
        [JsonProperty("bartender_present")] public bool BartenderPresent { get; set; }
    }

    public static class TavernCounterFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            // Additive v1 field: older servers and settlements without a tavern geometry
            // may omit it or send null. Unknown is not the same as "counter is nearby".
            if (token == null || token.Type == JTokenType.Null) return;

            var obj = token as JObject;
            if (obj == null)
                throw new FormatException(path + " должен быть объектом или null.");

            RequireFiniteNumber(obj["x"], path + ".x");
            RequireFiniteNumber(obj["y"], path + ".y");
            var radius = RequireFiniteNumber(obj["radius"], path + ".radius");
            if (radius <= 0d || radius > 10d)
                throw new FormatException(path + ".radius должен быть больше 0 и не больше 10.");

            var bartender = obj["bartender_present"];
            if (bartender == null || bartender.Type != JTokenType.Boolean)
                throw new FormatException(path + ".bartender_present должен быть boolean.");
        }

        private static double RequireFiniteNumber(JToken token, string path)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException(path + " должен быть числом.");
            double value;
            if (!JsonNumberReader.TryReadFiniteDouble(token, out value))
                throw new FormatException(path + " должен быть конечным числом.");
            return value;
        }
    }
}
