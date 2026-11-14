using System;
using Pogranichye.Client.Presentation;

public static class MapLabelLayoutPolicyCheck
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

    private static bool Inside(MapLabelPlacement label, float x, float y, float width, float height)
    {
        return label.X >= x && label.Y >= y &&
            label.X + label.Width <= x + width &&
            label.Y + label.Height <= y + height;
    }

    public static int Main()
    {
        Check(MapLabelLayoutPolicy.TryPlace(0f, 0f, 320f, 320f,
                80f, 100f, 112f, 18f, 4f, out var right) &&
              !right.AlignRight && Math.Abs(right.X - 84f) < 0.001f &&
              Inside(right, 0f, 0f, 320f, 320f),
            "labels use the right side when it fits");

        Check(MapLabelLayoutPolicy.TryPlace(0f, 0f, 320f, 320f,
                300f, 100f, 112f, 18f, 4f, out var left) &&
              left.AlignRight && Math.Abs(left.X - 184f) < 0.001f &&
              Inside(left, 0f, 0f, 320f, 320f),
            "labels flip left before reaching the right map edge");

        Check(MapLabelLayoutPolicy.TryPlace(10f, 20f, 320f, 240f,
                160f, 21f, 112f, 28f, 4f, out var top) &&
              Math.Abs(top.Y - 20f) < 0.001f,
            "labels clamp to the top map edge");

        Check(MapLabelLayoutPolicy.TryPlace(10f, 20f, 320f, 240f,
                160f, 259f, 112f, 28f, 4f, out var bottom) &&
              Math.Abs(bottom.Y - 232f) < 0.001f,
            "labels clamp to the bottom map edge");

        Check(MapLabelLayoutPolicy.TryPlace(0f, 0f, 320f, 320f,
                2f, 2f, 128f, 26f, 4f, out var corner) &&
              Inside(corner, 0f, 0f, 320f, 320f),
            "labels remain fully inside the viewport at the top-left corner");

        Check(!MapLabelLayoutPolicy.TryPlace(0f, 0f, 320f, 320f,
                float.NaN, 10f, 100f, 20f, 4f, out _),
            "non-finite marker positions fail safely");

        Check(!MapLabelLayoutPolicy.TryPlace(0f, 0f, 80f, 20f,
                40f, 10f, 81f, 18f, 4f, out _),
            "labels wider than the viewport are rejected");

        Check(!MapLabelLayoutPolicy.TryPlace(0f, 0f, 320f, 320f,
                40f, 40f, 100f, 20f, -1f, out _),
            "negative marker gaps are rejected");

        Console.WriteLine(_failed == 0
            ? "MapLabelLayoutPolicy: ALL CHECKS PASSED"
            : "MapLabelLayoutPolicy: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
