using System;
using System.Collections.Generic;
using Pogranichye.Client;

public static class MovementIntentQueuePolicyCheck
{
    private struct Intent
    {
        public long Sequence;
        public Intent(long sequence) { Sequence = sequence; }
    }

    private static int _failed;
    private static void Check(bool ok, string label)
    {
        if (ok) Console.WriteLine("ok:   " + label);
        else { _failed++; Console.WriteLine("FAIL: " + label); }
    }

    private static bool HasSequence(List<Intent> intents, long sequence)
    {
        foreach (var intent in intents)
            if (intent.Sequence == sequence) return true;
        return false;
    }

    public static int Main()
    {
        var queued = new Queue<Intent>();
        queued.Enqueue(new Intent(2));
        queued.Enqueue(new Intent(3));
        queued.Enqueue(new Intent(4));
        queued.Enqueue(new Intent(5));
        var pending = new List<Intent>
        {
            new Intent(1), // the one packet already in flight
            new Intent(2), new Intent(3), new Intent(4), new Intent(5)
        };

        var trimmed = MovementIntentQueuePolicy.TrimOldest(
            queued, pending, intent => intent.Sequence, 2);
        Check(trimmed == 2, "queue cap discards only the oldest unsent intents");
        Check(queued.Count == 2 && queued.Peek().Sequence == 4,
            "queue keeps the newest direction samples in order");
        Check(pending.Count == 3 && HasSequence(pending, 1) &&
            HasSequence(pending, 4) && HasSequence(pending, 5),
            "prediction drops discarded packets but preserves the in-flight packet");

        var stopped = MovementIntentQueuePolicy.DiscardQueuedOnStop(
            queued, pending, intent => intent.Sequence);
        Check(stopped == 2 && queued.Count == 0,
            "releasing movement clears every unsent packet");
        Check(pending.Count == 1 && pending[0].Sequence == 1,
            "stop keeps only the packet that cannot be cancelled");

        var empty = MovementIntentQueuePolicy.DiscardQueuedOnStop(
            queued, pending, intent => intent.Sequence);
        Check(empty == 0 && pending.Count == 1,
            "repeated stop is idempotent");

        Console.WriteLine(_failed == 0
            ? "MovementIntentQueuePolicy: ALL CHECKS PASSED"
            : "MovementIntentQueuePolicy: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
