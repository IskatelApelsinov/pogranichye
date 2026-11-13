using System;
using Pogranichye.Client.World;

internal static class HexCellCoordinateCheck
{
    private static int _checks;

    private static void Equal<T>(T expected, T actual, string label)
    {
        _checks++;
        if (!Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }

    private static void True(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }

    private static void Throws<T>(Action action, string label) where T : Exception
    {
        _checks++;
        try { action(); }
        catch (T) { return; }
        throw new Exception(label + ": expected " + typeof(T).Name);
    }

    public static int Main()
    {
        const double radius = 100d;
        var origin = HexCellCoordinate.FromWorld(0, 0d, 0d, radius);
        Equal("0:0:0", origin.Id, "origin id");

        for (var i = 0; i < 6; i++)
        {
            var neighbor = origin.Neighbor(i);
            Equal(1L, origin.DistanceTo(neighbor), "neighbor distance " + i);
            var reverseFound = false;
            for (var j = 0; j < 6; j++)
                if (neighbor.Neighbor(j) == origin) reverseFound = true;
            True(reverseFound, "neighbor relation must be reciprocal " + i);
        }

        var negative = HexCellCoordinate.FromWorld(0, -1d, -1d, radius);
        Equal(0L, negative.Q, "small negative east/north remains origin cell");
        Equal(0L, negative.R, "small negative north remains origin cell");

        var farNegative = HexCellCoordinate.FromWorld(0, -1.5d * Math.Sqrt(3d) * radius, -1.5d * radius, radius);
        Equal(-1L, farNegative.Q, "negative axial q");
        Equal(-1L, farNegative.R, "negative axial r");

        var cell = new HexCellCoordinate(3, -7, 12);
        cell.GetCenter(radius, out var east, out var north);
        Equal(cell, HexCellCoordinate.FromWorld(3, east, north, radius), "center round trip");
        Equal("3:-7:12", cell.Id, "stable frame-qualified id");
        Equal(0L, cell.DistanceTo(cell), "self distance");

        Throws<ArgumentOutOfRangeException>(() => HexCellCoordinate.FromWorld(0, 0, 0, 0), "zero radius");
        Throws<ArgumentOutOfRangeException>(() => HexCellCoordinate.FromWorld(-1, 0, 0, radius), "negative frame");
        Throws<ArgumentException>(() => cell.DistanceTo(origin), "cross-frame distance");
        Throws<ArgumentOutOfRangeException>(() => origin.Neighbor(6), "invalid direction");

        Console.WriteLine("Hex cell coordinate checks passed: " + _checks);
        return 0;
    }
}
