using System;
using System.Collections.Generic;

namespace Pogranichye.Client
{
    /// <summary>
    /// Policy for unsent client movement packets. The server accepts monotonically increasing
    /// sequence numbers rather than requiring a contiguous sequence, so discarded unsent inputs
    /// can safely leave gaps without changing the wire contract.
    /// </summary>
    public static class MovementIntentQueuePolicy
    {
        public static int TrimOldest<T>(Queue<T> queued, List<T> pending,
            Func<T, long> getSequence, int maxQueued)
        {
            if (queued == null || pending == null || getSequence == null)
                return 0;

            if (maxQueued < 0) maxQueued = 0;
            var discarded = new HashSet<long>();
            while (queued.Count > maxQueued)
                discarded.Add(getSequence(queued.Dequeue()));

            RemovePending(pending, discarded, getSequence);
            return discarded.Count;
        }

        public static int DiscardQueuedOnStop<T>(Queue<T> queued, List<T> pending,
            Func<T, long> getSequence)
        {
            if (queued == null || pending == null || getSequence == null)
                return 0;

            var discarded = new HashSet<long>();
            while (queued.Count > 0)
                discarded.Add(getSequence(queued.Dequeue()));

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
