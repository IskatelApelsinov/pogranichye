using System;
using Pogranichye.Client;

internal static class InitialFullFrameBufferCheck
{
    private static int _failures;
    private static int _checks;

    private static void Check(string name, bool condition)
    {
        _checks++;
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        var start = new DateTime(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc);
        var socketA = new object();
        var socketB = new object();
        var buffer = new InitialFullFrameBuffer();
        string frame;

        buffer.Begin(socketA, start.AddSeconds(5));
        Check("current socket buffers its first snapshot",
            buffer.TryBuffer(socketA, "frame-1", start.AddSeconds(1)));
        Check("warm-up keeps only the newest snapshot",
            buffer.TryBuffer(socketA, "frame-2", start.AddSeconds(2)));
        Check("snapshot is not released before the deadline",
            !buffer.TryTake(socketA, start.AddSeconds(4), out frame) && frame == null);
        Check("deadline releases the newest snapshot",
            buffer.TryTake(socketA, start.AddSeconds(5), out frame) && frame == "frame-2");
        Check("released snapshot cannot be consumed twice",
            !buffer.TryTake(socketA, start.AddSeconds(6), out frame));

        buffer.Begin(socketB, start.AddSeconds(10));
        Check("late callback from replaced socket is not buffered",
            !buffer.TryBuffer(socketA, "stale-old-session", start.AddSeconds(6)));
        Check("replacement socket can buffer its own snapshot",
            buffer.TryBuffer(socketB, "new-session", start.AddSeconds(7)));
        Check("old active socket cannot consume replacement snapshot",
            !buffer.TryTake(socketA, start.AddSeconds(10), out frame) && frame == null);
        Check("owner mismatch discards rather than transfers the stale snapshot",
            !buffer.TryTake(socketB, start.AddSeconds(11), out frame));

        buffer.Begin(socketA, start.AddSeconds(15));
        Check("snapshot is buffered before warm-up expires",
            buffer.TryBuffer(socketA, "older-buffered", start.AddSeconds(14)));
        Check("first post-deadline snapshot passes through immediately",
            !buffer.TryBuffer(socketA, "newer-live", start.AddSeconds(15)));
        Check("expired buffered snapshot is not later replayed",
            !buffer.TryTake(socketA, start.AddSeconds(16), out frame));

        buffer.Begin(socketA, start.AddSeconds(20));
        Check("buffer accepts a snapshot before explicit close",
            buffer.TryBuffer(socketA, "closing-session", start.AddSeconds(19)));
        buffer.End(socketA);
        Check("ending the owning socket clears its pending snapshot",
            !buffer.TryTake(socketA, start.AddSeconds(21), out frame));

        buffer.Begin(socketA, start.AddSeconds(25));
        Check("starting a new session resets the previous pending snapshot",
            !buffer.TryTake(socketB, start.AddSeconds(26), out frame));

        Console.WriteLine(_failures == 0
            ? "InitialFullFrameBuffer: " + _checks + " checks passed"
            : "InitialFullFrameBuffer: FAILED " + _failures + " of " + _checks);
        return _failures == 0 ? 0 : 1;
    }
}
