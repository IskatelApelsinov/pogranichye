using System;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    /// <summary>
    /// Reads JSON numeric tokens without converting them to culture-sensitive strings.
    /// All protocol validators should use this helper before accepting numeric values.
    /// </summary>
    internal static class JsonNumberReader
    {
        internal static bool TryReadFiniteDouble(JToken token, out double value)
        {
            value = 0d;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                return false;

            try
            {
                value = token.Value<double>();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch (Exception)
            {
                value = 0d;
                return false;
            }
        }

        internal static bool TryReadInt64(JToken token, out long value)
        {
            value = 0L;
            if (token == null || token.Type != JTokenType.Integer)
                return false;

            try
            {
                value = token.Value<long>();
                return true;
            }
            catch (Exception)
            {
                value = 0L;
                return false;
            }
        }

        internal static bool TryReadInt32(JToken token, out int value)
        {
            value = 0;
            long wideValue;
            if (!TryReadInt64(token, out wideValue) || wideValue < int.MinValue || wideValue > int.MaxValue)
                return false;
            value = (int)wideValue;
            return true;
        }
    }
}