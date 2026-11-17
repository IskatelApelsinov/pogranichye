using System;
using Pogranichye.Client;

internal static class InputDeviceEventDebugStateCheck
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
        var state = new InputDeviceEventDebugState();
        Check(state.Describe(10, 1048576).Contains("события: нет"), "empty state is explicit");

        state.Record(1, "ZSC ODDOR-GEAR", 4 * 1024 * 1024, 10);
        state.Record(1, "ZSC ODDOR-GEAR", 512 * 1024, 10);
        state.Record(2, "USB Mouse", 256 * 1024, 10);
        var current = state.Describe(10, 1024 * 1024);
        Check(current.Contains("4.8 MiB / 3 событий"), "total bytes and event count are aggregated");
        Check(current.Contains("пик 4.0 MiB"), "largest individual event is retained");
        Check(current.Contains("ZSC ODDOR-GEAR") && current.Contains("USB Mouse"),
            "per-device traffic identifies the noisy HID");
        Check(current.Contains("лимит 1.0 MiB"), "current Input System budget is shown");

        var aged = state.Describe(12, 1024 * 1024);
        Check(aged.Contains("2 кадр. назад"), "last sample age is visible");

        state.Record(1, "ZSC ODDOR-GEAR", 100, 11);
        var nextFrame = state.Describe(11, 0);
        Check(nextFrame.Contains("100 B / 1 событий"), "new frame replaces stale traffic");
        Check(!nextFrame.Contains("USB Mouse"), "old device sample is cleared on a new frame");
        Check(nextFrame.Contains("лимит без ограничения"), "unlimited budget is described safely");

        state.Record(1, "ZSC ODDOR-GEAR", -10, 11);
        Check(state.Describe(11, 0).Contains("100 B / 2 событий"),
            "negative event sizes are clamped without losing the event count");

        state.Record(-1, "invalid", 999, 11);
        state.Record(1, "old frame", 999, 9);
        Check(state.Describe(11, 0).Contains("100 B / 2 событий"),
            "invalid device ids and out-of-order frames are ignored");

        state.Reset();
        Check(state.Describe(12, 1024).Contains("события: нет"), "reset clears device traffic");

        Console.WriteLine(_failed == 0
            ? "InputDeviceEventDebugState: ALL CHECKS PASSED"
            : "InputDeviceEventDebugState: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
