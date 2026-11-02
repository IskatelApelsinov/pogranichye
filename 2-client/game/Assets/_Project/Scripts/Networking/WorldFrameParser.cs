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
                ValidatePosition(me["pos"], "me.pos", true);

                var place = root["place"];
                if (place != null && place.Type != JTokenType.Null)
                {
                    if (place.Type != JTokenType.Object)
                        throw new FormatException("Поле place должно быть объектом.");
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


        private static void ValidatePosition(JToken token, string fieldName, bool player)
        {
            if (token == null || token.Type == JTokenType.Null) return; // v1.0 compatibility
            if (token.Type != JTokenType.Object)
                throw new FormatException(fieldName + " должен быть объектом.");

            if (!IsNumber(token["x"]) || !IsNumber(token["y"]) || !IsNumber(token["facing"]))
                throw new FormatException(fieldName + " должен содержать числовые x, y и facing.");
            var facingValue = token["facing"].Value<double>();
            if (facingValue < 0d || facingValue >= 360d)
                throw new FormatException(fieldName + ".facing должен быть в диапазоне [0, 360).");

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
                if (staminaValue < 0d || staminaValue > 1d)
                    throw new FormatException(fieldName + ".stam должен находиться в диапазоне 0..1.");
            }
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
