using System;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

public static class JsonNumberReaderCheck
{
    private static int _failures;

    public static int Main()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            foreach (var cultureName in new[] { "ru-RU", "en-US", "nl-NL" })
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                var token = JObject.Parse("{\"decimal\":0.1,\"integer\":42,\"large\":1e300}");
                double decimalValue, largeValue;
                long integerValue;
                Check("decimal JSON number under " + cultureName,
                    JsonNumberReader.TryReadFiniteDouble(token["decimal"], out decimalValue) &&
                    Math.Abs(decimalValue - 0.1d) < 0.0000001d);
                Check("large finite JSON number under " + cultureName,
                    JsonNumberReader.TryReadFiniteDouble(token["large"], out largeValue) &&
                    largeValue > 1e299d);
                Check("integer JSON number under " + cultureName,
                    JsonNumberReader.TryReadInt64(token["integer"], out integerValue) && integerValue == 42L);
                Check("reject string number under " + cultureName,
                    !JsonNumberReader.TryReadFiniteDouble(JValue.CreateString("0.1"), out decimalValue));
                Check("reject non-finite overflow under " + cultureName,
                    !JsonNumberReader.TryReadFiniteDouble(new JValue(double.PositiveInfinity), out largeValue));
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }

        Console.WriteLine(_failures == 0
            ? "All JSON numeric culture checks passed."
            : _failures + " JSON numeric culture check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine("FAIL: " + name);
    }
}