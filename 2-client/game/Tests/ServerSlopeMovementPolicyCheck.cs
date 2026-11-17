using System;
using Pogranichye.Client.Presentation;

internal static class ServerSlopeMovementPolicyCheck
{
    private static void Near(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.0000001d)
            throw new Exception(name + ": expected " + expected + ", got " + actual);
    }

    private static int Main()
    {
        const double up = 3.5d;
        const double down = 2d;
        const double easy = 0.25d;
        const double boost = 1.08d;
        const double cliff = 1.1d;

        Near(1d, ServerSlopeMovementPolicy.Multiplier(0d, up, down, easy, boost, cliff), "flat");
        Near(1d / (1d + up * 0.5d),
            ServerSlopeMovementPolicy.Multiplier(0.5d, up, down, easy, boost, cliff), "uphill");
        Near(1d + (boost - 1d) * 0.5d,
            ServerSlopeMovementPolicy.Multiplier(-0.125d, up, down, easy, boost, cliff), "easy downhill");
        Near(boost / (1d + down * 0.25d),
            ServerSlopeMovementPolicy.Multiplier(-0.5d, up, down, easy, boost, cliff), "steep downhill");
        Near(0d, ServerSlopeMovementPolicy.Multiplier(1.100001d, up, down, easy, boost, cliff), "cliff");
        Console.WriteLine("Server slope movement policy checks passed.");
        return 0;
    }
}
