using System;
using Pogranichye.Client;

internal static class InputEventDebugStateCheck
{
    private static int _failed;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        Console.Error.WriteLine("FAIL " + name);
        _failed++;
    }

    public static int Main()
    {
        var state = new InputEventDebugState();
        Check(state.Describe(1f) == "Последнее: нет", "empty state is explicit");

        state.Record("Move", "W", "Performed", 10f);
        var movement = state.Describe(10.26f);
        Check(movement.Contains("Move/W/Performed"), "physical key and action phase are retained");
        Check(movement.Contains("0.3с назад"), "event age is formatted invariantly");

        state.Record("Primary", "Left Button", "Started", 10.5f);
        var mouse = state.Describe(10.5f);
        Check(mouse.Contains("Primary/Left Button/Started"), "mouse button replaces older event");
        Check(!mouse.Contains("Move/W"), "only the latest event is displayed");

        state.Record(null, null, null, 12f);
        var missing = state.Describe(11f);
        Check(missing.Contains("?/?/?"), "missing action metadata is safe");
        Check(missing.Contains("0.0с назад"), "future timestamps do not produce negative age");

        Console.WriteLine(_failed == 0
            ? "InputEventDebugState: ALL CHECKS PASSED"
            : "InputEventDebugState: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
