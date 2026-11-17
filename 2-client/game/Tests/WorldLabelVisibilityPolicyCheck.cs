using System;
using Pogranichye.Client.Presentation;

internal static class WorldLabelVisibilityPolicyCheck
{
    private static int _checks;

    private static void Expect(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        Expect(WorldLabelVisibilityPolicy.OccluderLayer == 2, "Occluders must use the reserved Ignore Raycast layer.");
        Expect(WorldLabelVisibilityPolicy.OccluderMask == 4, "The explicit occluder mask must contain only layer 2.");
        Expect(WorldLabelVisibilityPolicy.SignMaxDistance == 24f, "Physical signs must use the short reading range.");
        Expect(WorldLabelVisibilityPolicy.BillboardMaxDistance == 45f, "World nameplates must not span the whole settlement.");
        Expect(WorldLabelVisibilityPolicy.IsWithinDistance(0f, 24f), "A label at the camera origin is within range.");
        Expect(WorldLabelVisibilityPolicy.IsWithinDistance(24f, 24f), "The exact sign range boundary is inclusive.");
        Expect(!WorldLabelVisibilityPolicy.IsWithinDistance(24.01f, 24f), "A sign beyond its range must be hidden.");
        Expect(!WorldLabelVisibilityPolicy.IsWithinDistance(-0.1f, 24f), "Negative distances are invalid.");
        Expect(!WorldLabelVisibilityPolicy.IsWithinDistance(float.NaN, 24f), "NaN distance must be rejected.");
        Expect(!WorldLabelVisibilityPolicy.IsWithinDistance(1f, float.PositiveInfinity), "Infinite maximum distance must be rejected.");
        Expect(WorldLabelVisibilityPolicy.IsFacing(0.06f), "A clearly outward-facing label should be visible.");
        Expect(!WorldLabelVisibilityPolicy.IsFacing(0.05f), "The facing threshold is strict.");
        Expect(!WorldLabelVisibilityPolicy.IsFacing(float.NaN), "NaN facing must be rejected.");
        Console.WriteLine("World label visibility policy checks passed: " + _checks);
        return 0;
    }
}
