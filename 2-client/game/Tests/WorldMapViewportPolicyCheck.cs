using System;
using Pogranichye.Client.Presentation;

internal static class WorldMapViewportPolicyCheck
{
    private static int _checks;
    private static void Near(float expected, float actual, string label)
    {
        _checks++;
        if (Math.Abs(expected - actual) > 0.01f)
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
    private static void True(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }

    public static int Main()
    {
        Near(520f, WorldMapViewportPolicy.GetMapSize(1920f, 1080f, false),
            "compact map keeps established size");
        Near(890f, WorldMapViewportPolicy.GetMapSize(1920f, 1080f, true),
            "expanded map uses available height");
        Near(530f, WorldMapViewportPolicy.GetMapSize(1280f, 720f, true),
            "720p expanded map fits screen height");
        Near(1250f, WorldMapViewportPolicy.GetMapSize(2560f, 1440f, true),
            "1440p expanded map uses wide viewport");
        True(WorldMapViewportPolicy.GetMapSize(360f, 600f, true) <= 360f,
            "portrait map does not exceed screen width");
        Near(180f, WorldMapViewportPolicy.GetMapSize(float.NaN, 720f, true),
            "invalid dimensions use safe minimum");
        Console.WriteLine("World map viewport policy checks passed: " + _checks);
        return 0;
    }
}
