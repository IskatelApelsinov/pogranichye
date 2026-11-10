// Standalone check of RequestGate (pure C#, no Unity). This is NOT a Unity build or a Unity test run.
//   mcs -out:gate.exe Tests/RequestGateCheck.cs Assets/_Project/Scripts/Runtime/RequestGate.cs && mono gate.exe
using System;
using System.Collections.Generic;
using Pogranichye.Client;

public static class RequestGateCheck
{
    private static int _failed;
    private static void Check(bool ok, string what) { if (!ok) { _failed++; Console.WriteLine("FAIL: " + what); } else Console.WriteLine("ok:   " + what); }

    public static int Main()
    {
        var sent = new List<string>();
        var gate = new RequestGate(p => sent.Add((string)p["reqid"]), 6f, 3);
        Func<string, Dictionary<string, object>> pay = id => new Dictionary<string, object> { { "reqid", id } };
        RequestGate.Item item;

        // two quick requests: only the first goes on the wire
        gate.Enqueue("A", "a", pay("a"), 0f);
        gate.Enqueue("B", "b", pay("b"), 0.1f);
        Check(sent.Count == 1 && sent[0] == "a", "second request waits while the first is in flight");

        // an unrelated or stale result does not resolve anything (no false success)
        Check(gate.OnResult("zzz", true, 0.2f, out item) == RequestGate.Outcome.None && gate.Busy, "foreign reqid is ignored");
        Check(gate.OnResult("b", true, 0.2f, out item) == RequestGate.Outcome.None, "result for a queued (unsent) request is ignored");

        // matching ACK resolves A and only then B is sent
        Check(gate.OnResult("a", true, 0.3f, out item) == RequestGate.Outcome.Accepted && item.Label == "A", "matching reqid resolves the in-flight request");
        Check(sent.Count == 2 && sent[1] == "b", "next request is sent only after the matching ACK");
        // the same result seen again in the next frame (full + fast) must not resolve B
        Check(gate.OnResult("a", true, 0.4f, out item) == RequestGate.Outcome.None && gate.Busy, "repeated old result does not resolve the next request");
        Check(gate.OnResult("b", false, 0.5f, out item) == RequestGate.Outcome.Rejected && item.Label == "B", "rejection is delivered to its own request");
        Check(!gate.Busy && gate.Waiting == 0, "both acknowledged, none lost, no false timeout");
        Check(gate.Tick(100f, out item) == RequestGate.Outcome.None, "idle gate never reports a timeout");

        // timeout frees the gate; a late ACK of the expired request is not shown as someone else's success
        gate.Enqueue("C", "c", pay("c"), 10f);
        gate.Enqueue("D", "d", pay("d"), 10f);
        Check(gate.Tick(15f, out item) == RequestGate.Outcome.None, "no timeout before the deadline");
        Check(gate.Tick(16.5f, out item) == RequestGate.Outcome.TimedOut && item.Label == "C", "in-flight request times out");
        Check(sent[sent.Count - 1] == "d", "queue advances after a timeout");
        Check(gate.OnResult("c", true, 17f, out item) == RequestGate.Outcome.None, "late ACK of an expired request is ignored");
        Check(gate.OnResult("d", true, 17f, out item) == RequestGate.Outcome.Accepted, "the next request still gets its own ACK");

        // capacity and reset
        gate.Enqueue("E", "e", pay("e"), 20f);
        Check(gate.Enqueue("F", "f", pay("f"), 20f) && gate.Enqueue("G", "g", pay("g"), 20f) && gate.Enqueue("H", "h", pay("h"), 20f), "queue accepts up to capacity");
        Check(!gate.Enqueue("I", "i", pay("i"), 20f), "overflow is refused instead of silently dropped later");
        var before = sent.Count;
        gate.Clear();
        Check(!gate.Busy && gate.Waiting == 0 && gate.Tick(999f, out item) == RequestGate.Outcome.None && sent.Count == before, "Clear drops in-flight and queued requests (disconnect)");

        Console.WriteLine(_failed == 0 ? "RequestGate: ALL CHECKS PASSED" : "RequestGate: FAILED " + _failed);
        return _failed == 0 ? 0 : 1;
    }
}
