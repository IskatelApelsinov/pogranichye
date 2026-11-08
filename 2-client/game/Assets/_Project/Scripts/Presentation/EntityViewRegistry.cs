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
        private readonly Dictionary<ulong, string> _neighborEquipmentSignatures = new Dictionary<ulong, string>();
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
            var playerMotion = player.GetComponent<EntityMotion>();
            if (_hasPlayerMugState && _playerHadMug && !frame.Me.Mug && playerMotion != null)
                playerMotion.PlayDrinkGesture();

            // Keep the consumed tankard visible for the full presentation gesture.
            // Inventory/mug state remains server-authoritative; this is only a short visual tail.
            var showTankard = frame.Me.Mug || (playerMotion != null && playerMotion.IsDrinkGesturePlaying);
            SetMug(player, showTankard);
            ApplyPlayerInventoryAppearance(player, frame.Me.Inventory, showTankard);
            // Activity animation is driven only by the server-confirmed registry code.
            // Apply inventory first so a real held item suppresses the generic activity prop
            // before the activity code is refreshed for this snapshot.
            SetActivityCode(player, frame.Me.ActivityCode);
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
                    ApplyNeighborGearAppearance(view, neighbor.Gear, id);
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
                _neighborEquipmentSignatures.Remove(id);
            }
        }


        /// <summary>
        /// Applies only the public hand/worn subset from neighbors[].gear. Never infers
        /// equipment from role, activity text, or the client's own inventory.
        /// </summary>
        private void ApplyNeighborGearAppearance(GameObject view, NeighborGearFrame gear, ulong entityId)
        {
            if (view == null) return;

            var handItem = gear != null ? gear.Hand ?? string.Empty : string.Empty;
            var worn = gear != null ? gear.Worn : null;
            var head = GetWornItem(worn, "head");
            var body = GetWornItem(worn, "body");
            var feet = GetWornItem(worn, "feet");
            var signature = (gear == null ? "legacy" : "public") + "|" + handItem + "|" + head + "|" + body + "|" + feet;

            var motion = view.GetComponent<EntityMotion>();
            var supportedHandItem = IsSupportedHandItem(handItem) ? handItem : string.Empty;
            if (motion != null) motion.SetEquippedHandItem(supportedHandItem, false);

            if (_neighborEquipmentSignatures.TryGetValue(entityId, out var previous) && previous == signature)
                return;
            _neighborEquipmentSignatures[entityId] = signature;

            var oldWorn = view.transform.Find("Worn equipment");
            if (oldWorn != null) Object.Destroy(oldWorn.gameObject);
            var wornRoot = new GameObject("Worn equipment").transform;
            wornRoot.SetParent(view.transform, false);
            if (head == "hat") BuildWornHat(wornRoot);
            if (body == "cloak") BuildWornCloak(wornRoot);
            if (feet == "boots") BuildWornBoots(wornRoot);

            var hand = view.transform.Find("Right arm/Right hand");
            if (hand == null) return;
            var oldHeld = hand.Find("Held equipment item");
            if (oldHeld != null) Object.Destroy(oldHeld.gameObject);
            if (supportedHandItem.Length > 0) BuildHeldItem(hand, supportedHandItem);
        }

        private static bool IsSupportedHandItem(string item)
        {
            switch (item)
            {
                case "axe":
                case "sword":
                case "bow":
                case "rod":
                case "torch":
                case "lantern":
                    return true;
                default:
                    return false;
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
            var carriedSignature = GetCarriedItemsSignature(inventory, handItem, head, body, feet);
            var signature = player.GetEntityId() + "|" + handItem + "|" + head + "|" + body + "|" + feet +
                "|" + carriedSignature + "|" + (tankardHeld ? "mug" : "free");
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

            var oldCarried = _playerVisualRoot.Find("Carried equipment");
            if (oldCarried != null) Object.Destroy(oldCarried.gameObject);
            var carriedRoot = new GameObject("Carried equipment").transform;
            carriedRoot.SetParent(_playerVisualRoot, false);
            BuildCarriedEquipment(carriedRoot, inventory, handItem, head, body, feet);

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

        private static string GetCarriedItemsSignature(InventoryFrame inventory,
            string handItem, string head, string body, string feet)
        {
            if (inventory?.Items == null) return string.Empty;
            var signature = string.Empty;
            var visualItems = new[] { "axe", "sword", "bow", "rod", "torch", "lantern", "cloak", "boots", "hat" };
            foreach (var item in visualItems)
            {
                if (item == handItem || item == head || item == body || item == feet) continue;
                if (inventory.Items.TryGetValue(item, out var count) && count > 0)
                    signature += item + ":" + count + ";";
            }
            return signature;
        }

        private void BuildCarriedEquipment(Transform root, InventoryFrame inventory,
            string handItem, string head, string body, string feet)
        {
            if (inventory?.Items == null) return;
            foreach (var item in new[] { "axe", "sword", "bow", "rod", "torch", "lantern" })
            {
                if (item == handItem || !inventory.Items.TryGetValue(item, out var count) || count < 1)
                    continue;
                switch (item)
                {
                    case "axe":
                        BuildCarriedAxe(root);
                        break;
                    case "sword":
                        BuildCarriedSword(root);
                        break;
                    case "bow":
                        BuildCarriedBow(root);
                        break;
                    case "rod":
                        BuildCarriedRod(root);
                        break;
                    case "torch":
                        BuildCarriedTorch(root);
                        break;
                    case "lantern":
                        BuildCarriedLantern(root);
                        break;
                }
            }

            // Clothing not currently worn is represented as a compact folded bundle,
            // rather than being rendered on the body as if it were equipped.
            var hasPackedClothing =
                (inventory.Items.TryGetValue("cloak", out var cloakCount) && cloakCount > 0 && body != "cloak") ||
                (inventory.Items.TryGetValue("boots", out var bootsCount) && bootsCount > 0 && feet != "boots") ||
                (inventory.Items.TryGetValue("hat", out var hatCount) && hatCount > 0 && head != "hat");
            if (hasPackedClothing)
            {
                var bundle = CreatePart("Carried clothing • bedroll", PrimitiveType.Capsule, root,
                    new Vector3(0f, 1.03f, -0.25f), new Vector3(0.20f, 0.31f, 0.17f));
                bundle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                ApplyColor(bundle.GetComponent<Renderer>(), new Color(0.43f, 0.34f, 0.23f));
                var strap = CreatePart("Carried clothing • leather strap", PrimitiveType.Cube, root,
                    new Vector3(0f, 1.03f, -0.17f), new Vector3(0.40f, 0.045f, 0.035f));
                strap.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                ApplyColor(strap.GetComponent<Renderer>(), new Color(0.22f, 0.13f, 0.075f));
            }
        }

        private void BuildCarriedAxe(Transform root)
        {
            var wood = new Color(0.28f, 0.16f, 0.085f);
            var iron = new Color(0.31f, 0.34f, 0.36f);
            var haft = CreatePart("Carried axe • haft", PrimitiveType.Cylinder, root,
                new Vector3(0.22f, 1.04f, -0.23f), new Vector3(0.018f, 0.27f, 0.018f));
            haft.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
            ApplyColor(haft.GetComponent<Renderer>(), wood);
            var blade = CreatePart("Carried axe • head", PrimitiveType.Cube, root,
                new Vector3(0.10f, 1.28f, -0.23f), new Vector3(0.16f, 0.11f, 0.045f));
            ApplyColor(blade.GetComponent<Renderer>(), iron);
        }

        private void BuildCarriedSword(Transform root)
        {
            var sheath = CreatePart("Carried sword • scabbard", PrimitiveType.Cube, root,
                new Vector3(-0.30f, 0.79f, -0.08f), new Vector3(0.075f, 0.54f, 0.075f));
            sheath.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            ApplyColor(sheath.GetComponent<Renderer>(), new Color(0.22f, 0.12f, 0.07f));
            var hilt = CreatePart("Carried sword • hilt", PrimitiveType.Cylinder, root,
                new Vector3(-0.35f, 1.08f, -0.07f), new Vector3(0.025f, 0.11f, 0.025f));
            hilt.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            ApplyColor(hilt.GetComponent<Renderer>(), new Color(0.55f, 0.39f, 0.16f));
        }

        private void BuildCarriedBow(Transform root)
        {
            var wood = new Color(0.30f, 0.19f, 0.10f);
            var upper = CreatePart("Carried bow • upper limb", PrimitiveType.Cylinder, root,
                new Vector3(0.20f, 1.43f, -0.24f), new Vector3(0.018f, 0.24f, 0.018f));
            upper.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
            ApplyColor(upper.GetComponent<Renderer>(), wood);
            var lower = CreatePart("Carried bow • lower limb", PrimitiveType.Cylinder, root,
                new Vector3(-0.02f, 1.08f, -0.24f), new Vector3(0.018f, 0.24f, 0.018f));
            lower.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
            ApplyColor(lower.GetComponent<Renderer>(), wood);
            var grip = CreatePart("Carried bow • grip", PrimitiveType.Cylinder, root,
                new Vector3(0.09f, 1.25f, -0.22f), new Vector3(0.025f, 0.09f, 0.025f));
            ApplyColor(grip.GetComponent<Renderer>(), new Color(0.55f, 0.39f, 0.16f));
        }

        private void BuildCarriedRod(Transform root)
        {
            var rod = CreatePart("Carried rod • staff", PrimitiveType.Cylinder, root,
                new Vector3(-0.22f, 1.18f, -0.23f), new Vector3(0.012f, 0.42f, 0.012f));
            rod.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
            ApplyColor(rod.GetComponent<Renderer>(), new Color(0.30f, 0.19f, 0.10f));
        }

        private void BuildCarriedTorch(Transform root)
        {
            var handle = CreatePart("Carried torch • handle", PrimitiveType.Cylinder, root,
                new Vector3(0.30f, 0.73f, 0.10f), new Vector3(0.015f, 0.17f, 0.015f));
            ApplyColor(handle.GetComponent<Renderer>(), new Color(0.28f, 0.16f, 0.085f));
        }

        private void BuildCarriedLantern(Transform root)
        {
            var frame = CreatePart("Carried lantern • frame", PrimitiveType.Cube, root,
                new Vector3(0.30f, 0.79f, 0.10f), new Vector3(0.13f, 0.17f, 0.09f));
            ApplyColor(frame.GetComponent<Renderer>(), new Color(0.55f, 0.39f, 0.16f));
            var glass = CreatePart("Carried lantern • glass", PrimitiveType.Sphere, root,
                new Vector3(0.30f, 0.79f, 0.15f), new Vector3(0.065f, 0.10f, 0.025f));
            ApplyColor(glass.GetComponent<Renderer>(), new Color(0.80f, 0.62f, 0.30f));
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
            var top = CreatePart("Worn hat • crown top", PrimitiveType.Cylinder, root,
                new Vector3(0f, 2.012f, 0f), new Vector3(0.165f, 0.012f, 0.165f));
            ApplyColor(top.GetComponent<Renderer>(), new Color(0.30f, 0.27f, 0.22f));
            var band = CreatePart("Worn hat • leather band", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.88f, 0f), new Vector3(0.174f, 0.018f, 0.174f));
            ApplyColor(band.GetComponent<Renderer>(), new Color(0.30f, 0.17f, 0.09f));
            var brimStitch = CreatePart("Worn hat • brim stitching", PrimitiveType.Cylinder, root,
                new Vector3(0f, 1.846f, 0f), new Vector3(0.236f, 0.006f, 0.236f));
            ApplyColor(brimStitch.GetComponent<Renderer>(), new Color(0.43f, 0.36f, 0.25f));
        }

        private void BuildWornCloak(Transform root)
        {
            var cloth = new Color(0.28f, 0.25f, 0.19f);
            var shadow = new Color(0.19f, 0.18f, 0.14f);
            var edging = new Color(0.43f, 0.36f, 0.24f);
            var leather = new Color(0.22f, 0.14f, 0.085f);

            var mantle = CreatePart("Worn cloak • shoulder mantle", PrimitiveType.Capsule, root,
                new Vector3(0f, 1.02f, -0.015f), new Vector3(0.50f, 0.52f, 0.35f));
            ApplyColor(mantle.GetComponent<Renderer>(), cloth);
            var back = CreatePart("Worn cloak • hanging back", PrimitiveType.Cube, root,
                new Vector3(0f, 0.82f, -0.205f), new Vector3(0.43f, 0.68f, 0.075f));
            ApplyColor(back.GetComponent<Renderer>(), cloth);

            for (var side = -1; side <= 1; side += 2)
            {
                var fold = CreatePart("Worn cloak • cloth fold", PrimitiveType.Capsule, root,
                    new Vector3(side * 0.14f, 0.84f, -0.248f), new Vector3(0.035f, 0.29f, 0.018f));
                ApplyColor(fold.GetComponent<Renderer>(), side < 0 ? shadow : edging);
                var shoulder = CreatePart("Worn cloak • shoulder drape", PrimitiveType.Capsule, root,
                    new Vector3(side * 0.19f, 1.28f, -0.015f), new Vector3(0.16f, 0.12f, 0.23f));
                shoulder.transform.localRotation = Quaternion.Euler(0f, 0f, side * -12f);
                ApplyColor(shoulder.GetComponent<Renderer>(), cloth);
                var sideEdge = CreatePart("Worn cloak • side hem", PrimitiveType.Cube, root,
                    new Vector3(side * 0.205f, 0.82f, -0.205f), new Vector3(0.035f, 0.61f, 0.025f));
                ApplyColor(sideEdge.GetComponent<Renderer>(), edging);
            }

            var hem = CreatePart("Worn cloak • lower hem", PrimitiveType.Cube, root,
                new Vector3(0f, 0.49f, -0.207f), new Vector3(0.43f, 0.045f, 0.085f));
            ApplyColor(hem.GetComponent<Renderer>(), edging);
            var frontClosure = CreatePart("Worn cloak • front closure", PrimitiveType.Cube, root,
                new Vector3(0f, 1.19f, 0.174f), new Vector3(0.055f, 0.20f, 0.025f));
            ApplyColor(frontClosure.GetComponent<Renderer>(), leather);
            var clasp = CreatePart("Worn cloak • bronze clasp", PrimitiveType.Sphere, root,
                new Vector3(0f, 1.32f, 0.19f), new Vector3(0.075f, 0.075f, 0.035f));
            ApplyColor(clasp.GetComponent<Renderer>(), new Color(0.62f, 0.43f, 0.17f));
        }

        private void BuildWornBoots(Transform root)
        {
            var leather = new Color(0.20f, 0.12f, 0.075f);
            var darkLeather = new Color(0.12f, 0.075f, 0.045f);
            var wornLeather = new Color(0.30f, 0.19f, 0.11f);
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 0.14f;
                var sole = CreatePart("Worn boots • thick sole", PrimitiveType.Cube, root,
                    new Vector3(x, 0.045f, 0.065f), new Vector3(0.225f, 0.055f, 0.33f));
                ApplyColor(sole.GetComponent<Renderer>(), darkLeather);
                var boot = CreatePart("Worn boots • leather boot", PrimitiveType.Cube, root,
                    new Vector3(x, 0.13f, 0.055f), new Vector3(0.22f, 0.19f, 0.31f));
                ApplyColor(boot.GetComponent<Renderer>(), leather);
                var toe = CreatePart("Worn boots • rounded toe", PrimitiveType.Sphere, root,
                    new Vector3(x, 0.115f, 0.19f), new Vector3(0.105f, 0.085f, 0.12f));
                ApplyColor(toe.GetComponent<Renderer>(), wornLeather);
                var cuff = CreatePart("Worn boots • cuff", PrimitiveType.Cylinder, root,
                    new Vector3(x, 0.23f, 0f), new Vector3(0.105f, 0.035f, 0.105f));
                ApplyColor(cuff.GetComponent<Renderer>(), wornLeather);
                var ankleStrap = CreatePart("Worn boots • ankle strap", PrimitiveType.Cube, root,
                    new Vector3(x, 0.205f, 0.105f), new Vector3(0.19f, 0.035f, 0.035f));
                ApplyColor(ankleStrap.GetComponent<Renderer>(), darkLeather);
                var buckle = CreatePart("Worn boots • brass buckle", PrimitiveType.Cube, root,
                    new Vector3(x + side * 0.075f, 0.205f, 0.128f), new Vector3(0.035f, 0.045f, 0.018f));
                ApplyColor(buckle.GetComponent<Renderer>(), new Color(0.58f, 0.42f, 0.18f));
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
                // Keep the viewmodel low and near the screen edges. The character's
                // stylized capsule hands otherwise dominate the first-person view.
                _leftPlayerArm.localPosition = new Vector3(-0.34f, -0.43f, 0.66f);
                _rightPlayerArm.localPosition = new Vector3(0.34f, -0.43f, 0.66f);
                _leftPlayerArm.localRotation = Quaternion.identity;
                _rightPlayerArm.localRotation = Quaternion.identity;
                _leftPlayerArm.localScale = new Vector3(0.12f, 0.29f, 0.12f);
                _rightPlayerArm.localScale = new Vector3(0.12f, 0.29f, 0.12f);
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
            AddBaseClothingDetails(visualRoot, id);
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

        private void AddBaseClothingDetails(Transform root, ulong id)
        {
            var leather = id % 3UL == 0UL
                ? new Color(0.20f, 0.12f, 0.075f)
                : id % 3UL == 1UL
                    ? new Color(0.27f, 0.17f, 0.095f)
                    : new Color(0.16f, 0.13f, 0.09f);
            var bronze = new Color(0.58f, 0.42f, 0.18f);
            var seam = new Color(0.39f, 0.31f, 0.21f);

            var belt = CreatePart("Clothing • leather waist belt", PrimitiveType.Cylinder, root,
                new Vector3(0f, 0.76f, 0f), new Vector3(0.247f, 0.035f, 0.17f));
            ApplyColor(belt.GetComponent<Renderer>(), leather);

            var buckle = CreatePart("Clothing • belt buckle", PrimitiveType.Cube, root,
                new Vector3(0f, 0.76f, 0.171f), new Vector3(0.085f, 0.075f, 0.025f));
            ApplyColor(buckle.GetComponent<Renderer>(), bronze);
            var buckleInset = CreatePart("Clothing • buckle inset", PrimitiveType.Cube, root,
                new Vector3(0f, 0.76f, 0.187f), new Vector3(0.043f, 0.035f, 0.008f));
            ApplyColor(buckleInset.GetComponent<Renderer>(), leather);

            var beltTail = CreatePart("Clothing • belt tail", PrimitiveType.Cube, root,
                new Vector3(0.13f, 0.76f, 0.16f), new Vector3(0.09f, 0.025f, 0.018f));
            beltTail.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            ApplyColor(beltTail.GetComponent<Renderer>(), leather);

            var hem = CreatePart("Clothing • tunic hem", PrimitiveType.Cylinder, root,
                new Vector3(0f, 0.49f, 0f), new Vector3(0.238f, 0.018f, 0.16f));
            ApplyColor(hem.GetComponent<Renderer>(), seam);
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