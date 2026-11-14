using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking
{
    /// <summary>
    /// Validated partial v1 update. Fast frames are patches, never full snapshots.
    /// The JObject members preserve additive v1 fields while the envelope and merge
    /// boundaries are validated here, outside gameplay/presentation code.
    /// </summary>
    public sealed class FastFramePatch
    {
        public JObject Me { get; }
        public JObject Place { get; }

        internal FastFramePatch(JObject me, JObject place)
        {
            Me = me;
            Place = place;
        }
    }

    public static class FastFrameParser
    {
        public const int SupportedVersion = 1;

        /// <summary>
        /// Returns false only when the JSON object is not marked as a fast frame.
        /// A malformed object explicitly marked fast throws FormatException and must
        /// not be retried as a full snapshot.
        /// </summary>
        public static bool TryParse(string json, out FastFramePatch patch)
        {
            patch = null;
            if (string.IsNullOrWhiteSpace(json)) return false;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Быстрый кадр не является корректным JSON-объектом.", ex);
            }

            var fast = root["fast"];
            if (fast == null || fast.Type != JTokenType.Boolean || !fast.Value<bool>())
                return false;

            var version = root["v"];
            if (version == null || version.Type != JTokenType.Integer ||
                version.Value<int>() != SupportedVersion)
                throw new FormatException("Быстрый кадр имеет неподдерживаемую или отсутствующую версию протокола.");

            var alive = root["alive"];
            if (alive == null || alive.Type != JTokenType.Boolean || !alive.Value<bool>())
                throw new FormatException("Быстрый кадр должен содержать alive:true.");

            var meToken = root["me"];
            var placeToken = root["place"];
            if (meToken != null && meToken.Type != JTokenType.Null && meToken.Type != JTokenType.Object)
                throw new FormatException("Быстрый кадр: me должен быть объектом или null.");
            if (placeToken != null && placeToken.Type != JTokenType.Null && placeToken.Type != JTokenType.Object)
                throw new FormatException("Быстрый кадр: place должен быть объектом или null.");

            var me = meToken as JObject;
            var place = placeToken as JObject;
            if (me == null && place == null)
                throw new FormatException("Быстрый кадр не содержит ни одного обновляемого раздела.");

            ValidateOptionalObject(me, "me.combat");
            ValidateOptionalArray(me, "me.feed");
            ValidateOptionalObjectOrNull(me, "me.result");
            ValidateOptionalArray(place, "place.foes");
            ValidateOptionalArray(place, "place.neighbors");
            ValidateOptionalArray(place, "place.foes");
            ValidateOptionalArray(place, "place.exits");
            ValidateNeighborEntries(place);
            ValidateFoeEntries(place);

            patch = new FastFramePatch(me, place);
            return true;
        }


        private static void ValidateNeighborEntries(JObject place)
        {
            var token = place?["neighbors"] as JArray;
            if (token == null) return;
            for (var i = 0; i < token.Count; i++)
            {
                var item = token[i] as JObject;
                if (item == null || item["id"]?.Type != JTokenType.String)
                    throw new FormatException("place.neighbors[" + i + "] должен содержать строковый id.");
                var pos = item["pos"];
                if (pos != null && pos.Type != JTokenType.Null && pos.Type != JTokenType.Object)
                    throw new FormatException("place.neighbors[" + i + "].pos должен быть объектом.");
            }
        }

        private static void ValidateFoeEntries(JObject place)
        {
            var token = place?["foes"] as JArray;
            if (token == null) return;
            for (var i = 0; i < token.Count; i++)
            {
                var item = token[i] as JObject;
                if (item == null || item["id"]?.Type != JTokenType.String)
                    throw new FormatException("place.foes[" + i + "] должен содержать строковый id.");
            }
        }

        private static void ValidateOptionalObject(JObject parent, string field)
        {
            if (parent == null) return;
            var name = FieldName(field);
            var token = parent[name];
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Object)
                throw new FormatException(field + " должен быть объектом.");
        }

        private static void ValidateOptionalObjectOrNull(JObject parent, string field)
        {
            ValidateOptionalObject(parent, field);
        }

        private static void ValidateOptionalArray(JObject parent, string field)
        {
            if (parent == null) return;
            var name = FieldName(field);
            var token = parent[name];
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Array)
                throw new FormatException(field + " должен быть массивом.");
        }

        private static string FieldName(string path)
        {
            var dot = path.LastIndexOf('.');
            return dot >= 0 ? path.Substring(dot + 1) : path;
        }
    }
}
