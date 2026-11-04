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
        private Transform _leftPlayerArm;
        private Transform _rightPlayerArm;
        private Vector3 _leftArmHomePosition;
        private Vector3 _rightArmHomePosition;
        private Quaternion _leftArmHomeRotation;
        private Quaternion _rightArmHomeRotation;
        private Vector3 _leftArmHomeScale;
        private Vector3 _rightArmHomeScale;
        private bool _firstPersonArmsAttached;

        public Transform PlayerTransform { get; private set; }

        public EntityViewRegistry(Transform root) { _root = root; }

        public void Apply(WorldFrame frame)
        {
            if (frame == null || !frame.Alive || frame.Me == null) return;

            var active = new HashSet<ulong>();
            var playerId = WorldFrameParser.ParseEntityId(frame.Me.Id, "me.id");
            active.Add(playerId);

            var playerExisted = _views.TryGetValue(playerId, out var existingPlayer);
            var player = playerExisted ? existingPlayer : GetOrCreate(playerId, "Player", frame.Me.Role, Vector3.zero, true);
            if (!playerExisted && frame.Me.Position != null)
            {
                player.transform.position = ToUnityPosition(frame.Me.Position);
                player.transform.rotation = Quaternion.Euler(0f, FacingToUnityYaw(frame.Me.Position.Facing), 0f);
            }
            // A goal is an intention, not a confirmed action. The local player has no world nameplate.
            PlayerTransform = player.transform;
            SetLabel(player, string.Empty);

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
                    var position = neighbor.Position != null
                        ? ToUnityPosition(neighbor.Position)
                        : new Vector3(Mathf.Cos(angle) * 2.65f, 0f, Mathf.Sin(angle) * 2.65f);
                    var view = GetOrCreate(id, neighbor.Name, neighbor.Role, position, false);
                    SetActivity(view, neighbor.Act);
                    var targetRotation = neighbor.Position != null
                        ? Quaternion.Euler(0f, FacingToUnityYaw(neighbor.Position.Facing), 0f)
                        : position.sqrMagnitude > 0.01f
                            ? Quaternion.LookRotation(-position.normalized, Vector3.up)
                            : view.transform.rotation;
                    var motion = view.GetComponent<EntityMotion>();
                    if (motion != null)
                        motion.SetNetworkTarget(position, targetRotation);
                    else
                    {
                        view.transform.position = position;
                        view.transform.rotation = targetRotation;
                    }
                    // v1 has no server-authoritative familiarity flag. Unknown/missing means hidden.
                    SetLabel(view, string.Empty);
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


        public static Vector3 ToUnityPosition(ScenePositionFrame position)
        {
            return position == null
                ? Vector3.zero
                : new Vector3((float)position.X, 0f, (float)position.Y);
        }

        public static float FacingToUnityYaw(double facingDegrees)
        {
            return Mathf.Repeat(90f - (float)facingDegrees, 360f);
        }

        public void SetPlayerFirstPerson(bool enabled, Transform cameraTransform)
        {
            if (PlayerTransform == null) return;

            if (_leftPlayerArm == null) _leftPlayerArm = PlayerTransform.Find("Left arm");
            if (_rightPlayerArm == null) _rightPlayerArm = PlayerTransform.Find("Right arm");

            if (enabled && !_firstPersonArmsAttached && cameraTransform != null &&
                _leftPlayerArm != null && _rightPlayerArm != null)
            {
                _leftArmHomePosition = _leftPlayerArm.localPosition;
                _rightArmHomePosition = _rightPlayerArm.localPosition;
                _leftArmHomeRotation = _leftPlayerArm.localRotation;
                _rightArmHomeRotation = _rightPlayerArm.localRotation;
                _leftArmHomeScale = _leftPlayerArm.localScale;
                _rightArmHomeScale = _rightPlayerArm.localScale;

                // These are presentation-only viewmodel sleeves. They follow the camera,
                // while the networked player root remains at its canonical world position.
                _leftPlayerArm.SetParent(cameraTransform, false);
                _rightPlayerArm.SetParent(cameraTransform, false);
                _leftPlayerArm.localPosition = new Vector3(-0.31f, -0.34f, 0.52f);
                _rightPlayerArm.localPosition = new Vector3(0.31f, -0.34f, 0.52f);
                _leftPlayerArm.localRotation = Quaternion.identity;
                _rightPlayerArm.localRotation = Quaternion.identity;
                _leftPlayerArm.localScale = new Vector3(0.19f, 0.44f, 0.19f);
                _rightPlayerArm.localScale = new Vector3(0.19f, 0.44f, 0.19f);
                _firstPersonArmsAttached = true;
            }
            else if (!enabled && _firstPersonArmsAttached)
            {
                _leftPlayerArm.SetParent(PlayerTransform, false);
                _rightPlayerArm.SetParent(PlayerTransform, false);
                _leftPlayerArm.localPosition = _leftArmHomePosition;
                _rightPlayerArm.localPosition = _rightArmHomePosition;
                _leftPlayerArm.localRotation = _leftArmHomeRotation;
                _rightPlayerArm.localRotation = _rightArmHomeRotation;
                _leftPlayerArm.localScale = _leftArmHomeScale;
                _rightPlayerArm.localScale = _rightArmHomeScale;
                _firstPersonArmsAttached = false;
            }

            var renderers = PlayerTransform.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers) renderer.enabled = !enabled;
            if (enabled)
            {
                if (_leftPlayerArm != null)
                    foreach (var renderer in _leftPlayerArm.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                if (_rightPlayerArm != null)
                    foreach (var renderer in _rightPlayerArm.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            }
        }

        public void Clear()
        {
            if (PlayerTransform != null && _firstPersonArmsAttached)
                SetPlayerFirstPerson(false, null);
            _leftPlayerArm = null;
            _rightPlayerArm = null;
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
            var leftHand = CreatePart("Left hand", PrimitiveType.Sphere, leftArm.transform,
                new Vector3(0f, -0.82f, 0.04f), new Vector3(1.1f, 0.65f, 1.1f));
            var rightHand = CreatePart("Right hand", PrimitiveType.Sphere, rightArm.transform,
                new Vector3(0f, -0.82f, 0.04f), new Vector3(1.1f, 0.65f, 1.1f));
            var leftThumb = CreatePart("Left thumb", PrimitiveType.Sphere, leftArm.transform,
                new Vector3(0.48f, -0.58f, 0.08f), new Vector3(0.42f, 0.62f, 0.58f));
            var rightThumb = CreatePart("Right thumb", PrimitiveType.Sphere, rightArm.transform,
                new Vector3(-0.48f, -0.58f, 0.08f), new Vector3(0.42f, 0.62f, 0.58f));
            var leftLeg = CreatePart("Left leg", PrimitiveType.Capsule, go.transform,
                new Vector3(-0.14f, 0.36f, 0), new Vector3(0.18f, 0.36f, 0.18f));
            var rightLeg = CreatePart("Right leg", PrimitiveType.Capsule, go.transform,
                new Vector3(0.14f, 0.36f, 0), new Vector3(0.18f, 0.36f, 0.18f));

            var tunic = RoleColor(role, isPlayer);
            if (!isPlayer) tunic = VariantTunicColor(tunic, id);
            var skin = SkinColor(id, isPlayer);
            ApplyColor(body.GetComponent<Renderer>(), tunic);
            ApplyColor(head.GetComponent<Renderer>(), skin);
            ApplyColor(leftArm.GetComponent<Renderer>(), tunic);
            ApplyColor(rightArm.GetComponent<Renderer>(), tunic);
            if (!isPlayer) AddIdentityDetails(go.transform, id);
            ApplyColor(leftHand.GetComponent<Renderer>(), skin);
            ApplyColor(rightHand.GetComponent<Renderer>(), skin);
            ApplyColor(leftThumb.GetComponent<Renderer>(), skin);
            ApplyColor(rightThumb.GetComponent<Renderer>(), skin);
            ApplyColor(leftLeg.GetComponent<Renderer>(), new Color(0.22f, 0.20f, 0.17f));
            ApplyColor(rightLeg.GetComponent<Renderer>(), new Color(0.22f, 0.20f, 0.17f));
            AddFaceDetails(go.transform);
            AddRoleAccessories(go.transform, role);

            var motion = go.AddComponent<EntityMotion>();
            motion.Initialize(body.transform, head.transform, leftArm.transform, rightArm.transform,
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

        // Appearance is derived from the stable server entity ID, never from frame order
        // or Unity's global random state. The same resident therefore keeps their look
        // across reconnects and clients without adding protocol fields.
        private static Color SkinColor(ulong id, bool isPlayer)
        {
            if (isPlayer) return new Color(0.72f, 0.55f, 0.40f);
            switch ((int)(id % 5UL))
            {
                case 0: return new Color(0.58f, 0.39f, 0.28f);
                case 1: return new Color(0.78f, 0.61f, 0.46f);
                case 2: return new Color(0.68f, 0.48f, 0.34f);
                case 3: return new Color(0.84f, 0.68f, 0.53f);
                default: return new Color(0.72f, 0.55f, 0.40f);
            }
        }

        private static Color VariantTunicColor(Color baseColor, ulong id)
        {
            Color.RGBToHSV(baseColor, out var hue, out var saturation, out var value);
            var hueOffset = (((int)(id % 7UL)) - 3) * 0.012f;
            var valueOffset = (((int)((id / 7UL) % 5UL)) - 2) * 0.025f;
            return Color.HSVToRGB(Mathf.Repeat(hue + hueOffset, 1f),
                Mathf.Clamp01(saturation * (0.92f + (float)(id % 3UL) * 0.08f)),
                Mathf.Clamp01(value + valueOffset));
        }

        private void AddIdentityDetails(Transform root, ulong id)
        {
            var stature = 0.96f + (float)(id % 5UL) * 0.02f;
            root.localScale = new Vector3(1f, stature, 1f);

            // A small subset of residents have visible hair/beards. Keep these as
            // simple low-poly meshes and reuse the existing property-block path.
            var hairColor = (id % 3UL) == 0UL
                ? new Color(0.18f, 0.12f, 0.09f)
                : (id % 3UL) == 1UL
                    ? new Color(0.34f, 0.22f, 0.14f)
                    : new Color(0.48f, 0.40f, 0.28f);
            if ((id % 4UL) != 0UL)
            {
                var hair = CreatePart("Hair", PrimitiveType.Sphere, root,
                    new Vector3(0f, 1.79f, -0.015f), new Vector3(0.34f, 0.18f, 0.34f));
                ApplyColor(hair.GetComponent<Renderer>(), hairColor);
            }

            if ((id % 3UL) == 1UL || (id % 7UL) == 0UL)
            {
                var beard = CreatePart("Beard", PrimitiveType.Sphere, root,
                    new Vector3(0f, 1.47f, 0.16f), new Vector3(0.19f, 0.18f, 0.13f));
                ApplyColor(beard.GetComponent<Renderer>(), hairColor);
            }
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