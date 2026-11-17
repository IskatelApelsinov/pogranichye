using System;

namespace Pogranichye.Client
{
    /// <summary>
    /// Keeps only the newest initial full snapshot during connection warm-up.
    /// The owning socket is part of the buffer identity: callbacks from a replaced
    /// connection must never seed or consume the next session's world state.
    /// </summary>
    public sealed class InitialFullFrameBuffer
    {
        private readonly object _gate = new object();
        private object _owner;
        private DateTime _deadlineUtc = DateTime.MinValue;
        private string _pendingFrame;

        public void Begin(object owner, DateTime deadlineUtc)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            lock (_gate)
            {
                _owner = owner;
                _deadlineUtc = deadlineUtc;
                _pendingFrame = null;
            }
        }

        /// <summary>
        /// Returns true when the frame was buffered and should not be processed yet.
        /// Once warm-up expires, a newly arriving frame supersedes and discards any
        /// older buffered snapshot, then proceeds through the normal processing path.
        /// </summary>
        public bool TryBuffer(object owner, string frame, DateTime nowUtc)
        {
            if (owner == null || string.IsNullOrEmpty(frame)) return false;
            lock (_gate)
            {
                if (!ReferenceEquals(_owner, owner) || _deadlineUtc == DateTime.MinValue)
                    return false;

                if (nowUtc >= _deadlineUtc)
                {
                    ClearLocked();
                    return false;
                }

                _pendingFrame = frame;
                return true;
            }
        }

        /// <summary>
        /// Takes the newest buffered frame after warm-up, but only for its owning socket.
        /// A mismatched active socket discards the stale frame instead of transferring it.
        /// </summary>
        public bool TryTake(object activeOwner, DateTime nowUtc, out string frame)
        {
            lock (_gate)
            {
                frame = null;
                if (_deadlineUtc == DateTime.MinValue || nowUtc < _deadlineUtc)
                    return false;

                var owner = _owner;
                var pending = _pendingFrame;
                ClearLocked();

                if (pending == null || !ReferenceEquals(owner, activeOwner))
                    return false;

                frame = pending;
                return true;
            }
        }

        public void End(object owner)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_owner, owner))
                    ClearLocked();
            }
        }

        private void ClearLocked()
        {
            _owner = null;
            _deadlineUtc = DateTime.MinValue;
            _pendingFrame = null;
        }
    }
}
