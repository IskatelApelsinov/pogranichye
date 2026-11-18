using System;
using Pogranichye.Client.Presentation;

internal static class WorldInteriorLightingPolicyCheck
{
    private static int _checks;

    private static void Main()
    {
        TavernUsesServerRoomCentreAndCeiling();
        OnlyTavernRoomsReceiveLight();
        RangeIsBounded();
        InvalidBoundsAreRejected();
        Console.WriteLine("World interior lighting policy: " + _checks + " checks passed.");
    }

    private static void TavernUsesServerRoomCentreAndCeiling()
    {
        WorldInteriorLightingPolicy.LightPlan plan;
        Assert(WorldInteriorLightingPolicy.TryResolve("tavern",
            100, 200, 10, 112, 208, 14, 96, 196, 8, out plan),
            "valid tavern room should receive a light");
        Near(plan.X, 10f, "local X follows server bounds centre");
        Near(plan.Z, 8f, "local Z follows server bounds centre");
        Near(plan.Y, 5.35f, "light hangs below server ceiling");
        Near(plan.Range, 6.6f, "range scales with room footprint");
        Near(plan.Intensity, 0.72f, "warm light intensity is controlled");
    }

    private static void OnlyTavernRoomsReceiveLight()
    {
        WorldInteriorLightingPolicy.LightPlan plan;
        Assert(!WorldInteriorLightingPolicy.TryResolve("cellar",
            0, 0, 0, 8, 8, 3, 0, 0, 0, out plan), "cellar remains unlit by this policy");
        Assert(!WorldInteriorLightingPolicy.TryResolve("tower",
            0, 0, 0, 8, 8, 3, 0, 0, 0, out plan), "tower remains unchanged");
        Assert(!WorldInteriorLightingPolicy.TryResolve("cave",
            0, 0, 0, 8, 8, 3, 0, 0, 0, out plan), "cave remains unchanged");
        Assert(WorldInteriorLightingPolicy.TryResolve("TAVERN",
            0, 0, 0, 8, 8, 3, 0, 0, 0, out plan), "kind matching is case-insensitive");
    }

    private static void RangeIsBounded()
    {
        WorldInteriorLightingPolicy.LightPlan plan;
        Assert(WorldInteriorLightingPolicy.TryResolve("tavern",
            0, 0, 0, 2, 2, 2, 0, 0, 0, out plan), "small valid tavern room");
        Near(plan.Range, 3f, "minimum range is clamped");
        Assert(WorldInteriorLightingPolicy.TryResolve("tavern",
            0, 0, 0, 40, 40, 4, 0, 0, 0, out plan), "large valid tavern room");
        Near(plan.Range, 7f, "maximum range is clamped");
    }

    private static void InvalidBoundsAreRejected()
    {
        WorldInteriorLightingPolicy.LightPlan plan;
        Assert(!WorldInteriorLightingPolicy.TryResolve("tavern",
            0, 0, 0, 1, 8, 3, 0, 0, 0, out plan), "too narrow room rejected");
        Assert(!WorldInteriorLightingPolicy.TryResolve("tavern",
            0, 0, 0, 8, 8, 1, 0, 0, 0, out plan), "too low room rejected");
        Assert(!WorldInteriorLightingPolicy.TryResolve("tavern",
            0, 0, 0, double.NaN, 8, 3, 0, 0, 0, out plan), "NaN rejected");
        Assert(!WorldInteriorLightingPolicy.TryResolve("tavern",
            0, 0, 0, double.PositiveInfinity, 8, 3, 0, 0, 0, out plan), "infinity rejected");
        Assert(!WorldInteriorLightingPolicy.TryResolve(null,
            0, 0, 0, 8, 8, 3, 0, 0, 0, out plan), "missing kind rejected");
    }

    private static void Near(float actual, float expected, string message)
    {
        Assert(Math.Abs(actual - expected) < 0.001f, message + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
}
