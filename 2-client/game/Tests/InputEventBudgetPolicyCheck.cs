using System;
using Pogranichye.Client;

internal static class InputEventBudgetPolicyCheck
{
    private static int _assertions;

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new Exception(message);
    }

    public static int Main()
    {
        Require(InputEventBudgetPolicy.MinimumSafeBytesPerUpdate == 8388608,
            "the minimum budget must be 8 MiB");

        Require(InputEventBudgetPolicy.Resolve(0, 1048576) == 0,
            "an explicit unlimited project setting must remain unlimited");
        Require(InputEventBudgetPolicy.Resolve(0, 8388608) == 0,
            "an explicit unlimited setting must not be replaced by the default");

        Require(InputEventBudgetPolicy.Resolve(5000000, 1048576) == 8388608,
            "the previous 5 MB default must be raised above the observed 5.2 MB HID burst");
        Require(InputEventBudgetPolicy.Resolve(5242944, 1048576) == 8388608,
            "the observed 5 MiB aggregate burst must fit below the configured minimum");
        Require(InputEventBudgetPolicy.Resolve(8388608, 8388608) == 8388608,
            "the configured minimum must remain unchanged");
        Require(InputEventBudgetPolicy.Resolve(16777216, 8388608) == 16777216,
            "a larger project-level budget must never be reduced");
        Require(InputEventBudgetPolicy.Resolve(5000000, 12582912) == 12582912,
            "an explicitly configured higher minimum must be respected");
        Require(InputEventBudgetPolicy.Resolve(-1, 8388608) == -1,
            "non-positive sentinels must not be rewritten");

        Console.WriteLine("InputEventBudgetPolicy: " + _assertions + " checks passed");
        return 0;
    }
}
