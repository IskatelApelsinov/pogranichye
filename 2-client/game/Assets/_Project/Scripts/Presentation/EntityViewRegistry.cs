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

            var player = GetOrCreate(playerId, "Player", frame.Me.Role, Vector3.zero, true);
            SetActivity(player, frame.Me.Goal);
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
                    var position = new Vector3(Mathf.Cos(angle) * 2.65f, 0f, Mathf.Sin(angle) * 2.65f);
                    var view = GetOrCreate(id, neighbor.Name, neighbor.Role, position, false);
                    SetActivity(view, neighbor.Act);
                    view.transform.position = position;
                    if (position.sqrMagnitude > 0.01f)
                        view.transform.rotation = Quaternion.LookRotation(-position.normalized, Vector3.up);
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

        public void SetPlayerFirstPerson(bool enabled)
        {
            if (PlayerTransform == null) return;
            var renderers = PlayerTransform.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var partName = renderer.transform.name;
                var isArm = partName == "Left arm" || partName == "Right arm";
                renderer.enabled = !enabled || isArm;
            }
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                if (view != null) Object.Destroy(view);
            _views.Clear();
            PlayerTransform = null;
        }

        private GameObject GetOrCreate(ulong id, string displayName, string role, Vector3 position, bool isPlayer)
        {
            if (_views.TryGetValue(id, out var existing)) return existing;

            var go = new GameObject($"EntityView_{id}_{displayName}");
            go.transform.SetParent(_root, false);
            go.transform.position = position;

            var body = CreatePart("Body", PrimitiveType.Capsule, go.transform,
                new Vector3(0, 0.95f, 0), new Vector3(0.48f, 0.52f, 0.32f));
            var head = CreatePart("Head", PrimitiveType.Sphere, go.transform,
                new Vector3(0, 1.62f, 0), new Vector3(0.36f, 0.40f, 0.36f));
            var leftArm = CreatePart("Left arm", PrimitiveType.Capsule, go.transform,
                new Vector3(-0.33f, 0.98f, 0), new Vector3(0.16f, 0.42f, 0.16f));
            var rightArm = CreatePart("Right arm", PrimitiveType.Capsule, go.transform,
                new Vector3(0.33f, 0.98f, 0), new Vector3(0.16f, 0.42f, 0.16f));
            var leftLeg = CreatePart("Left leg", PrimitiveType.Capsule, go.transform,
                new Vector3(-0.14f, 0.36f, 0), new Vector3(0.18f, 0.36f, 0.18f));
            var rightLeg = CreatePart("Right leg", PrimitiveType.Capsule, go.transform,
                new Vector3(0.14f, 0.36f, 0), new Vector3(0.18f, 0.36f, 0.18f));

            var tunic = RoleColor(role, isPlayer);
            ApplyColor(body.GetComponent<Renderer>(), tunic);
            ApplyColor(head.GetComponent<Renderer>(), new Color(0.72f, 0.55f, 0.40f));
            ApplyColor(leftArm.GetComponent<Renderer>(), tunic);
            ApplyColor(rightArm.GetComponent<Renderer>(), tunic);
            ApplyColor(leftLeg.GetComponent<Renderer>(), new Color(0.22f, 0.20f, 0.17f));
            ApplyColor(rightLeg.GetComponent<Renderer>(), new Color(0.22f, 0.20f, 0.17f));
            AddFaceDetails(go.transform);
            AddRoleAccessories(go.transform, role);

            var motion = go.AddComponent<EntityMotion>();
            motion.Initialize(body.transform, leftArm.transform, rightArm.transform,
                leftLeg.transform, rightLeg.transform, isPlayer);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(go.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 2.28f, 0f);
            labelObject.AddComponent<BillboardLabel>();

            var text = labelObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 42;
            text.characterSize = 0.065f;
            text.color = Color.white;
            text.richText = false;

            if (!isPlayer)
            {
                var hitbox = go.AddComponent<CapsuleCollider>();
                hitbox.direction = 1;
                hitbox.center = new Vector3(0f, 0.92f, 0f);
                hitbox.height = 1.84f;
                hitbox.radius = 0.30f;
                var npc = go.AddComponent<WorldNpc>();
                npc.Initialize(id, displayName, body.GetComponent<Renderer>(), tunic);
            }

            _views.Add(id, go);
            return go;
        }

        private void AddFaceDetails(Transform root)
        {
            var leftEye = CreatePart("Left eye", PrimitiveType.Sphere, root,
                new Vector3(-0.075f, 1.66f, 0.165f), new Vector3(0.035f, 0.045f, 0.02f));
            var rightEye = CreatePart("Right eye", PrimitiveType.Sphere, root,
                new Vector3(0.075f, 1.66f, 0.165f), new Vector3(0.035f, 0.045f, 0.02f));
            var mouth = CreatePart("Mouth", PrimitiveType.Sphere, root,
                new Vector3(0f, 1.54f, 0.175f), new Vector3(0.06f, 0.02f, 0.015f));
            var features = new Color(0.12f, 0.09f, 0.07f);
            ApplyColor(leftEye.GetComponent<Renderer>(), features);
            ApplyColor(rightEye.GetComponent<Renderer>(), features);
            ApplyColor(mouth.GetComponent<Renderer>(), new Color(0.35f, 0.13f, 0.12f));
        }

        private void AddRoleAccessories(Transform root, string role)
        {
            var normalized = (role ?? string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "tavernkeeper":
                    AddHat(root, "Tavernkeeper cap", new Color(0.30f, 0.16f, 0.25f), 0.30f, false);
                    break;
                case "smith":
                    var smithCap = CreatePart("Smith leather cap", PrimitiveType.Cube, root,
                        new Vector3(0f, 1.86f, 0f), new Vector3(0.38f, 0.12f, 0.34f));
                    ApplyColor(smithCap.GetComponent<Renderer>(), new Color(0.22f, 0.23f, 0.25f));
                    break;
                case "farmer":
                    AddHat(root, "Farmer straw hat", new Color(0.72f, 0.57f, 0.29f), 0.36f, true);
                    break;
                case "herbalist":
                    var herbBand = CreatePart("Herbalist headband", PrimitiveType.Cylinder, root,
                        new Vector3(0f, 1.78f, 0f), new Vector3(0.23f, 0.04f, 0.23f));
                    ApplyColor(herbBand.GetComponent<Renderer>(), new Color(0.22f, 0.42f, 0.25f));
                    break;
                case "cook":
                    var cookHat = CreatePart("Cook hat", PrimitiveType.Cylinder, root,
                        new Vector3(0f, 1.94f, 0f), new Vector3(0.18f, 0.13f, 0.18f));
                    ApplyColor(cookHat.GetComponent<Renderer>(), new Color(0.84f, 0.81f, 0.72f));
                    break;
                case "hunter":
                case "fisher":
                    var fieldCap = CreatePart("Field cap", PrimitiveType.Cylinder, root,
                        new Vector3(0f, 1.86f, 0f), new Vector3(0.25f, 0.06f, 0.25f));
                    ApplyColor(fieldCap.GetComponent<Renderer>(), normalized == "hunter"
                        ? new Color(0.25f, 0.31f, 0.18f)
                        : new Color(0.20f, 0.34f, 0.43f));
                    break;
            }
        }

        private void AddHat(Transform root, string name, Color color, float brimRadius, bool flatBrim)
        {
            var brim = CreatePart(name + " brim", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.82f, 0f), new Vector3(brimRadius, 0.035f, brimRadius));
            ApplyColor(brim.GetComponent<Renderer>(), color);
            if (flatBrim) return;

            var crown = CreatePart(name + " crown", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.90f, 0f), new Vector3(brimRadius * 0.62f, 0.09f, brimRadius * 0.62f));
            ApplyColor(crown.GetComponent<Renderer>(), color);
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

        private static Color RoleColor(string role, bool isPlayer)
        {
            if (isPlayer) return new Color(0.20f, 0.48f, 0.72f);
            switch ((role ?? string.Empty).ToLowerInvariant())
            {
                case "tavernkeeper": return new Color(0.48f, 0.24f, 0.48f);
                case "smith": return new Color(0.35f, 0.38f, 0.42f);
                case "farmer": return new Color(0.38f, 0.48f, 0.24f);
                case "herbalist": return new Color(0.24f, 0.48f, 0.34f);
                case "cook": return new Color(0.68f, 0.42f, 0.24f);
                case "hunter": return new Color(0.36f, 0.40f, 0.24f);
                case "fisher": return new Color(0.24f, 0.42f, 0.58f);
                default: return new Color(0.55f, 0.38f, 0.22f);
            }
        }

        private static void SetActivity(GameObject view, string activity)
        {
            var motion = view != null ? view.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.SetActivity(activity);
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