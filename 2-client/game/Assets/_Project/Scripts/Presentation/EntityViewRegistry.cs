using System.Collections.Generic;
using Pogranichye.Client;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    public sealed class EntityViewRegistry
    {
        private readonly Dictionary<ulong, GameObject> _views = new Dictionary<ulong, GameObject>();
        private readonly Transform _root;

        public EntityViewRegistry(Transform root) { _root = root; }

        public void Apply(WorldFrame frame)
        {
            var active = new HashSet<ulong>();
            var playerId = WorldFrameParser.ParseEntityId(frame.Me.Id, "me.id");
            active.Add(playerId);
            var player = GetOrCreate(playerId, "Player", new Vector3(0, 1, 0), true);
            SetLabel(player, $"{frame.Me.Name} {frame.Me.Surname}\n{frame.Me.Role}\n{frame.Me.Last}");

            if (frame.Place?.Neighbors != null)
            {
                var count = frame.Place.Neighbors.Count;
                for (var i = 0; i < count; i++)
                {
                    var neighbor = frame.Place.Neighbors[i];
                    var id = WorldFrameParser.ParseEntityId(neighbor.Id, $"neighbors[{i}].id");
                    active.Add(id);
                    var angle = Mathf.PI * 2f * i / Mathf.Max(1, count);
                    var position = new Vector3(Mathf.Cos(angle) * 3.2f, 1, Mathf.Sin(angle) * 3.2f);
                    var view = GetOrCreate(id, neighbor.Name, position, false);
                    SetLabel(view, $"{neighbor.Name}\n{neighbor.Role}\n{neighbor.Act}");
                    view.transform.position = position;
                }
            }

            var stale = new List<ulong>();
            foreach (var pair in _views)
                if (!active.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var id in stale)
            {
                Object.Destroy(_views[id]);
                _views.Remove(id);
            }
        }

        public void Clear()
        {
            foreach (var view in _views.Values) Object.Destroy(view);
            _views.Clear();
        }

        private GameObject GetOrCreate(ulong id, string name, Vector3 position, bool isPlayer)
        {
            if (_views.TryGetValue(id, out var existing)) return existing;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"EntityView_{id}_{name}";
            go.transform.SetParent(_root, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.7f, 1f, 0.7f);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(go.transform, false);
            labelObject.transform.localPosition = new Vector3(0, 1.25f, 0);
            var text = labelObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 36;
            text.characterSize = 0.08f;
            text.color = Color.white;

            VerticalSliceController.ApplyUrpMaterial(
                go.GetComponent<Renderer>(),
                isPlayer ? new Color(0.25f, 0.55f, 0.8f) : new Color(0.55f, 0.55f, 0.48f));
            _views.Add(id, go);
            return go;
        }

        private static void SetLabel(GameObject go, string value)
        {
            var text = go.GetComponentInChildren<TextMesh>();
            if (text != null) text.text = value ?? string.Empty;
        }
    }
}
