using System;
using Pogranichye.Client.Presentation;

internal static class ClientUiScalePolicyCheck
{
    private static int _checks;
    private static void Equal<T>(T expected, T actual, string label)
    {
        _checks++;
        if (!Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }

    private static void Near(float expected, float actual, string label)
    {
        _checks++;
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }

    public static int Main()
    {
        Near(1f, ClientUiScalePolicy.ScaleForHeight(0), "invalid height");
        Near(1f, ClientUiScalePolicy.ScaleForHeight(720), "720p keeps baseline readable");
        Near(1f, ClientUiScalePolicy.ScaleForHeight(1080), "reference height");
        Near(1.1111111f, ClientUiScalePolicy.ScaleForHeight(1200), "moderate scale");
        Near(1.25f, ClientUiScalePolicy.ScaleForHeight(1440), "large display scale");
        Near(1.25f, ClientUiScalePolicy.ScaleForHeight(2160), "scale cap");
        Equal(15, ClientUiScalePolicy.ResolveFontSize(15, 720), "compact body size");
        Equal(19, ClientUiScalePolicy.ResolveFontSize(15, 1440), "large body size rounds up");
        Equal(1, ClientUiScalePolicy.ResolveFontSize(0, 1440), "zero base size");
        Equal(1, ClientUiScalePolicy.ResolveFontSize(-4, 1080), "negative base size");
        Console.WriteLine("Adaptive UI typography: " + _checks + " checks passed.");
        return 0;
    }
}
