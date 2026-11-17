using System;
using System.Collections.Generic;
using Pogranichye.Client.Presentation;

public static class PoiPositionPolicyCheck
{
    private static int _failed;

    private static void Check(bool value, string message)
    {
        if (value) Console.WriteLine("ok:   " + message);
        else
        {
            _failed++;
            Console.WriteLine("FAIL: " + message);
        }
    }

    public static int Main()
    {
        var layout = new Dictionary<string, PoiCoordinate>
        {
            { "tavern", new PoiCoordinate(112d, 86d) },
            { "garden", new PoiCoordinate(70d, -20d) }
        };
        var legacy = new Dictionary<string, PoiCoordinate>
        {
            { "tavern", new PoiCoordinate(-33d, 12d) },
            { "garden", new PoiCoordinate(70d, -20d) },
            { "apiary", new PoiCoordinate(73.6d, -20.9d) },
            { "woods", new PoiCoordinate(-95d, 10d) }
        };

        Check(PoiPositionPolicy.IsLayoutForSettlement("village-a", "village-a"),
            "the selected layout is accepted for its own settlement");
        Check(!PoiPositionPolicy.IsLayoutForSettlement("village-a", "village-b"),
            "a previous settlement layout is rejected during A-to-B transition");
        Check(PoiPositionPolicy.IsLayoutForSettlement("village-a", "village-a"),
            "returning from B to A can reuse A's cached layout");
        Check(!PoiPositionPolicy.IsLayoutForSettlement(null, "village-b"),
            "a missing selected layout never matches the current settlement");
        Check(PoiPositionPolicy.AllowsLegacyFallback("starter") &&
              PoiPositionPolicy.AllowsLegacyFallback(null),
            "starter and pre-v1.19 frames retain the legacy compatibility path");
        Check(!PoiPositionPolicy.AllowsLegacyFallback("cross") &&
              !PoiPositionPolicy.AllowsLegacyFallback("street"),
            "explicit generated plans do not use starter POI anchors");

        PoiCoordinate point;
        Check(PoiPositionPolicy.TryResolve("tavern", layout, legacy, true, false, out point) &&
              point.X == 112d && point.Y == 86d,
            "current Layout v2 POI overrides the starter-layout mirror");
        Check(PoiPositionPolicy.TryResolve("apiary", layout, legacy, true, false, out point) &&
              point.X == 70d && point.Y == -20d,
            "apiary resolves to the server's garden POI, not its visual hive offset");
        var explicitApiary = new Dictionary<string, PoiCoordinate>
        {
            { "garden", new PoiCoordinate(70d, -20d) },
            { "apiary", new PoiCoordinate(73.6d, -20.9d) }
        };
        Check(PoiPositionPolicy.TryResolve("apiary", explicitApiary, legacy, true, false, out point) &&
              point.X == 70d && point.Y == -20d,
            "a visual apiary key cannot override the server's current garden interaction anchor");
        Check(!PoiPositionPolicy.TryResolve("woods", layout, legacy, true, false, out point),
            "a missing POI in a generated layout does not fall back to a stale starter coordinate");
        Check(PoiPositionPolicy.TryResolve("tavern", null, legacy, false, true, out point) &&
              point.X == -33d && point.Y == 12d,
            "legacy starter coordinates remain available when explicitly allowed");
        Check(PoiPositionPolicy.TryResolve("apiary", null, legacy, false, true, out point) &&
              point.X == 70d && point.Y == -20d,
            "legacy apiary affordance uses the server's garden anchor, not the visual hive offset");
        Check(!PoiPositionPolicy.TryResolve("tavern", null, legacy, false, false, out point),
            "generated settlement waiting for its layout never uses starter coordinates");
        Check(!PoiPositionPolicy.TryResolve(null, layout, legacy, true, false, out point) &&
              !PoiPositionPolicy.TryResolve("", layout, legacy, false, true, out point),
            "empty POI identifiers fail closed");

        Check(PoiPositionPolicy.IsWithin(114d, 86d, 112d, 86d, 2d),
            "the exact interaction-radius boundary is included");
        Check(!PoiPositionPolicy.IsWithin(114.01d, 86d, 112d, 86d, 2d),
            "positions outside the interaction radius are rejected");
        Check(!PoiPositionPolicy.IsWithin(double.NaN, 0d, 0d, 0d, 2d) &&
              !PoiPositionPolicy.IsWithin(0d, 0d, 0d, 0d, -1d),
            "non-finite coordinates and negative radii fail safely");

        Console.WriteLine(_failed == 0
            ? "PoiPositionPolicy: ALL CHECKS PASSED"
            : "PoiPositionPolicy: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
