using System;
using System.Collections.Generic;
using Pogranichye.Client;

public static class MovementIntentQueuePolicyCheck
{
    private sealed class Intent
    {
        public long Sequence;
        public float X;
        public float Y;
    }

    private static long Seq(Intent i) { return i.Sequence; }
    private static bool Moving(Intent i) { return i.X * i.X + i.Y * i.Y > 0.000001f; }
    private static Intent Item(long seq, float x, float y) { return new Intent { Sequence=seq, X=x, Y=y }; }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Main()
    {
        TrimsOldestAndPredictionEntries();
        StopDropsQueuedMovementButKeepsNewestStop();
        NewMovementAfterStopIsNotDiscarded();
        Console.WriteLine("MovementIntentQueuePolicy: 3 checks passed");
    }

    private static void TrimsOldestAndPredictionEntries()
    {
        var q = new Queue<Intent>();
        var pending = new List<Intent>();
        for (var i=1; i<=5; i++) { var x=Item(i,1,0); q.Enqueue(x); pending.Add(x); }
        var removed = MovementIntentQueuePolicy.TrimOldest(q,pending,Seq,3);
        Assert(removed==2 && q.Count==3 && pending.Count==3,"trim must remove oldest queued and predicted inputs");
        Assert(q.Peek().Sequence==3,"newest movement tail must remain");
    }

    private static void StopDropsQueuedMovementButKeepsNewestStop()
    {
        var q = new Queue<Intent>();
        var pending = new List<Intent>();
        foreach(var x in new[]{Item(1,1,0),Item(2,1,0),Item(3,0,0),Item(4,0,0)}){q.Enqueue(x);pending.Add(x);}
        var removed=MovementIntentQueuePolicy.DiscardMovingOnStop(q,pending,Seq,Moving);
        Assert(removed==3,"two movement packets and stale stop must be discarded");
        Assert(q.Count==1 && q.Peek().Sequence==4,"newest stop must survive");
        Assert(pending.Count==1 && pending[0].Sequence==4,"prediction must match surviving queue");
    }

    private static void NewMovementAfterStopIsNotDiscarded()
    {
        var q = new Queue<Intent>();
        var pending = new List<Intent>();
        foreach(var x in new[]{Item(1,0,0),Item(2,1,0),Item(3,0,1)}){q.Enqueue(x);pending.Add(x);}
        var removed=MovementIntentQueuePolicy.DiscardMovingOnStop(q,pending,Seq,Moving);
        Assert(removed==3,"stops before later movement and prior movement must be discarded");
        Assert(q.Count==0 && pending.Count==0,"a stale stop must not remain ahead of fresh movement");
    }
}
