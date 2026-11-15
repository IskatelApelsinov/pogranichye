using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    /// <summary>
    /// Validates the server's single-slot action acknowledgement. This is not a queue:
    /// RequestGate still correlates exactly one in-flight reqid against the latest result.
    /// </summary>
    public static class ActionResultFrameParser
    {
        public static void Validate(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null)
                throw new FormatException(fieldName + " должен быть объектом или null.");

            var reqid = obj["reqid"];
            if (reqid == null || reqid.Type != JTokenType.String || string.IsNullOrEmpty(reqid.Value<string>()))
                throw new FormatException(fieldName + ".reqid должен быть непустой строкой.");

            var ok = obj["ok"];
            if (ok == null || ok.Type != JTokenType.Boolean)
                throw new FormatException(fieldName + ".ok должен быть boolean.");

            var reason = obj["reason"];
            if (reason == null || reason.Type != JTokenType.String)
                throw new FormatException(fieldName + ".reason должен быть строкой.");
        }

        public static ActionResultFrame Parse(JToken token, string fieldName)
        {
            Validate(token, fieldName);
            if (token == null || token.Type == JTokenType.Null) return null;
            try
            {
                return token.ToObject<ActionResultFrame>();
            }
            catch (JsonException ex)
            {
                throw new FormatException("Не удалось преобразовать " + fieldName + " в типизированное подтверждение.", ex);
            }
        }
    }
}
