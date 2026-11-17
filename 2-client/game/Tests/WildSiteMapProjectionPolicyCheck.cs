using System;
using Pogranichye.Client.Presentation;

internal static class WildSiteMapProjectionPolicyCheck
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(double actual, double expected, string message)
    {
        Assert(Math.Abs(actual - expected) < 0.000001d, message + $" (actual {actual}, expected {expected})");
    }

    private static void Main()
    {
        double east, north;
        Assert(WildSiteMapProjectionPolicy.TryToSettlementLocal(1120d, -300d, 1000d, -250d, out east, out north),
            "global site should project through settlement centre");
        Near(east, 120d, "east must be relative to the settlement anchor");
        Near(north, -50d, "north must be relative to the settlement anchor");

        Assert(WildSiteMapProjectionPolicy.TryToSettlementLocal(-255d, 257d, -256d, 256d, out east, out north),
            "negative coordinates around chunk seams should remain valid");
        Near(east, 1d, "negative east anchor subtraction");
        Near(north, 1d, "north anchor subtraction");

        Assert(!WildSiteMapProjectionPolicy.TryToSettlementLocal(double.NaN, 0d, 0d, 0d, out east, out north),
            "NaN coordinates must be rejected");
        Assert(!WildSiteMapProjectionPolicy.TryToSettlementLocal(0d, 0d, double.PositiveInfinity, 0d, out east, out north),
            "infinite anchors must be rejected");
        Assert(!WildSiteMapProjectionPolicy.TryToSettlementLocal(2000000d, 0d, 0d, 0d, out east, out north),
            "unbounded offsets must be rejected");
        Console.WriteLine("Wild site map projection checks passed.");
    }
}
