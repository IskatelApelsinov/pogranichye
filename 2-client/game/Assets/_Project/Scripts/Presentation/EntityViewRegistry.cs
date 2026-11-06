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
        private Transform _playerVisualRoot;
        private Vector3 _leftArmHomePosition;
        private Vector3 _rightArmHomePosition;
        private Quaternion _leftArmHomeRotation;
        private Quaternion _rightArmHomeRotation;
        private Vector3 _leftArmHomeScale;
        private Vector3 _rightArmHomeScale;
        private bool _firstPersonArmsAttached;
        private bool _hasPlayerMugState;
        private bool _playerHadMug;
        private string _lastPlayerEquipmentSignature = string.Empty;
        private string _lastTalkEvent = string.Empty;
        private string _lastSocialEvent = string.Empty;

        public Transform PlayerTransform { get; private set; }

        public EntityViewRegistry(Transform root) { _root = root; }

        public void SetPlayerPresentationOffset(Vector3 localOffset)
        {
            if (_playerVisualRoot != null) _playerVisualRoot.localPosition = localOffset;
        }

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
            // Activity animation is driven only by the server-confirmed registry code.
            SetActivityCode(player, frame.Me.ActivityCode);
            var playerMotion = player.GetComponent<EntityMotion>();
            if (_hasPlayerMugState && _playerHadMug && !frame.Me.Mug && playerMotion != null)
                playerMotion.PlayDrinkGesture();

            // Keep the consumed tankard visible for the full presentation gesture.
            // Inventory/mug state remains server-authoritative; this is only a short visual tail.
            var showTankard = frame.Me.Mug || (playerMotion != null && playerMotion.IsDrinkGesturePlaying);
            SetMug(player, showTankard);
            ApplyPlayerInventoryAppearance(player, frame.Me.Inventory, showTankard);
            _hasPlayerMugState = true;
            _playerHadMug = frame.Me.Mug;
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
                    SetActivityCode(view, neighbor.ActivityCode);
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
                    // v1.4 server fields are authoritative; do not infer familiarity locally.
                    var displayName = neighbor.Known ? neighbor.Name : neighbor.Description;
                    SetLabel(view, displayName);
                    var npcIdentity = view.GetComponent<WorldNpc>();
                    if (npcIdentity != null)
                        npcIdentity.SetServerIdentity(neighbor.Known, neighbor.Name, neighbor.Description);
                    visibleCount++;
                }
            }

            ApplyTalkPresentation(frame, player);

            var stale = new List<ulong>();
            foreach (var pair in _views)
                if (!active.Contains(pair.Key)) stale.Add(pair.Key);

            foreach (var id in stale)
            {
                if (_views[id] != null) Object.Destroy(_views[id]);
                _views.Remove(id);
            }
        }


        private void ApplyPlayerInventoryAppearance(GameObject player, InventoryFrame inventory, bool tankardHeld)
        {
            if (player == null) return;
            var handItem = inventory != null ? inventory.Hand ?? string.Empty : string.Empty;
            var worn = inventory != null ? inventory.Worn : null;
            var head = GetWornItem(worn, "head");
            var body = GetWornItem(worn, "body");
            var feet = GetWornItem(worn, "feet");
            var signature = player.GetInstanceID() + "|" + handItem + "|" + head + "|" + body + "|" + feet +
                "|" + (tankardHeld ? "mug" : "free");
            var motion = player.GetComponent<EntityMotion>();
            if (motion != null) motion.SetEquippedHandItem(handItem, tankardHeld);
            if (signature == _lastPlayerEquipmentSignature) return;
            _lastPlayerEquipmentSignature = signature;

            if (_playerVisualRoot == null) return;
            var oldWorn = _playerVisualRoot.Find("Worn equipment");
            if (oldWorn != null) Object.Destroy(oldWorn.gameObject);
            var wornRoot = new GameObject("Worn equipment").transform;
            wornRoot.SetParent(_playerVisualRoot, false);

            if (head == "hat") BuildWornHat(wornRoot);
            if (body == "cloak") BuildWornCloak(wornRoot);
            if (feet == "boots") BuildWornBoots(wornRoot);

            // In first person, body clothing stays on the hidden world model. Hand-held
            // equipment remains attached to the camera-following arm and stays visible.
            if (_firstPersonArmsAttached)
                foreach (var renderer in wornRoot.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;

            var hand = player.transform.Find("PlayerVisualRoot/Right arm/Right hand");
            if (hand == null) return;
            var oldHeld = hand.Find("Held equipment item");
            if (oldHeld != null) Object.Destroy(oldHeld.gameObject);
            if (tankardHeld || string.IsNullOrEmpty(handItem)) return;
            BuildHeldItem(hand, handItem);
        }

        private static string GetWornItem(Dictionary<string, string> worn, string slot)
        {
            return worn != null && worn.TryGetValue(slot, out var item) ? item ?? string.Empty : string.Empty;
        }

        private void BuildWornHat(Transform root)
        {
            var wool = new Color(0.25f, 0.22f, 0.18f);
            var brim = CreatePart("Worn hat • brim", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.82f, 0f), new Vector3(0.25f, 0.035f, 0.25f));
            ApplyColor(brim.GetComponent<Renderer>(), wool);
            var crown = CreatePart("Worn hat • crown", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.91f, 0f), new Vector3(0.17f, 0.10f, 0.17f));
            ApplyColor(crown.GetComponent<Renderer>(), wool);
            var band = CreatePart("Worn hat • leather band", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.88f, 0f), new Vector3(0.174f, 0.018f, 0.174f));
            ApplyColor(band.GetComponent<Renderer>(), new Color(0.30f, 0.17f, 0.09f));
        }

        private void BuildWornCloak(Transform root)
        {
            var cloth = new Color(0.28f, 0.25f, 0.19f);
            var mantle = CreatePart("Worn cloak • mantle", PrimitiveType.Capsule, root,
                new Vector3(0f, 0.98f, -0.015f), new Vector3(0.50f, 0.55f, 0.35f));
            ApplyColor(mantle.GetComponent<Renderer>(), cloth);
            var back = CreatePart("Worn cloak • hanging back", PrimitiveType.Cube, root,
                new Vector3(0f, 0.83f, -0.205f), new Vector3(0.43f, 0.70f, 0.075f));
            ApplyColor(back.GetComponent<Renderer>(), cloth);
            var clasp = CreatePart("Worn cloak • bronze clasp", PrimitiveType.Sphere, root,
                new Vector3(0f, 1.31f, 0.19f), new Vector3(0.075f, 0.075f, 0.035f));
            ApplyColor(clasp.GetComponent<Renderer>(), new Color(0.62f, 0.43f, 0.17f));
        }

        private void BuildWornBoots(Transform root)
        {
            var leather = new Color(0.20f, 0.12f, 0.075f);
            for (var side = -1; side <= 1; side += 2)
            {
                var boot = CreatePart("Worn boots • leather boot", PrimitiveType.Cube, root,
                    new Vector3(side * 0.14f, 0.13f, 0.055f), new Vector3(0.22f, 0.19f, 0.31f));
                ApplyColor(boot.GetComponent<Renderer>(), leather);
                var cuff = CreatePart("Worn boots • cuff", PrimitiveType.Cylinder, root,
                    new Vector3(side * 0.14f, 0.23f, 0f), new Vector3(0.105f, 0.035f, 0.105f));
                ApplyColor(cuff.GetComponent<Renderer>(), new Color(0.29f, 0.18f, 0.10f));
            }
        }

        private void BuildHeldItem(Transform hand, string item)
        {
            var held = new GameObject("Held equipment item").transform;
            held.SetParent(hand, false);
            held.localPosition = new Vector3(0f, -0.04f, 0.10f);
            held.localRotation = Quaternion.identity;
            held.localScale = Vector3.one * 3f;
            var wood = new Color(0.28f, 0.16f, 0.085f);
            var iron = new Color(0.31f, 0.34f, 0.36f);
            var gold = new Color(0.55f, 0.39f, 0.16f);

            if (item == "axe" || item == "rod" || item == "torch" || item == "lantern")
            {
                var shaft = CreatePart("Held item • wooden shaft", PrimitiveType.Cylinder, held,
                    new Vector3(0f, 0.20f, 0f), new Vector3(0.022f, 0.22f, 0.022f));
                ApplyColor(shaft.GetComponent<Renderer>(), wood);
            }

            switch (item)
            {
                case "axe":
                {
                    var blade = CreatePart("Held axe • iron blade", PrimitiveType.Cube, held,
                        new Vector3(0.13f, 0.38f, 0f), new Vector3(0.24f, 0.13f, 0.055f));
                    ApplyColor(blade.GetComponent<Renderer>(), iron);
                    break;
                }
                case "sword":
                {
                    var blade = CreatePart("Held sword • blade", PrimitiveType.Cube, held,
                        new Vector3(0f, 0.47f, 0f), new Vector3(0.045f, 0.34f, 0.035f));
                    ApplyColor(blade.GetComponent<Renderer>(), iron);
                    var guard = CreatePart("Held sword • crossguard", PrimitiveType.Cube, held,
                        new Vector3(0f, 0.27f, 0f), new Vector3(0.17f, 0.035f, 0.045f));
                    ApplyColor(guard.GetComponent<Renderer>(), gold);
                    var grip = CreatePart("Held sword • grip", PrimitiveType.Cylinder, held,
                        new Vector3(0f, 0.13f, 0f), new Vector3(0.022f, 0.075f, 0.022f));
                    ApplyColor(grip.GetComponent<Renderer>(), wood);
                    break;
                }
                case "bow":
                {
                    var upper = CreatePart("Held bow • upper limb", PrimitiveType.Cylinder, held,
                        new Vector3(0f, 0.43f, 0f), new Vector3(0.018f, 0.17f, 0.018f));
                    upper.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
                    ApplyColor(upper.GetComponent<Renderer>(), wood);
                    var lower = CreatePart("Held bow • lower limb", PrimitiveType.Cylinder, held,
                        new Vector3(0f, 0.12f, 0f), new Vector3(0.018f, 0.17f, 0.018f));
                    lower.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
                    ApplyColor(lower.GetComponent<Renderer>(), wood);
                    var grip = CreatePart("Held bow • grip", PrimitiveType.Cylinder, held,
                        new Vector3(0f, 0.27f, 0f), new Vector3(0.023f, 0.09f, 0.023f));
                    ApplyColor(grip.GetComponent<Renderer>(), gold);
                    break;
                }
                case "rod":
                {
                    var tip = CreatePart("Held rod • tip", PrimitiveType.Cylinder, held,
                        new Vector3(0f, 0.49f, 0f), new Vector3(0.009f, 0.12f, 0.009f));
                    tip.transform.localRotation = Quaternion.Euler(0f, 0f, -10f);
                    ApplyColor(tip.GetComponent<Renderer>(), wood);
                    break;
                }
                case "torch":
                {
                    var flame = CreatePart("Held torch • flame", PrimitiveType.Sphere, held,
                        new Vector3(0f, 0.48f, 0f), new Vector3(0.10f, 0.15f, 0.10f));
                    ApplyColor(flame.GetComponent<Renderer>(), new Color(1f, 0.48f, 0.10f));
                    break;
                }
                case "lantern":
                {
                    var frame = CreatePart("Held lantern • body", PrimitiveType.Cube, held,
                        new Vector3(0f, 0.43f, 0f), new Vector3(0.16f, 0.20f, 0.12f));
                    ApplyColor(frame.GetComponent<Renderer>(), gold);
                    var glow = CreatePart("Held lantern • glass", PrimitiveType.Sphere, held,
                        new Vector3(0f, 0.43f, 0.065f), new Vector3(0.075f, 0.11f, 0.035f));
                    ApplyColor(glow.GetComponent<Renderer>(), new Color(1f, 0.72f, 0.30f));
                    break;
                }
            }
        }

        public bool TryGetView(string entityId, out GameObject view)
        {
            view = null;
            if (string.IsNullOrWhiteSpace(entityId) ||
                !ulong.TryParse(entityId, out var id)) return false;
            return _views.TryGetValue(id, out view) && view != null;
        }

        private void ApplyTalkPresentation(WorldFrame frame, GameObject player)
        {
            // Co-drink is a one-shot server event, not a guess based on localized dialogue text.
            var socialEvent = frame.Me.SocialEvent;
            if (socialEvent != null && socialEvent.Sequence > 0 &&
                socialEvent.Sequence.ToString() != _lastSocialEvent)
            {
                _lastSocialEvent = socialEvent.Sequence.ToString();
                if (string.Equals(socialEvent.Type, "codrink", System.StringComparison.OrdinalIgnoreCase))
                {
                    var playerMotion = player.GetComponent<EntityMotion>();
                    if (playerMotion != null) playerMotion.PlayClinkGesture();
                    if (TryGetView(socialEvent.With, out var drinkPartner))
                    {
                        var partnerNpc = drinkPartner.GetComponent<WorldNpc>();
                        if (partnerNpc != null) partnerNpc.PlayServerClink();
                        else
                        {
                            var partnerMotion = drinkPartner.GetComponent<EntityMotion>();
                            if (partnerMotion != null) partnerMotion.PlayClinkGesture();
                        }
                    }
                }
            }

            var talk = frame.Me.Talk;
            if (talk == null)
            {
                _lastTalkEvent = string.Empty;
                return;
            }

            var eventKey = (talk.With ?? string.Empty) + "|" + (talk.Line ?? string.Empty);
            if (string.IsNullOrEmpty(talk.Line) || eventKey == _lastTalkEvent) return;
            _lastTalkEvent = eventKey;

            var npcDrink = talk.Line.IndexOf("поднимает кружку", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var playerMotionForTalk = player.GetComponent<EntityMotion>();
            if (playerMotionForTalk != null)
            {
                playerMotionForTalk.PlayTalkGesture(null);
                if (npcDrink) playerMotionForTalk.PlayDrinkGesture();
            }

            if (TryGetView(talk.With, out var partner))
            {
                var partnerMotion = partner.GetComponent<EntityMotion>();
                if (partnerMotion != null) partnerMotion.PlayTalkGesture(player.transform);
                var partnerNpc = partner.GetComponent<WorldNpc>();
                if (partnerNpc != null && npcDrink) partnerNpc.PlayServerDrink();
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

            var armRoot = _playerVisualRoot != null ? _playerVisualRoot : PlayerTransform;
            if (_leftPlayerArm == null) _leftPlayerArm = armRoot.Find("Left arm");
            if (_rightPlayerArm == null) _rightPlayerArm = armRoot.Find("Right arm");

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
                _leftPlayerArm.SetParent(armRoot, false);
                _rightPlayerArm.SetParent(armRoot, false);
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
            _playerVisualRoot = null;
            foreach (var view in _views.Values)
                if (view != null) Object.Destroy(view);
            _views.Clear();
            _lastTalkEvent = string.Empty;
            _lastSocialEvent = string.Empty;
            _hasPlayerMugState = false;
            _playerHadMug = false;
            _lastPlayerEquipmentSignature = string.Empty;
            PlayerTransform = null;
        }

        private GameObject GetOrCreate(ulong id, string displayName, string role, Vector3 position, bool isPlayer)
        {
            if (_views.TryGetValue(id, out var existing)) return existing;

            var go = new GameObject($"EntityView_{id}_{displayName}");
            go.transform.SetParent(_root, false);
            go.transform.position = position;

            var visualRoot = go.transform;
            if (isPlayer)
            {
                var visualObject = new GameObject("PlayerVisualRoot");
                visualObject.transform.SetParent(go.transform, false);
                visualRoot = visualObject.transform;
                _playerVisualRoot = visualRoot;
            }

            var body = CreatePart("Body", PrimitiveType.Capsule, visualRoot,
                new Vector3(0, 0.95f, 0), new Vector3(0.48f, 0.52f, 0.32f));
            var head = CreatePart("Head", PrimitiveType.Sphere, visualRoot,
                new Vector3(0, 1.62f, 0), new Vector3(0.36f, 0.40f, 0.36f));
            var leftArm = CreatePart("Left arm", PrimitiveType.Capsule, visualRoot,
                new Vector3(-0.33f, 0.98f, 0), new Vector3(0.16f, 0.42f, 0.16f));
            var rightArm = CreatePart("Right arm", PrimitiveType.Capsule, visualRoot,
                new Vector3(0.33f, 0.98f, 0), new Vector3(0.16f, 0.42f, 0.16f));
            var leftHand = CreatePart("Left hand", PrimitiveType.Sphere, leftArm.transform,
                new Vector3(0f, -0.82f, 0.04f), new Vector3(1.1f, 0.65f, 1.1f));
            var rightHand = CreatePart("Right hand", PrimitiveType.Sphere, rightArm.transform,
                new Vector3(0f, -0.82f, 0.04f), new Vector3(1.1f, 0.65f, 1.1f));
            var leftThumb = CreatePart("Left thumb", PrimitiveType.Sphere, leftArm.transform,
                new Vector3(0.48f, -0.58f, 0.08f), new Vector3(0.42f, 0.62f, 0.58f));
            var rightThumb = CreatePart("Right thumb", PrimitiveType.Sphere, rightArm.transform,
                new Vector3(-0.48f, -0.58f, 0.08f), new Vector3(0.42f, 0.62f, 0.58f));
            var leftLeg = CreatePart("Left leg", PrimitiveType.Capsule, visualRoot,
                new Vector3(-0.14f, 0.36f, 0), new Vector3(0.18f, 0.36f, 0.18f));
            var rightLeg = CreatePart("Right leg", PrimitiveType.Capsule, visualRoot,
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
            AddFaceDetails(visualRoot);
            AddRoleAccessories(visualRoot, role);

            var motion = go.AddComponent<EntityMotion>();
            motion.Initialize(body.transform, head.transform, leftArm.transform, rightArm.transform,
                leftLeg.transform, rightLeg.transform, isPlayer, id);

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

        private void SetMug(GameObject view, bool held)
        {
            if (view == null) return;
            var hand = view.transform.Find("PlayerVisualRoot/Right arm/Right hand");
            if (hand == null) hand = view.transform.Find("Right arm/Right hand");
            if (hand == null) return;

            var mug = hand.Find("Held tankard");
            if (mug == null)
            {
                var detailedTankard = ProceduralVillageBuilder.CreateTankardView(hand, "Held tankard");
                if (detailedTankard != null)
                {
                    detailedTankard.transform.localPosition = new Vector3(0f, -0.10f, 0.10f);
                    detailedTankard.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
                    detailedTankard.transform.localScale = Vector3.one * 0.42f;
                    mug = detailedTankard.transform;
                }
                else
                {
                    // Keep a simple fallback if entity views are built before village materials.
                    var root = new GameObject("Held tankard");
                    root.transform.SetParent(hand, false);
                    root.transform.localPosition = new Vector3(0f, -0.10f, 0.10f);
                    root.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
                    root.transform.localScale = Vector3.one * 0.42f;

                    var body = CreatePart("Wooden cup", PrimitiveType.Cylinder, root.transform,
                        new Vector3(0f, 0.06f, 0f), new Vector3(0.22f, 0.30f, 0.22f));
                    ApplyColor(body.GetComponent<Renderer>(), new Color(0.39f, 0.22f, 0.11f));
                    var rim = CreatePart("Iron rim", PrimitiveType.Cylinder, root.transform,
                        new Vector3(0f, 0.115f, 0f), new Vector3(0.235f, 0.025f, 0.235f));
                    ApplyColor(rim.GetComponent<Renderer>(), new Color(0.34f, 0.32f, 0.27f));
                    var handle = CreatePart("Cup handle", PrimitiveType.Cube, root.transform,
                        new Vector3(0.22f, 0.065f, 0f), new Vector3(0.16f, 0.10f, 0.09f));
                    ApplyColor(handle.GetComponent<Renderer>(), new Color(0.32f, 0.18f, 0.09f));
                    var ale = CreatePart("Ale foam", PrimitiveType.Sphere, root.transform,
                        new Vector3(0f, 0.125f, 0f), new Vector3(0.34f, 0.055f, 0.34f));
                    ApplyColor(ale.GetComponent<Renderer>(), new Color(0.88f, 0.76f, 0.48f));
                    mug = root.transform;
                }
            }

            var motion = view.GetComponent<EntityMotion>();
            if (motion != null) motion.SetHeldTankard(held ? mug : null);
            mug.gameObject.SetActive(held);
        }

        private static void SetActivityCode(GameObject view, string activityCode)
        {
            var motion = view != null ? view.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.SetActivityCode(activityCode);
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