using System.Collections.Generic;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Owns temporary client-side representations of the player and nearby NPCs.
    /// IDs are server entity IDs; positions are presentation-only, not world coordinates.
    /// </summary>
    public sealed class EntityViewRegistry
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Dictionary<ulong, GameObject> _views = new Dictionary<ulong, GameObject>();
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private readonly Transform _root;

        public Transform PlayerTransform { get; private set; }

        public EntityViewRegistry(Transform root) { _root = root; }

        public void Apply(WorldFrame frame)
        {
            if (frame == null || !frame.Alive || frame.Me == null) return;

            var active = new HashSet<ulong>();
            var playerId = WorldFrameParser.ParseEntityId(frame.Me.Id, "me.id");
            active.Add(playerId);

            var player = GetOrCreate(playerId, "Player", Vector3.zero, true);
            PlayerTransform = player.transform;
            SetLabel(player, $"{frame.Me.Name} {frame.Me.Surname}\n{frame.Me.Role}\n{frame.Me.Last}");

            var neighbors = frame.Place?.Neighbors;
            if (neighbors != null)
            {
                var visibleCount = 0;
                for (var i = 0; i < neighbors.Count; i++)
                {
                    var neighbor = neighbors[i];
                    if (neighbor == null) continue;

                    var id = WorldFrameParser.ParseEntityId(neighbor.Id, $"neighbors[{i}].id");
                    if (id == playerId) continue;

                    active.Add(id);
                    var angle = Mathf.PI * 2f * visibleCount / Mathf.Max(1, neighbors.Count);
                    var position = new Vector3(Mathf.Cos(angle) * 4f, 0f, Mathf.Sin(angle) * 4f);
                    var view = GetOrCreate(id, neighbor.Name, position, false);
                    view.transform.position = position;
                    SetLabel(view, $"{neighbor.Name}\n{neighbor.Role}\n{neighbor.Act}");
                    visibleCount++;
                }
            }

            var stale = new List<ulong>();
            foreach (var pair in _views)
                if (!active.Contains(pair.Key)) stale.Add(pair.Key);

            foreach (var id in stale)
            {
                if (_views[id] != null) Object.Destroy(_views[id]);
                _views.Remove(id);
            }
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                if (view != null) Object.Destroy(view);
            _views.Clear();
            PlayerTransform = null;
        }

        private GameObject GetOrCreate(ulong id, string displayName, Vector3 position, bool isPlayer)
        {
            if (_views.TryGetValue(id, out var existing)) return existing;

            var go = new GameObject($"EntityView_{id}_{displayName}");
            go.transform.SetParent(_root, false);
            go.transform.position = position;

            var body = CreatePart("Body", PrimitiveType.Capsule, go.transform,
                new Vector3(0, 1.05f, 0), new Vector3(0.58f, 0.62f, 0.38f));
            var head = CreatePart("Head", PrimitiveType.Sphere, go.transform,
                new Vector3(0, 1.92f, 0), new Vector3(0.42f, 0.46f, 0.42f));
            var leftArm = CreatePart("Left arm", PrimitiveType.Capsule, go.transform,
                new Vector3(-0.39f, 1.08f, 0), new Vector3(0.19f, 0.48f, 0.19f));
            var rightArm = CreatePart("Right arm", PrimitiveType.Capsule, go.transform,
                new Vector3(0.39f, 1.08f, 0), new Vector3(0.19f, 0.48f, 0.19f));
            var leftLeg = CreatePart("Left leg", PrimitiveType.Capsule, go.transform,
                new Vector3(-0.17f, 0.38f, 0), new Vector3(0.22f, 0.48f, 0.22f));
            var rightLeg = CreatePart("Right leg", PrimitiveType.Capsule, go.transform,
                new Vector3(0.17f, 0.38f, 0), new Vector3(0.22f, 0.48f, 0.22f));

            var tunic = isPlayer ? new Color(0.20f, 0.48f, 0.72f) : new Color(0.55f, 0.38f, 0.22f);
            ApplyColor(body.GetComponent<Renderer>(), tunic);
            ApplyColor(head.GetComponent<Renderer>(), new Color(0.72f, 0.55f, 0.40f));
            ApplyColor(leftArm.GetComponent<Renderer>(), tunic);
            ApplyColor(rightArm.GetComponent<Renderer>(), tunic);
            ApplyColor(leftLeg.GetComponent<Renderer>(), new Color(0.22f, 0.20f, 0.17f));
            ApplyColor(rightLeg.GetComponent<Renderer>(), new Color(0.22f, 0.20f, 0.17f));

            var motion = go.AddComponent<EntityMotion>();
            motion.Initialize(body.transform, leftArm.transform, rightArm.transform,
                leftLeg.transform, rightLeg.transform, isPlayer);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(go.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 2.45f, 0f);
            labelObject.AddComponent<BillboardLabel>();

            var text = labelObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 42;
            text.characterSize = 0.065f;
            text.color = Color.white;
            text.richText = false;

            _views.Add(id, go);
            return go;
        }

        private static GameObject CreatePart(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            return part;
        }

        private void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            _propertyBlock.Clear();
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        private static void SetLabel(GameObject go, string value)
        {
            var text = go.GetComponentInChildren<TextMesh>();
            if (text != null) text.text = value ?? string.Empty;
        }
    }
}