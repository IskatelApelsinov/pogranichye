using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    [Serializable]
    public sealed class FeedEntryFrame
    {
        [JsonProperty("seq")] public long Sequence { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("foe")] public string Foe { get; set; }
        [JsonProperty("part")] public string Part { get; set; }
        [JsonProperty("dmg")] public double Damage { get; set; }
    }

    /// <summary>
    /// Validates the bounded, server-confirmed me.feed event list and maps it to typed entries.
    /// Unknown additive fields are ignored; event ordering and required display fields are strict.
    /// </summary>
    public static class FeedFrameParser
    {
        public static void Validate(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var items = token as JArray;
            if (items == null)
                throw new FormatException(fieldName + " должен быть массивом или null.");

            long previousSequence = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] as JObject;
                var itemPath = fieldName + "[" + i + "]";
                if (item == null)
                    throw new FormatException(itemPath + " должен быть объектом.");

                var sequenceToken = item["seq"];
                long sequence;
                if (sequenceToken == null || sequenceToken.Type != JTokenType.Integer ||
                    !long.TryParse(sequenceToken.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) ||
                    sequence <= previousSequence)
                    throw new FormatException(itemPath + ".seq должен быть положительным строго возрастающим int64.");
                previousSequence = sequence;

                var kind = item["kind"];
                if (kind == null || kind.Type != JTokenType.String || string.IsNullOrWhiteSpace(kind.Value<string>()))
                    throw new FormatException(itemPath + ".kind должен быть непустой строкой.");
                var text = item["text"];
                if (text == null || text.Type != JTokenType.String)
                    throw new FormatException(itemPath + ".text должен быть строкой.");

                ValidateOptionalString(item["foe"], itemPath + ".foe");
                ValidateOptionalString(item["part"], itemPath + ".part");

                var damageToken = item["dmg"];
                if (damageToken != null && damageToken.Type != JTokenType.Null)
                {
                    if (damageToken.Type != JTokenType.Integer && damageToken.Type != JTokenType.Float)
                        throw new FormatException(itemPath + ".dmg должен быть числом.");
                    var damage = damageToken.Value<double>();
                    if (double.IsNaN(damage) || double.IsInfinity(damage) ||
                        damage < 0d || damage > float.MaxValue)
                        throw new FormatException(itemPath + ".dmg должен быть конечным неотрицательным числом в диапазоне float.");
                }
            }
        }

        public static List<FeedEntryFrame> Parse(JToken token, string fieldName)
        {
            Validate(token, fieldName);
            if (token == null || token.Type == JTokenType.Null) return null;
            try
            {
                return token.ToObject<List<FeedEntryFrame>>();
            }
            catch (JsonException ex)
            {
                throw new FormatException("Не удалось преобразовать " + fieldName + " в типизированные события.", ex);
            }
        }

        private static void ValidateOptionalString(JToken token, string fieldName)
        {
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(fieldName + " должен быть строкой или null.");
        }
    }
}
