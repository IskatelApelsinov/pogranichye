using System;
using Pogranichye.Client.Presentation;

internal static class WorldPositionProjectionPolicyCheck
{
    private static void Near(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.000001d)
            throw new Exception(name + ": expected " + expected + ", got " + actual);
    }

    private static int Main()
    {
        if (!WorldPositionProjectionPolicy.TryProject(0, 1000000000.25d, -250000000d, 120.75d,
            0, 999999990d, -250000005d, 118d, out var east, out var up, out var north))
            throw new Exception("same-frame global projection should succeed");

        Near(10.25d, east, "east");
        Near(2.75d, up, "up");
        Near(5d, north, "north");

        if (WorldPositionProjectionPolicy.TryProject(1, 10d, 20d, 30d,
            0, 0d, 0d, 0d, out east, out up, out north))
            throw new Exception("different frames must not be projected");

        if (WorldPositionProjectionPolicy.TryProject(0, double.NaN, 20d, 30d,
            0, 0d, 0d, 0d, out east, out up, out north))
            throw new Exception("non-finite world coordinates must be rejected");

        Console.WriteLine("World position projection policy checks passed.");
        return 0;
    }
}
