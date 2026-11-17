using System;
using System.Collections;
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

        /// <summary>
        /// Returns true when the same visible action and payload are already in flight or queued.
        /// reqid is deliberately ignored because every retry receives a new identifier.
        /// </summary>
        public bool ContainsDuplicate(string label, Dictionary<string, object> payload)
        {
            if (payload == null) return false;
            if (_inFlight != null && SameRequest(_inFlight, label, payload)) return true;
            foreach (var queued in _queue)
                if (SameRequest(queued, label, payload)) return true;
            return false;
        }

        /// <summary>Queues a request. Identical pending actions and queue overflow are refused.</summary>
        public bool Enqueue(string label, string reqId, Dictionary<string, object> payload, float now)
        {
            if (string.IsNullOrEmpty(reqId) || payload == null) return false;
            if (ContainsDuplicate(label, payload)) return false;
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

        private static bool SameRequest(Item pending, string label, Dictionary<string, object> payload)
        {
            if (pending == null || !string.Equals(pending.Label, label, StringComparison.Ordinal) ||
                pending.Payload == null || ComparableFieldCount(pending.Payload) != ComparableFieldCount(payload))
                return false;

            foreach (var pair in pending.Payload)
            {
                if (string.Equals(pair.Key, "reqid", StringComparison.Ordinal)) continue;
                if (!payload.TryGetValue(pair.Key, out var other) || !ValuesEqual(pair.Value, other))
                    return false;
            }

            foreach (var pair in payload)
            {
                if (string.Equals(pair.Key, "reqid", StringComparison.Ordinal)) continue;
                if (!pending.Payload.ContainsKey(pair.Key)) return false;
            }
            return true;
        }

        private static int ComparableFieldCount(Dictionary<string, object> payload)
        {
            var count = 0;
            foreach (var key in payload.Keys)
                if (!string.Equals(key, "reqid", StringComparison.Ordinal)) count++;
            return count;
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            var leftMap = left as IDictionary;
            var rightMap = right as IDictionary;
            if (leftMap != null || rightMap != null)
            {
                if (leftMap == null || rightMap == null || leftMap.Count != rightMap.Count) return false;
                foreach (DictionaryEntry entry in leftMap)
                    if (!rightMap.Contains(entry.Key) || !ValuesEqual(entry.Value, rightMap[entry.Key]))
                        return false;
                return true;
            }

            var leftList = left as IList;
            var rightList = right as IList;
            if (leftList != null || rightList != null)
            {
                if (leftList == null || rightList == null || leftList.Count != rightList.Count) return false;
                for (var i = 0; i < leftList.Count; i++)
                    if (!ValuesEqual(leftList[i], rightList[i])) return false;
                return true;
            }

            return left.Equals(right);
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
