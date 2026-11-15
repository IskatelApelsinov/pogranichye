using System;
using Pogranichye.Client.World;

internal static class WorldOriginStateCheck
{
    private static int _checks;
    private static void True(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }
    private static void Equal<T>(T expected, T actual, string label)
    {
        _checks++;
        if (!Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
    private static void Near(double expected, double actual, double epsilon, string label)
    {
        _checks++;
        if (Math.Abs(expected - actual) > epsilon) throw new Exception(label + ": expected " + expected + ", got " + actual);
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
        var state = new WorldOriginState();
        Throws<InvalidOperationException>(() => { var ignored = state.Origin; }, "origin must be initialized");
        state.Initialize(new WorldOriginPosition(0, 1000000d, -2000000d, 128d));
        var world = new WorldOriginPosition(0, 1000012.25d, -1999991.5d, 130.75d);
        True(state.TryToLocal(world, out var local), "large global coordinate converts");
        Near(12.25d, local.X, 1e-9, "east local precision");
        Near(2.75d, local.Y, 1e-9, "up local precision");
        Near(8.5d, local.Z, 1e-9, "north local precision");
        True(state.TryToGlobal(local, out var roundTrip), "local converts back");
        Near(world.East, roundTrip.East, 1e-9, "east round trip");
        Near(world.North, roundTrip.North, 1e-9, "north round trip");
        Near(world.Up, roundTrip.Up, 1e-9, "up round trip");

        var generation = state.Generation;
        True(!state.TryRebase(new WorldOriginPosition(0, 1000100d, -1999900d, 128d), out _),
            "small movement does not rebase");
        Equal(generation, state.Generation, "generation unchanged without rebase");

        True(state.TryRebase(new WorldOriginPosition(0, 1002100d, -1999900d, 200d),
            2048d, 256d, 64d, out var shift), "large movement rebases");
        Near(1001984d, shift.Current.East, 1e-9, "east snaps to 256m grid");
        Near(-1999872d, shift.Current.North, 1e-9, "north snaps to 256m grid");
        Near(192d, shift.Current.Up, 1e-9, "up snaps to 64m grid");
        Equal(generation + 1, state.Generation, "generation increments once");
        True(state.TryToLocal(new WorldOriginPosition(0, 1002120d, -1999870d, 193d), out local),
            "position converts after rebase");
        Near(136d, local.X, 1e-9, "post-rebase east");
        Near(1d, local.Y, 1e-9, "post-rebase up");
        Near(2d, local.Z, 1e-9, "post-rebase north");

        True(!state.TryToLocal(new WorldOriginPosition(1, 0, 0, 0), out local),
            "different world frame rejected");
        True(!state.TryRebase(new WorldOriginPosition(1, 0, 0, 0), out shift),
            "different frame cannot rebase");
        Throws<ArgumentOutOfRangeException>(() => {
            WorldOriginShift ignored;
            state.TryRebase(new WorldOriginPosition(0, 0, 0, 0), 0, 256, 64, out ignored);
        }, "zero threshold rejected");

        Console.WriteLine("World origin state checks passed: " + _checks);
        return 0;
    }
}