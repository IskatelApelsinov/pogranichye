using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    public static class WorldFrameParser
    {
        public const int SupportedVersion = 1;

        public static WorldFrame Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("Получен пустой кадр сервера.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Кадр сервера не является корректным JSON-объектом.", ex);
            }

            var versionToken = root["v"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                throw new FormatException("Поле v должно быть целым числом.");

            var version = versionToken.Value<int>();
            if (version != SupportedVersion)
                throw new NotSupportedException(
                    $"Версия протокола {version} не поддерживается. Клиент поддерживает v{SupportedVersion}.");

            var aliveToken = root["alive"];
            if (aliveToken == null || aliveToken.Type != JTokenType.Boolean)
                throw new FormatException("Поле alive должно быть boolean.");

            var alive = aliveToken.Value<bool>();
            if (alive)
            {
                var me = root["me"];
                if (me == null || me.Type != JTokenType.Object)
                    throw new FormatException("В живом кадре отсутствует объект me.");
                RequireStringId(me["id"], "me.id");
                ValidateJournal(me["journal"], "me.journal");
                ValidatePosition(me["pos"], "me.pos", true);
                ValidateWorldPosition(me["wpos"], "me.wpos");
                ValidateRoom(me["room"], "me.room");

                var place = root["place"];
                if (place != null && place.Type != JTokenType.Null)
                {
                    if (place.Type != JTokenType.Object)
                        throw new FormatException("Поле place должно быть объектом.");
                    ValidateWorldAnchor(place["anchor"], "place.anchor");
                    ValidatePlaceRooms(place["rooms"], "place.rooms");
                    var neighbors = place["neighbors"];
                    if (neighbors != null && neighbors.Type != JTokenType.Null)
                    {
                        if (neighbors.Type != JTokenType.Array)
                            throw new FormatException("place.neighbors должно быть массивом.");
                        var neighborArray = (JArray)neighbors;
                        for (var i = 0; i < neighborArray.Count; i++)
                        {
                            var neighbor = neighborArray[i];
                            if (neighbor == null || neighbor.Type != JTokenType.Object)
                                throw new FormatException($"place.neighbors[{i}] должен быть объектом.");
                            RequireStringId(neighbor["id"], $"place.neighbors[{i}].id");
                            ValidatePosition(neighbor["pos"], $"place.neighbors[{i}].pos", false);
                            ValidateWorldPosition(neighbor["wpos"], $"place.neighbors[{i}].wpos");
                        }
                    }
                }
            }

            WorldFrame frame;
            try
            {
                frame = root.ToObject<WorldFrame>();
            }
            catch (JsonException ex)
            {
                throw new FormatException("Не удалось преобразовать кадр v1 в DTO.", ex);
            }

            if (frame == null)
                throw new FormatException("Не удалось разобрать кадр сервера.");
            if (frame.Me != null && frame.Me.WorldPosition != null && !frame.Me.WorldPosition.IsValid)
                throw new FormatException("me.wpos: координаты, chunk и layer противоречат друг другу.");
            if (frame.Me != null && frame.Me.Room != null)
            {
                ParseEntityId(frame.Me.Room.Id, "me.room.id");
                ParseEntityId(frame.Me.Room.Settlement, "me.room.settlement");
            }
            if (frame.Place?.Neighbors != null)
            {
                for (var i = 0; i < frame.Place.Neighbors.Count; i++)
                {
                    var neighbor = frame.Place.Neighbors[i];
                    if (neighbor?.WorldPosition != null && !neighbor.WorldPosition.IsValid)
                        throw new FormatException($"place.neighbors[{i}].wpos: координаты, chunk и layer противоречат друг другу.");
                    if (neighbor?.WorldPosition != null && frame.Place.Anchor != null &&
                        neighbor.WorldPosition.Frame != frame.Place.Anchor.Frame)
                        throw new FormatException($"place.neighbors[{i}].wpos.frame не совпадает с place.anchor.frame.");
                    if (neighbor?.WorldPosition != null && neighbor.Position != null && frame.Place.Anchor != null &&
                        (Math.Abs(neighbor.WorldPosition.East - frame.Place.Anchor.East - neighbor.Position.X) > 0.002d ||
                         Math.Abs(neighbor.WorldPosition.North - frame.Place.Anchor.North - neighbor.Position.Y) > 0.002d))
                        throw new FormatException($"place.neighbors[{i}].wpos не совпадает с anchor + локальная pos.");
                }
            }
            if (frame.Place?.Rooms != null &&
                (frame.Place.Rooms.Total < 0 || frame.Place.Rooms.Vacant < 0 ||
                 frame.Place.Rooms.Vacant > frame.Place.Rooms.Total ||
                 !IsFinite(frame.Place.Rooms.RentPrice) || frame.Place.Rooms.RentPrice < 0d))
                throw new FormatException("place.rooms содержит некорректную сводку комнат.");
            if (frame.Me != null && frame.Me.Room != null &&
                (frame.Me.Room.LeaseState != "vacant" && frame.Me.Room.LeaseState != "active" &&
                 frame.Me.Room.LeaseState != "grace" && frame.Me.Room.LeaseState != "expired"))
                throw new FormatException("me.room.lease_state содержит неизвестное состояние аренды.");
            if (frame.Place != null && frame.Place.Anchor != null && frame.Me != null &&
                frame.Me.WorldPosition != null && frame.Place.Anchor.Frame != frame.Me.WorldPosition.Frame)
                throw new FormatException("place.anchor.frame не совпадает с me.wpos.frame.");
            if (frame.Alive)
            {
                ParseEntityId(frame.Me.Id, "me.id");
                if (frame.Place?.Neighbors != null)
                {
                    for (var i = 0; i < frame.Place.Neighbors.Count; i++)
                    {
                        var neighbor = frame.Place.Neighbors[i];
                        if (neighbor == null)
                            throw new FormatException($"place.neighbors[{i}] равен null.");
                        ParseEntityId(neighbor.Id, $"place.neighbors[{i}].id");
                    }
                }
            }
            return frame;
        }


        private static void ValidateWorldPosition(JToken token, string fieldName)
        {
            // Optional additive v1.19 field: older v:1 servers remain compatible.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object ||
                token["scheme"]?.Type != JTokenType.String || token["scheme"].Value<string>() != "yof-tangent" ||
                token["v"]?.Type != JTokenType.Integer ||
                !int.TryParse(token["v"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version != 1 ||
                token["frame"]?.Type != JTokenType.Integer ||
                !int.TryParse(token["frame"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) || frame < 0)
                throw new FormatException(fieldName + " имеет неподдерживаемую схему координат.");
            foreach (var key in new[] { "e", "n", "u", "ground", "z" })
            {
                if (!IsNumber(token[key]) || !IsFinite(token[key].Value<double>()))
                    throw new FormatException(fieldName + "." + key + " должен быть конечным числом в метрах.");
            }
            var chunk = token["chunk"];
            if (chunk == null || chunk.Type != JTokenType.Array || ((JArray)chunk).Count != 2)
                throw new FormatException(fieldName + ".chunk должен содержать два целых индекса.");
            for (var i = 0; i < 2; i++)
                if (chunk[i].Type != JTokenType.Integer ||
                    !long.TryParse(chunk[i].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new FormatException(fieldName + ".chunk содержит индекс вне диапазона int64.");
            if (token["layer"] == null || token["layer"].Type != JTokenType.Integer ||
                !long.TryParse(token["layer"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                throw new FormatException(fieldName + ".layer должен быть целым int64.");
        }

        private static void ValidateRoom(JToken token, string fieldName)
        {
            // Optional additive v1.20 field: null for players without a room and on old servers.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object)
                throw new FormatException(fieldName + " должен быть объектом или null.");
            RequireStringId(token["id"], fieldName + ".id");
            RequireStringId(token["settlement"], fieldName + ".settlement");
            if (token["door_poi"]?.Type != JTokenType.String ||
                token["lease_state"]?.Type != JTokenType.String ||
                token["indoor"]?.Type != JTokenType.Boolean)
                throw new FormatException(fieldName + " должен содержать door_poi, lease_state и indoor.");
            var state = token["lease_state"].Value<string>();
            if (state != "vacant" && state != "active" && state != "grace" && state != "expired")
                throw new FormatException(fieldName + ".lease_state содержит неизвестное состояние.");
            foreach (var key in new[] { "rent_price", "chest_cap", "chest_load" })
                if (token[key] != null && (!IsNumber(token[key]) || !IsFinite(token[key].Value<double>()) || token[key].Value<double>() < 0d))
                    throw new FormatException(fieldName + "." + key + " должен быть неотрицательным конечным числом.");
            foreach (var key in new[] { "paid_through_day", "grace_until_day" })
                if (token[key] != null && (token[key].Type != JTokenType.Integer ||
                    !long.TryParse(token[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) || day < 0))
                    throw new FormatException(fieldName + "." + key + " должен быть неотрицательным целым числом.");
            if (token["chest_items"] != null && token["chest_items"].Type != JTokenType.Object)
                throw new FormatException(fieldName + ".chest_items должен быть объектом.");
        }

        private static void ValidatePlaceRooms(JToken token, string fieldName)
        {
            // Optional additive v1.20 summary. Never includes private chest contents.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object ||
                token["total"]?.Type != JTokenType.Integer ||
                token["vacant"]?.Type != JTokenType.Integer ||
                !IsNumber(token["rent_price"]))
                throw new FormatException(fieldName + " должен содержать total, vacant и rent_price.");
        }

        private static void ValidateWorldAnchor(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object || token["frame"]?.Type != JTokenType.Integer ||
                !int.TryParse(token["frame"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) || frame < 0)
                throw new FormatException(fieldName + " должен содержать неотрицательный целочисленный frame.");
            foreach (var key in new[] { "e", "n", "u" })
                if (!IsNumber(token[key]) || !IsFinite(token[key].Value<double>()))
                    throw new FormatException(fieldName + "." + key + " должен быть конечным числом в метрах.");
        }

        private static void ValidateJournal(JToken token, string fieldName)
        {
            // Optional additive v1 field: old servers may omit it or send null.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Array)
                throw new FormatException(fieldName + " должен быть массивом.");

            var entries = (JArray)token;
            long previousSequence = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Type != JTokenType.Object)
                    throw new FormatException(fieldName + "[" + i + "] должен быть объектом.");

                var sequence = entry["seq"];
                if (sequence == null || sequence.Type != JTokenType.Integer ||
                    !long.TryParse(sequence.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var sequenceValue) ||
                    sequenceValue <= previousSequence)
                    throw new FormatException(fieldName + "[" + i + "].seq должен строго возрастать и быть положительным int64.");

                var day = entry["day"];
                if (day == null || day.Type != JTokenType.Integer ||
                    !long.TryParse(day.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var dayValue) || dayValue < 0)
                    throw new FormatException(fieldName + "[" + i + "].day должен быть неотрицательным целым числом int64.");

                var kind = entry["kind"];
                if (kind == null || kind.Type != JTokenType.String || string.IsNullOrWhiteSpace(kind.Value<string>()))
                    throw new FormatException(fieldName + "[" + i + "].kind должен быть непустой строкой.");

                var text = entry["text"];
                if (text == null || text.Type != JTokenType.String || string.IsNullOrWhiteSpace(text.Value<string>()))
                    throw new FormatException(fieldName + "[" + i + "].text должен быть непустой строкой.");

                previousSequence = sequenceValue;
            }
        }

        private static void ValidatePosition(JToken token, string fieldName, bool player)
        {
            if (token == null || token.Type == JTokenType.Null) return; // v1.0 compatibility
            if (token.Type != JTokenType.Object)
                throw new FormatException(fieldName + " должен быть объектом.");

            if (!IsNumber(token["x"]) || !IsNumber(token["y"]) || !IsNumber(token["facing"]))
                throw new FormatException(fieldName + " должен содержать числовые x, y и facing.");
            var xValue = token["x"].Value<double>();
            var yValue = token["y"].Value<double>();
            var facingValue = token["facing"].Value<double>();
            if (!IsFinite(xValue) || !IsFinite(yValue) || Math.Abs(xValue) > 150.001d || Math.Abs(yValue) > 150.001d)
                throw new FormatException(fieldName + " координаты должны быть конечными и находиться в границах мира ±150 м.");
            if (!IsFinite(facingValue) || facingValue < 0d || facingValue >= 360d)
                throw new FormatException(fieldName + ".facing должен быть конечным числом в диапазоне [0, 360).");

            if (player)
            {
                var stamina = token["stam"];
                var sequence = token["seq"];
                if (!IsNumber(stamina))
                    throw new FormatException(fieldName + ".stam должен быть числом.");
                if (sequence == null || sequence.Type != JTokenType.Integer ||
                    !long.TryParse(sequence.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequenceValue) ||
                    sequenceValue < 0)
                    throw new FormatException(fieldName + ".seq должен быть неотрицательным целым числом в диапазоне int64.");
                var staminaValue = stamina.Value<double>();
                if (!IsFinite(staminaValue) || staminaValue < 0d || staminaValue > 1d)
                    throw new FormatException(fieldName + ".stam должен находиться в диапазоне 0..1.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsNumber(JToken token)
        {
            return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);
        }

        private static void RequireStringId(JToken token, string fieldName)
        {
            if (token == null || token.Type != JTokenType.String)
                throw new FormatException($"{fieldName} должен быть JSON-строкой uint64 ID.");
            ParseEntityId(token.Value<string>(), fieldName);
        }

        public static ulong ParseEntityId(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new FormatException($"{fieldName} должен быть строковым uint64 ID.");
            }
            return id;
        }
    }
}
