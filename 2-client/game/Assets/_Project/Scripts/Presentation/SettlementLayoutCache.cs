using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Small bounded cache of nearby settlement presentations. Layout geometry stays local to
    /// its canonical anchor; roots are reprojected against the player's current settlement anchor.
    /// This is presentation streaming only and does not own terrain chunks or server entities.
    /// </summary>
    public sealed class SettlementLayoutCache : IDisposable
    {
        private sealed class Entry
        {
            public string Id;
            public Transform Root;
            public WorldLayoutFrame Layout;
            public float LastUsed;
        }

        private const int Capacity = 3;
        private const double VisibleDistance = 700d;
        private readonly Transform _parent;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _disposed;

        public SettlementLayoutCache(Transform parent)
        {
            _parent = parent;
        }

        public bool TryGet(string settlementId, WorldFrame frame, out WorldLayoutFrame layout)
        {
            layout = null;
            if (_disposed || string.IsNullOrEmpty(settlementId) ||
                !_entries.TryGetValue(settlementId, out var entry))
                return false;

            entry.LastUsed = Time.unscaledTime;
            layout = entry.Layout;
            Update(frame);
            return true;
        }

        public bool Store(string settlementId, WorldLayoutFrame layout, WorldFrame frame)
        {
            if (_disposed || _parent == null || string.IsNullOrEmpty(settlementId) ||
                layout == null || layout.Version != 2 || layout.Anchor == null)
                return false;

            if (_entries.TryGetValue(settlementId, out var existing))
            {
                existing.LastUsed = Time.unscaledTime;
                existing.Layout = layout;
                WorldLayoutPresentationBuilder.Rebuild(existing.Root, layout);
                Update(frame);
                return true;
            }

            var root = new GameObject("SettlementLayout_" + settlementId).transform;
            root.SetParent(_parent, false);
            try
            {
                WorldLayoutPresentationBuilder.Build(root, layout);
                var entry = new Entry
                {
                    Id = settlementId,
                    Root = root,
                    Layout = layout,
                    LastUsed = Time.unscaledTime
                };
                _entries.Add(settlementId, entry);
                EvictLeastRecentlyUsed(settlementId);
                Update(frame);
                return true;
            }
            catch (Exception exception)
            {
                WorldLayoutPresentationBuilder.Cleanup(root);
                UnityEngine.Object.Destroy(root.gameObject);
                Debug.LogWarning("Не удалось собрать кэш поселения " + settlementId + ": " + exception.Message);
                return false;
            }
        }

        public void Update(WorldFrame frame)
        {
            if (_disposed || frame?.Place?.Anchor == null) return;
            var current = frame.Place.Anchor;
            foreach (var entry in _entries.Values)
            {
                var anchor = entry.Layout?.Anchor;
                if (anchor == null || anchor.Frame != current.Frame)
                {
                    WorldLayoutPresentationBuilder.SetVisible(entry.Root, false);
                    continue;
                }

                var east = anchor.East - current.East;
                var north = anchor.North - current.North;
                var up = anchor.Up - current.Up;
                var distanceSquared = east * east + north * north;
                if (distanceSquared > VisibleDistance * VisibleDistance)
                {
                    WorldLayoutPresentationBuilder.SetVisible(entry.Root, false);
                    continue;
                }

                WorldLayoutPresentationBuilder.SetPresentationOffset(entry.Root,
                    new Vector3((float)east, (float)up, (float)north));
                WorldLayoutPresentationBuilder.SetVisible(entry.Root, true);
            }
        }

        private void EvictLeastRecentlyUsed(string keepId)
        {
            while (_entries.Count > Capacity)
            {
                Entry oldest = null;
                foreach (var entry in _entries.Values)
                {
                    if (entry.Id == keepId) continue;
                    if (oldest == null || entry.LastUsed < oldest.LastUsed)
                        oldest = entry;
                }
                if (oldest == null) return;
                _entries.Remove(oldest.Id);
                WorldLayoutPresentationBuilder.Cleanup(oldest.Root);
                if (oldest.Root != null) UnityEngine.Object.Destroy(oldest.Root.gameObject);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                WorldLayoutPresentationBuilder.Cleanup(entry.Root);
                if (entry.Root != null) UnityEngine.Object.Destroy(entry.Root.gameObject);
            }
            _entries.Clear();
        }
    }
}
