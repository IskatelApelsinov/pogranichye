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
            { "tavern", new PoiCoordinate(112d, 86d) }
        };
        var legacy = new Dictionary<string, PoiCoordinate>
        {
            { "tavern", new PoiCoordinate(-33d, 12d) },
            { "woods", new PoiCoordinate(-95d, 10d) }
        };

        PoiCoordinate point;
        Check(PoiPositionPolicy.TryResolve("tavern", layout, legacy, true, out point) &&
              point.X == 112d && point.Y == 86d,
            "current Layout v2 POI overrides the starter-layout mirror");
        Check(!PoiPositionPolicy.TryResolve("woods", layout, legacy, true, out point),
            "a missing POI in a generated layout does not fall back to a stale starter coordinate");
        Check(PoiPositionPolicy.TryResolve("tavern", layout, legacy, false, out point) &&
              point.X == -33d && point.Y == 12d,
            "legacy starter coordinates remain available when no server POI map exists");
        Check(!PoiPositionPolicy.TryResolve(null, layout, legacy, true, out point) &&
              !PoiPositionPolicy.TryResolve("", layout, legacy, false, out point),
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
