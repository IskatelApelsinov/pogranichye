using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pogranichye.Client;
using Pogranichye.Client.Networking.Protocol;

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
        public GameplayStatusPatch Status { get; }
        public bool HasFeed { get; }
        public List<FeedEntryFrame> Feed { get; }
        public bool HasResult { get; }
        public ActionResultFrame Result { get; }
        public bool HasCombat { get; }
        public CombatFrame Combat { get; }
        public bool HasFoes { get; }
        public List<FoeFrame> Foes { get; }

        internal FastFramePatch(
            JObject me, JObject place, GameplayStatusPatch status,
            bool hasFeed, List<FeedEntryFrame> feed,
            bool hasResult, ActionResultFrame result,
            bool hasCombat, CombatFrame combat,
            bool hasFoes, List<FoeFrame> foes)
        {
            Me = me;
            Place = place;
            Status = status;
            HasFeed = hasFeed;
            Feed = feed;
            HasResult = hasResult;
            Result = result;
            HasCombat = hasCombat;
            Combat = combat;
            HasFoes = hasFoes;
            Foes = foes;
        }
    }

    public static class FastFrameParser
    {
        public const int SupportedVersion = 1;

        /// <summary>
        /// Returns false when the payload cannot be classified as a fast frame.
        /// Valid JSON explicitly marked fast but violating the fast contract throws
        /// FormatException. Invalid JSON falls through to the strict full-frame parser,
        /// which reports the malformed payload through its normal error path.
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
            catch (JsonException)
            {
                // We cannot safely classify malformed JSON as fast. Let the strict
                // full-frame parser reject it; never guess based on a substring.
                return false;
            }

            var fast = root["fast"];
            if (fast == null) return false;
            if (fast.Type != JTokenType.Boolean)
                throw new FormatException("Поле fast должно быть boolean.");
            if (!fast.Value<bool>()) return false;

            var version = root["v"];
            if (version == null || version.Type != JTokenType.Integer ||
                version.Value<int>() != SupportedVersion)
                throw new FormatException("Быстрый кадр имеет неподдерживаемую или отсутствующую версию протокола.");

            var alive = root["alive"];
            if (alive == null || alive.Type != JTokenType.Boolean)
                throw new FormatException("Поле alive в fast-кадре должно быть boolean.");
            // The server emits a terminal fast envelope when the entity disappears.
            // Route it through the full parser so the normal alive:false lifecycle runs.
            if (!alive.Value<bool>()) return false;

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

            ValidateOptionalObject(me, "me.pos");
            ValidateOptionalObject(me, "me.wounds");
            ValidateOptionalArray(me, "me.feed");
            ValidateOptionalNumber(me, "me.health");
            ValidateOptionalNumber(me, "me.energy");
            ValidateOptionalNumber(me, "me.mana");
            var status = ReadStatusPatch(me);
            // Preserve absent-vs-explicit-null semantics for the server event stream.
            var hasFeed = me != null && me.Property("feed") != null;
            var feed = hasFeed ? FeedFrameParser.Parse(me["feed"], "me.feed") : null;
            // me.result is a one-slot server acknowledgement, not an event queue.
            var hasResult = me != null && me.Property("result") != null;
            var result = hasResult ? ActionResultFrameParser.Parse(me["result"], "me.result") : null;
            var hasCombat = me != null && me.Property("combat") != null;
            var combat = hasCombat ? CombatFrameParser.Parse(me["combat"], "me.combat") : null;
            var hasFoes = place != null && place.Property("foes") != null;
            var foes = hasFoes ? FoeFrameParser.Parse(place["foes"], "place.foes") : null;
            ValidateOptionalArray(place, "place.neighbors");
            ValidateOptionalArray(place, "place.exits");
            ValidateNeighborEntries(place);

            patch = new FastFramePatch(me, place, status, hasFeed, feed, hasResult, result, hasCombat, combat, hasFoes, foes);
            return true;
        }


        private static GameplayStatusPatch ReadStatusPatch(JObject me)
        {
            if (me == null)
                return new GameplayStatusPatch(false, 0f, false, 0f, false, 0f, false, 0f);

            var hasHealth = TryReadOptionalNumber(me, "health", out var health);
            var hasEnergy = TryReadOptionalNumber(me, "energy", out var energy);
            var hasMana = TryReadOptionalNumber(me, "mana", out var mana);
            var hasStamina = TryReadPositionStamina(me, out var stamina);
            return new GameplayStatusPatch(
                hasHealth, health, hasEnergy, energy, hasMana, mana, hasStamina, stamina);
        }

        private static bool TryReadPositionStamina(JObject me, out float stamina)
        {
            stamina = 0f;
            var token = me?["pos"];
            if (token == null) return false;
            if (token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.Object)
                throw new FormatException("me.pos должен быть объектом или null.");

            // A present position replaces the previous position object. If its optional
            // stamina member is absent/null, the legacy numeric selector reads it as zero.
            TryReadOptionalNumber((JObject)token, "stam", out stamina);
            return true;
        }

        private static bool TryReadOptionalNumber(JObject parent, string field, out float value)
        {
            value = 0f;
            if (parent == null) return false;
            var token = parent[field];
            if (token == null) return false;
            if (token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new FormatException(field + " должен быть числом или null.");

            var number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) ||
                number > float.MaxValue || number < -float.MaxValue)
                throw new FormatException(field + " выходит за диапазон конечного float.");
            value = (float)number;
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

        private static void ValidateOptionalObject(JObject parent, string field)
        {
            if (parent == null) return;
            var name = FieldName(field);
            var token = parent[name];
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Object)
                throw new FormatException(field + " должен быть объектом.");
        }

        private static void ValidateOptionalNumber(JObject parent, string field)
        {
            if (parent == null) return;
            var token = parent[FieldName(field)];
            if (token != null && token.Type != JTokenType.Null &&
                token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new FormatException(field + " должен быть числом.");
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
