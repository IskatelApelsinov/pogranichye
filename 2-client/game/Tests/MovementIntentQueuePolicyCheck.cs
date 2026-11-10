using System;
using System.Collections.Generic;
using Pogranichye.Client;

public static class MovementIntentQueuePolicyCheck
{
    private struct Intent
    {
        public long Sequence;
        public bool Moving;
        public Intent(long sequence, bool moving) { Sequence = sequence; Moving = moving; }
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
        queued.Enqueue(new Intent(2, true));
        queued.Enqueue(new Intent(3, true));
        queued.Enqueue(new Intent(4, true));
        queued.Enqueue(new Intent(5, true));
        var pending = new List<Intent>
        {
            new Intent(1, true), // the one packet already in flight
            new Intent(2, true), new Intent(3, true), new Intent(4, true), new Intent(5, true)
        };

        var trimmed = MovementIntentQueuePolicy.TrimOldest(
            queued, pending, intent => intent.Sequence, 2);
        Check(trimmed == 2, "queue cap discards only the oldest unsent intents");
        Check(queued.Count == 2 && queued.Peek().Sequence == 4,
            "queue keeps the newest direction samples in order");
        Check(pending.Count == 3 && HasSequence(pending, 1) &&
            HasSequence(pending, 4) && HasSequence(pending, 5),
            "prediction drops discarded packets but preserves the in-flight packet");

        queued.Enqueue(new Intent(6, false));
        pending.Add(new Intent(6, false));
        queued.Enqueue(new Intent(7, false));
        pending.Add(new Intent(7, false));
        var stopped = MovementIntentQueuePolicy.DiscardMovingOnStop(
            queued, pending, intent => intent.Sequence, intent => intent.Moving);
        Check(stopped == 3 && queued.Count == 1 && queued.Peek().Sequence == 7,
            "stop discards queued movement and keeps the newest zero-vector intent");
        Check(pending.Count == 2 && HasSequence(pending, 1) && HasSequence(pending, 7),
            "prediction preserves the in-flight packet and the stop signal");

        var repeatedStop = MovementIntentQueuePolicy.DiscardMovingOnStop(
            queued, pending, intent => intent.Sequence, intent => intent.Moving);
        Check(repeatedStop == 0 && queued.Count == 1 && pending.Count == 2,
            "repeated stop keeps the zero-vector packet queued for the in-flight send");

        queued.Enqueue(new Intent(8, true));
        pending.Add(new Intent(8, true));
        var resumedThenStopped = MovementIntentQueuePolicy.DiscardMovingOnStop(
            queued, pending, intent => intent.Sequence, intent => intent.Moving);
        Check(resumedThenStopped == 2 && queued.Count == 0,
            "a newer movement packet invalidates an older queued stop signal");
        Check(pending.Count == 1 && pending[0].Sequence == 1,
            "prediction removes superseded stop and movement packets");

        Console.WriteLine(_failed == 0
            ? "MovementIntentQueuePolicy: ALL CHECKS PASSED"
            : "MovementIntentQueuePolicy: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
