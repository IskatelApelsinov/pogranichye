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
                throw new FormatException(path + " must be an object or null.");

            RequireFiniteNumber(obj["x"], path + ".x");
            RequireFiniteNumber(obj["y"], path + ".y");
            var radius = RequireFiniteNumber(obj["radius"], path + ".radius");
            if (radius <= 0d || radius > 10d)
                throw new FormatException(path + ".radius must be greater than 0 and no greater than 10.");

            var bartender = obj["bartender_present"];
            if (bartender == null || bartender.Type != JTokenType.Boolean)
                throw new FormatException(path + ".bartender_present must be a boolean.");
        }

        private static double RequireFiniteNumber(JToken token, string path)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException(path + " must be a number.");
            double value;
            try
            {
                value = token.Value<double>();
            }
            catch (Exception ex)
            {
                throw new FormatException(path + " must be a finite number.", ex);
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException(path + " must be a finite number.");
            return value;
        }
    }
}
