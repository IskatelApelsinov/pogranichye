using System;
using System.Collections.Generic;

namespace Pogranichye.Client
{
    /// <summary>
    /// Serialises result-bearing intents. The server reports outcomes in a single slot (me.result holds only
    /// the latest reqid), so two requests sent back to back could overwrite each other's acknowledgement and
    /// the client would show a false timeout. The gate keeps exactly one request in flight and sends the next
    /// one only after the matching reqid came back (or the wait timed out). Pure logic, no Unity types:
    /// it can be exercised outside the Editor.
    /// </summary>
    public sealed class RequestGate
    {
        public sealed class Item
        {
            public string ReqId;
            public string Label;
            public Dictionary<string, object> Payload;
            public float SentAt;
        }

        public enum Outcome { None, Accepted, Rejected, TimedOut }

        private readonly Queue<Item> _queue = new Queue<Item>();
        private readonly Action<Dictionary<string, object>> _send;
        private readonly float _timeout;
        private readonly int _capacity;
        private Item _inFlight;

        public RequestGate(Action<Dictionary<string, object>> send, float timeoutSeconds, int capacity)
        {
            if (send == null) throw new ArgumentNullException("send");
            _send = send;
            _timeout = timeoutSeconds;
            _capacity = capacity < 1 ? 1 : capacity;
        }

        public bool Busy { get { return _inFlight != null; } }
        public int Waiting { get { return _queue.Count; } }
        public string InFlightLabel { get { return _inFlight == null ? null : _inFlight.Label; } }

        /// <summary>Queues a request. Returns false (nothing is sent) when the queue is full.</summary>
        public bool Enqueue(string label, string reqId, Dictionary<string, object> payload, float now)
        {
            if (string.IsNullOrEmpty(reqId) || payload == null) return false;
            if (_queue.Count >= _capacity) return false;
            _queue.Enqueue(new Item { ReqId = reqId, Label = label, Payload = payload });
            Pump(now);
            return true;
        }

        /// <summary>
        /// Feed every observed me.result here. Only the in-flight reqid resolves; anything else (an older
        /// request that already timed out, a result repeated in the next frame) is ignored, so a stale
        /// acknowledgement can never be shown as the success of a different action.
        /// </summary>
        public Outcome OnResult(string reqId, bool ok, float now, out Item resolved)
        {
            resolved = null;
            if (_inFlight == null || string.IsNullOrEmpty(reqId) || reqId != _inFlight.ReqId) return Outcome.None;
            resolved = _inFlight;
            _inFlight = null;
            Pump(now);
            return ok ? Outcome.Accepted : Outcome.Rejected;
        }

        /// <summary>Call every frame: expires the in-flight request and lets the next one go.</summary>
        public Outcome Tick(float now, out Item expired)
        {
            expired = null;
            if (_inFlight == null || now - _inFlight.SentAt <= _timeout) return Outcome.None;
            expired = _inFlight;
            _inFlight = null;
            Pump(now);
            return Outcome.TimedOut;
        }

        public void Clear()
        {
            _queue.Clear();
            _inFlight = null;
        }

        private void Pump(float now)
        {
            if (_inFlight != null || _queue.Count == 0) return;
            _inFlight = _queue.Dequeue();
            _inFlight.SentAt = now;
            _send(_inFlight.Payload);
        }
    }
}
