using System;
using System.Collections.Generic;

namespace Pogranichye.Client
{
    /// <summary>
    /// Bounds unsent movement packets and removes stale movement when input returns to neutral.
    /// Sequence gaps are valid: the authoritative server accepts monotonically increasing seq.
    /// </summary>
    public static class MovementIntentQueuePolicy
    {
        public static int TrimOldest<T>(Queue<T> queued, List<T> pending,
            Func<T, long> getSequence, int maxQueued)
        {
            if (queued == null || pending == null || getSequence == null) return 0;
            if (maxQueued < 0) maxQueued = 0;
            var discarded = new HashSet<long>();
            while (queued.Count > maxQueued)
                discarded.Add(getSequence(queued.Dequeue()));
            RemovePending(pending, discarded, getSequence);
            return discarded.Count;
        }

        public static int DiscardMovingOnStop<T>(Queue<T> queued, List<T> pending,
            Func<T, long> getSequence, Func<T, bool> hasMovement)
        {
            if (queued == null || pending == null || getSequence == null || hasMovement == null) return 0;
            var discarded = new HashSet<long>();
            var latestStop = default(T);
            var hasLatestStop = false;
            while (queued.Count > 0)
            {
                var intent = queued.Dequeue();
                if (hasMovement(intent))
                {
                    if (hasLatestStop) discarded.Add(getSequence(latestStop));
                    discarded.Add(getSequence(intent));
                    hasLatestStop = false;
                }
                else
                {
                    if (hasLatestStop) discarded.Add(getSequence(latestStop));
                    latestStop = intent;
                    hasLatestStop = true;
                }
            }
            // Keep the newest stop packet; it follows the in-flight packet and halts the server.
            if (hasLatestStop) queued.Enqueue(latestStop);
            RemovePending(pending, discarded, getSequence);
            return discarded.Count;
        }

        private static void RemovePending<T>(List<T> pending, HashSet<long> discarded,
            Func<T, long> getSequence)
        {
            if (discarded.Count == 0) return;
            pending.RemoveAll(intent => discarded.Contains(getSequence(intent)));
        }
    }
}
