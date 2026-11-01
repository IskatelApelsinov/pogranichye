using System;
using System.Globalization;
using Newtonsoft.Json;
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

            WorldFrame frame;
            try
            {
                frame = JsonConvert.DeserializeObject<WorldFrame>(json);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Кадр сервера не является корректным JSON.", ex);
            }

            if (frame == null)
                throw new FormatException("Не удалось разобрать кадр сервера.");

            if (frame.Version != SupportedVersion)
                throw new NotSupportedException(
                    $"Версия протокола {frame.Version} не поддерживается. Клиент поддерживает v{SupportedVersion}.");

            if (!frame.Alive)
                return frame;

            if (frame.Me == null)
                throw new FormatException("В живом кадре отсутствует объект me.");

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

            return frame;
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
