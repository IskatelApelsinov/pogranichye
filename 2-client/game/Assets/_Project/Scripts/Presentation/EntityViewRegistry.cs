using System;
using Object = UnityEngine.Object;
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

        private readonly Dictionary<ulong, GameObject> _views = new Dictionary<ulong, GameObject>(32);
        private readonly Dictionary<ulong, string> _neighborEquipmentSignatures = new Dictionary<ulong, string>(32);
        // Reuse per-snapshot scratch collections: full frames arrive repeatedly and should
        // not allocate fresh HashSet/List instances on every network update.
        private readonly HashSet<ulong> _activeIds = new HashSet<ulong>();
        private readonly List<ulong> _staleIds = new List<ulong>(32);
        private const int MaxRemoteNeighborViews = 24;
        // Full snapshots are 1 Hz. Retain a missing neighbor briefly to bridge one transient
        // AOI/snapshot gap at a settlement seam; server presence remains authoritative.
        private readonly Dictionary<ulong, float> _neighborLastSeenAt = new Dictionary<ulong, float>();
        private const float NeighborRetentionSeconds = 2.25f;
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
        private ulong _playerMugOwner;
        private bool _hasPlayerMugOwner;
        private string _lastPlayerEquipmentSignature = string.Empty;
        private string _lastTalkEvent = string.Empty;
        private long _lastSocialEventSequence;
        private ulong _socialEventOwner;
        private bool _hasSocialEventBaseline;
#if UNITY_EDITOR
        // Editor-only telemetry for the reported "all NPCs stand still" issue. It observes
        // authoritative projected targets without changing movement or creating client simulation.
        private readonly Dictionary<ulong, Vector3> _diagnosticNpcTargets = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<ulong, Vector3> _diagnosticNpcViews = new Dictionary<ulong, Vector3>();
        private float _nextNpcDiagnosticAt;
        private int _diagnosticNpcSamples;
        private int _diagnosticNpcMoved;
        private int _diagnosticNpcViewsMoved;
        private int _diagnosticGlobalSamples;
        private int _diagnosticLocalSamples;
        private int _diagnosticNearLodViews;
        private int _diagnosticMidLodViews;
        private int _diagnosticFarLodViews;
        private float _diagnosticMaxPresentationGap;
        private float _diagnosticMaxTargetSpan;
        private float _diagnosticMaxViewSpan;
        private float _diagnosticMaxWposSpan;
        private float _diagnosticMaxLocalPosSpan;
#endif

        public Transform PlayerTransform { get; private set; }

        /// <summary>Raised only when a server-confirmed solo drink or co-drink event is presented.</summary>
        public event Action DrinkPresented;

        public EntityViewRegistry(Transform root) { _root = root; }

        public void PlayPlayerActionGesture(string action)
        {
            if (PlayerTransform == null) return;
            var motion = PlayerTransform.GetComponent<EntityMotion>();
            if (motion != null) motion.PlayActionGesture(action);
        }

        public void SetPlayerPresentationOffset(Vector3 localOffset)
        {
            if (_playerVisualRoot != null) _playerVisualRoot.localPosition = localOffset;
        }

        public void Apply(WorldFrame frame)
        {
            if (frame == null || !frame.Alive || frame.Me == null) return;

            var active = _activeIds;
            active.Clear();
            _staleIds.Clear();
#if UNITY_EDITOR
            _diagnosticNearLodViews = 0;
            _diagnosticMidLodViews = 0;
            _diagnosticFarLodViews = 0;
#endif
#if UNITY_EDITOR
            var hasFrameTargetBounds = false;
            var hasFrameViewBounds = false;
            var hasFrameWposBounds = false;
            var hasFrameLocalPosBounds = false;
            var frameTargetMin = Vector3.zero;
            var frameTargetMax = Vector3.zero;
            var frameViewMin = Vector3.zero;
            var frameViewMax = Vector3.zero;
            var frameWposMin = Vector3.zero;
            var frameWposMax = Vector3.zero;
            var frameLocalPosMin = Vector3.zero;
            var frameLocalPosMax = Vector3.zero;
#endif
            var playerId = WorldFrameParser.ParseEntityId(frame.Me.Id, "me.id");
            active.Add(playerId);

            var playerExisted = _views.TryGetValue(playerId, out var existingPlayer) &&
                existingPlayer != null && existingPlayer.GetComponent<WorldNpc>() == null &&
                PlayerTransform == existingPlayer.transform;
            var player = GetOrCreate(playerId, "Player", frame.Me.Role, Vector3.zero, true, true);
            if (!playerExisted && frame.Me.Position != null)
            {
                player.transform.position = ToUnityPosition(frame.Me.Position);
                player.transform.rotation = Quaternion.Euler(0f, FacingToUnityYaw(frame.Me.Position.Facing), 0f);
            }
            // Mug transitions belong to one controlled body. A reconnect may bind this
            // registry to a different server entity without an explicit Clear() first.
            // Re-baseline silently so the previous body's held mug cannot trigger a false sip.
            if (!_hasPlayerMugOwner || _playerMugOwner != playerId)
            {
                _playerMugOwner = playerId;
                _hasPlayerMugOwner = true;
                _hasPlayerMugState = false;
                _playerHadMug = false;
            }

            var playerMotion = player.GetComponent<EntityMotion>();
            if (DrinkPresentationPolicy.IsSoloDrinkTransition(_hasPlayerMugState, _playerHadMug, frame.Me.Mug))
            {
                if (playerMotion != null) playerMotion.PlayDrinkGesture();
                DrinkPresented?.Invoke();
            }

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
                // The server currently advertises at most 24 AOI neighbors. Keep this
                // client-side ceiling too, so malformed/future oversized snapshots cannot
                // instantiate an unbounded number of animated GameObjects and colliders.
                var neighborCount = Mathf.Min(neighbors.Count, MaxRemoteNeighborViews);
                for (var i = 0; i < neighborCount; i++)
                {
                    var neighbor = neighbors[i];
                    if (neighbor == null) continue;

                    var id = WorldFrameParser.ParseEntityId(neighbor.Id, $"neighbors[{i}].id");
                    if (id == playerId) continue;

                    active.Add(id);
                    _neighborLastSeenAt[id] = Time.unscaledTime;
                    var angle = Mathf.PI * 2f * visibleCount / Mathf.Max(1, neighbors.Count);
                    Vector3 position;
                    var hasGlobalPosition = WorldPositionProjection.TryToUnityPosition(
                        neighbor.WorldPosition, frame.Me.WorldPosition, frame.Me.Position, out position);
                    if (!hasGlobalPosition)
                        position = neighbor.Position != null
                            ? ToUnityPosition(neighbor.Position)
                            : new Vector3(Mathf.Cos(angle) * 2.65f, 0f, Mathf.Sin(angle) * 2.65f);
#if UNITY_EDITOR
                    _diagnosticNpcSamples++;
                    if (hasGlobalPosition)
                    {
                        _diagnosticGlobalSamples++;
                        if (!hasFrameWposBounds)
                        {
                            frameWposMin = position;
                            frameWposMax = position;
                            hasFrameWposBounds = true;
                        }
                        else
                        {
                            frameWposMin = Vector3.Min(frameWposMin, position);
                            frameWposMax = Vector3.Max(frameWposMax, position);
                        }
                    }
                    else
                    {
                        _diagnosticLocalSamples++;
                        if (neighbor.Position != null)
                        {
                            var localPosition = ToUnityPosition(neighbor.Position);
                            if (!hasFrameLocalPosBounds)
                            {
                                frameLocalPosMin = localPosition;
                                frameLocalPosMax = localPosition;
                                hasFrameLocalPosBounds = true;
                            }
                            else
                            {
                                frameLocalPosMin = Vector3.Min(frameLocalPosMin, localPosition);
                                frameLocalPosMax = Vector3.Max(frameLocalPosMax, localPosition);
                            }
                        }
                    }
                    if (_diagnosticNpcTargets.TryGetValue(id, out var previousTarget) &&
                        (position - previousTarget).sqrMagnitude > 0.0025f)
                        _diagnosticNpcMoved++;
                    _diagnosticNpcTargets[id] = position;
                    if (!hasFrameTargetBounds)
                    {
                        frameTargetMin = position;
                        frameTargetMax = position;
                        hasFrameTargetBounds = true;
                    }
                    else
                    {
                        frameTargetMin = Vector3.Min(frameTargetMin, position);
                        frameTargetMax = Vector3.Max(frameTargetMax, position);
                    }
                    if (_views.TryGetValue(id, out var diagnosticView) && diagnosticView != null)
                    {
                        var viewPosition = diagnosticView.transform.position;
                        if (_diagnosticNpcViews.TryGetValue(id, out var previousViewPosition) &&
                            (viewPosition - previousViewPosition).sqrMagnitude > 0.0025f)
                            _diagnosticNpcViewsMoved++;
                        _diagnosticNpcViews[id] = viewPosition;
                        if (!hasFrameViewBounds)
                        {
                            frameViewMin = viewPosition;
                            frameViewMax = viewPosition;
                            hasFrameViewBounds = true;
                        }
                        else
                        {
                            frameViewMin = Vector3.Min(frameViewMin, viewPosition);
                            frameViewMax = Vector3.Max(frameViewMax, viewPosition);
                        }
                        _diagnosticMaxPresentationGap = Mathf.Max(_diagnosticMaxPresentationGap,
                            Vector3.Distance(viewPosition, position));
                    }
#endif
                    var view = GetOrCreate(id, neighbor.Name, neighbor.Role, position, neighbor.Controlled, false);
                    SetActivityCode(view, neighbor.ActivityCode);
                    ApplyNeighborGearAppearance(view, neighbor.Gear, id);
                    ApplyControlledPlayerMarker(view, neighbor.Controlled);
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

                    // Distance LOD is presentation-only. Keep network targets, stable IDs,
                    // interaction colliders and server-driven activity state intact.
                    var viewLod = view.GetComponent<EntityViewLod>();
                    if (viewLod != null)
                    {
                        viewLod.ApplyDistance(Vector3.Distance(position, player.transform.position));
#if UNITY_EDITOR
                        switch (viewLod.AnimationTier)
                        {
                            case 0: _diagnosticNearLodViews++; break;
                            case 1: _diagnosticMidLodViews++; break;
                            default: _diagnosticFarLodViews++; break;
                        }
#endif
                    }

                    // NPC tavern drinking is a persistent server activity code; remote player
                    // sip gestures remain a short presentation tail. Neither state is inferred locally.
                    SetMug(view, neighbor.ActivityCode == "drink_tavern" ||
                        (motion != null && motion.IsDrinkGesturePlaying));
                    // v1.4 server fields are authoritative; do not infer familiarity locally.
                    var displayName = neighbor.Known ? neighbor.Name : neighbor.Description;
                    SetLabel(view, displayName);
                    var npcIdentity = view.GetComponent<WorldNpc>();
                    if (npcIdentity != null)
                        npcIdentity.SetServerIdentity(neighbor.Known, neighbor.Name, neighbor.Description);
                    visibleCount++;
                }
            }

#if UNITY_EDITOR
            // Measure spatial spread within each individual authoritative frame, then keep
            // the largest observed frame span for this five-second diagnostic window.
            if (hasFrameTargetBounds)
                _diagnosticMaxTargetSpan = Mathf.Max(_diagnosticMaxTargetSpan,
                    (frameTargetMax - frameTargetMin).magnitude);
            if (hasFrameViewBounds)
                _diagnosticMaxViewSpan = Mathf.Max(_diagnosticMaxViewSpan,
                    (frameViewMax - frameViewMin).magnitude);
            if (hasFrameWposBounds)
                _diagnosticMaxWposSpan = Mathf.Max(_diagnosticMaxWposSpan,
                    (frameWposMax - frameWposMin).magnitude);
            if (hasFrameLocalPosBounds)
                _diagnosticMaxLocalPosSpan = Mathf.Max(_diagnosticMaxLocalPosSpan,
                    (frameLocalPosMax - frameLocalPosMin).magnitude);
#endif
            ApplyTalkPresentation(frame, player);

            var stale = _staleIds;
            foreach (var pair in _views)
            {
                if (active.Contains(pair.Key)) continue;
                if (_neighborLastSeenAt.TryGetValue(pair.Key, out var lastSeen) &&
                    Time.unscaledTime - lastSeen < NeighborRetentionSeconds)
                    continue;
                stale.Add(pair.Key);
            }

            foreach (var id in stale)
            {
                if (_views[id] != null) Object.Destroy(_views[id]);
                _views.Remove(id);
                _neighborEquipmentSignatures.Remove(id);
                _neighborLastSeenAt.Remove(id);
#if UNITY_EDITOR
                _diagnosticNpcTargets.Remove(id);
                _diagnosticNpcViews.Remove(id);
#endif
            }

#if UNITY_EDITOR
            // Five-second summaries make the data path observable while keeping the normal
            // frame loop quiet. No per-NPC log spam and no effect in player builds.
            if (Time.unscaledTime >= _nextNpcDiagnosticAt)
            {
                _nextNpcDiagnosticAt = Time.unscaledTime + 5f;
                if (_diagnosticNpcSamples > 0)
                    Debug.Log($"[Pogranichye NPC replication] samples={_diagnosticNpcSamples}, " +
                        $"targetsMoved={_diagnosticNpcMoved}, viewsMoved={_diagnosticNpcViewsMoved}, " +
                        $"maxFrameTargetSpan={_diagnosticMaxTargetSpan:F2}m, " +
                        $"maxFrameViewSpan={_diagnosticMaxViewSpan:F2}m, " +
                        $"maxFrameWposSpan={_diagnosticMaxWposSpan:F2}m, " +
                        $"maxFrameLocalPosSpan={_diagnosticMaxLocalPosSpan:F2}m, " +
                        $"wpos={_diagnosticGlobalSamples}, localPos={_diagnosticLocalSamples}, " +
                        $"maxViewToTarget={_diagnosticMaxPresentationGap:F2}m, " +
                        $"lodNear={_diagnosticNearLodViews}, lodMid={_diagnosticMidLodViews}, " +
                        $"lodFar={_diagnosticFarLodViews}");
                _diagnosticNpcSamples = 0;
                _diagnosticNpcMoved = 0;
                _diagnosticNpcViewsMoved = 0;
                _diagnosticGlobalSamples = 0;
                _diagnosticLocalSamples = 0;
                _diagnosticMaxPresentationGap = 0f;
                _diagnosticMaxTargetSpan = 0f;
                _diagnosticMaxViewSpan = 0f;
                _diagnosticMaxWposSpan = 0f;
                _diagnosticMaxLocalPosSpan = 0f;
            }
#endif
        }


        /// <summary>
        /// Marks server-confirmed player-controlled neighbors with a subtle cloth armband.
        /// This is presentation only: the Controlled flag comes from the authoritative frame.
        /// </summary>
        private static Transform GetEntityVisualRoot(GameObject view)
        {
            if (view == null) return null;
            var playerRoot = view.transform.Find("PlayerVisualRoot");
            return playerRoot != null ? playerRoot : view.transform;
        }

        private void ApplyControlledPlayerMarker(GameObject view, bool controlled)
        {
            if (view == null) return;

            var visualRoot = GetEntityVisualRoot(view);
            var arm = visualRoot.Find("Left arm");
            if (arm == null) return;

            var marker = arm.Find("Controlled player armband");
            if (!controlled)
            {
                if (marker != null) marker.gameObject.SetActive(false);
                return;
            }

            if (marker == null)
            {
                var band = CreatePart("Controlled player armband", PrimitiveType.Cylinder, arm,
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.19f, 0.045f, 0.19f));
                ApplyColor(band.GetComponent<Renderer>(), new Color(0.16f, 0.43f, 0.48f));
            }
            else
            {
                marker.gameObject.SetActive(true);
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
            var armor = GetWornItem(worn, "armor");
            var offhand = GetWornItem(worn, "offhand");
            var belt = GetWornItem(worn, "belt");
            var signature = (gear == null ? "legacy" : "public") + "|" + handItem + "|" + head + "|" + body + "|" + feet + "|" + armor + "|" + offhand + "|" + belt;

            var visualRoot = GetEntityVisualRoot(view);
            var motion = view.GetComponent<EntityMotion>();
            var supportedHandItem = IsSupportedHandItem(handItem) ? handItem : string.Empty;
            if (motion != null) motion.SetEquippedHandItem(supportedHandItem, false);

            if (_neighborEquipmentSignatures.TryGetValue(entityId, out var previous) && previous == signature)
                return;
            _neighborEquipmentSignatures[entityId] = signature;

            var oldWorn = visualRoot.Find("Worn equipment");
            if (oldWorn != null) Object.Destroy(oldWorn.gameObject);
            var wornRoot = new GameObject("Worn equipment").transform;
            wornRoot.SetParent(visualRoot, false);
            if (head == "hat") BuildWornHat(wornRoot);
            if (body == "cloak") BuildWornCloak(wornRoot);
            if (feet == "boots") BuildWornBoots(wornRoot);
            if (armor == "jerkin") BuildWornJerkin(wornRoot);
            if (belt == "purse") BuildWornPurse(wornRoot);
            var leftArm = visualRoot.Find("Left arm");
            BuildWornShield(leftArm, offhand == "shield");

            var hand = visualRoot.Find("Right arm/Right hand");
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
            var armor = GetWornItem(worn, "armor");
            var offhand = GetWornItem(worn, "offhand");
            var belt = GetWornItem(worn, "belt");
            var carriedSignature = GetCarriedItemsSignature(inventory, handItem, head, body, feet, armor, offhand);
            var signature = player.GetEntityId() + "|" + handItem + "|" + head + "|" + body + "|" + feet +
                "|" + armor + "|" + offhand + "|" + belt + "|" + carriedSignature + "|" + (tankardHeld ? "mug" : "free");
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
            if (armor == "jerkin") BuildWornJerkin(wornRoot);
            if (belt == "purse") BuildWornPurse(wornRoot);
            var leftArm = _playerVisualRoot.Find("Left arm");
            BuildWornShield(leftArm, offhand == "shield");

            var oldCarried = _playerVisualRoot.Find("Carried equipment");
            if (oldCarried != null) Object.Destroy(oldCarried.gameObject);
            var carriedRoot = new GameObject("Carried equipment").transform;
            carriedRoot.SetParent(_playerVisualRoot, false);
            BuildCarriedEquipment(carriedRoot, inventory, handItem, head, body, feet, armor, offhand);

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
            string handItem, string head, string body, string feet, string armor, string offhand)
        {
            if (inventory?.Items == null) return string.Empty;
            var signature = string.Empty;
            var visualItems = new[] { "axe", "sword", "bow", "rod", "torch", "lantern", "knife", "arrow", "bandage", "potion", "cloak", "boots", "hat", "jerkin", "shield" };
            foreach (var item in visualItems)
            {
                if (item == handItem || item == head || item == body || item == feet || item == armor || item == offhand) continue;
                if (inventory.Items.TryGetValue(item, out var count) && count > 0)
                    signature += item + ":" + count + ";";
            }
            return signature;
        }

        private void BuildCarriedEquipment(Transform root, InventoryFrame inventory,
            string handItem, string head, string body, string feet, string armor, string offhand)
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

            if (handItem != "knife" && inventory.Items.TryGetValue("knife", out var knifeCount) && knifeCount > 0)
                BuildCarriedKnifeSheath(root);
            if (inventory.Items.TryGetValue("arrow", out var arrowCount) && arrowCount > 0)
                BuildCarriedQuiver(root, arrowCount);
            var hasBandages = inventory.Items.TryGetValue("bandage", out var bandageCount) && bandageCount > 0;
            var hasPotion = inventory.Items.TryGetValue("potion", out var potionCount) && potionCount > 0;
            if (hasBandages || hasPotion)
                BuildBeltPouches(root, hasBandages, hasPotion);

            // Clothing not currently worn is represented as a compact folded bundle,
            // rather than being rendered on the body as if it were equipped.
            var hasPackedClothing =
                (inventory.Items.TryGetValue("cloak", out var cloakCount) && cloakCount > 0 && body != "cloak") ||
                (inventory.Items.TryGetValue("boots", out var bootsCount) && bootsCount > 0 && feet != "boots") ||
                (inventory.Items.TryGetValue("hat", out var hatCount) && hatCount > 0 && head != "hat") ||
                (inventory.Items.TryGetValue("jerkin", out var jerkinCount) && jerkinCount > 0 && armor != "jerkin") ||
                (inventory.Items.TryGetValue("shield", out var shieldCount) && shieldCount > 0 && offhand != "shield");
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

        private void BuildCarriedKnifeSheath(Transform root)
        {
            var sheath = CreatePart("Carried knife • belt sheath", PrimitiveType.Cube, root,
                new Vector3(-0.27f, 0.76f, 0.12f), new Vector3(0.055f, 0.19f, 0.045f));
            sheath.transform.localRotation = Quaternion.Euler(0f, 0f, -24f);
            ApplyColor(sheath.GetComponent<Renderer>(), new Color(0.18f, 0.11f, 0.065f));
            var grip = CreatePart("Carried knife • handle", PrimitiveType.Cylinder, root,
                new Vector3(-0.32f, 0.88f, 0.13f), new Vector3(0.018f, 0.075f, 0.018f));
            grip.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            ApplyColor(grip.GetComponent<Renderer>(), new Color(0.43f, 0.29f, 0.16f));
        }

        private void BuildCarriedQuiver(Transform root, int arrowCount)
        {
            var quiver = CreatePart("Carried arrows • leather quiver", PrimitiveType.Cylinder, root,
                new Vector3(-0.22f, 1.08f, -0.27f), new Vector3(0.085f, 0.27f, 0.085f));
            quiver.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            ApplyColor(quiver.GetComponent<Renderer>(), new Color(0.24f, 0.15f, 0.085f));
            var visible = Mathf.Clamp(arrowCount, 1, 5);
            for (var i = 0; i < visible; i++)
            {
                var x = -0.27f + i * 0.025f;
                var shaft = CreatePart("Carried arrows • shaft " + i, PrimitiveType.Cylinder, root,
                    new Vector3(x, 1.37f, -0.25f), new Vector3(0.008f, 0.13f, 0.008f));
                shaft.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
                ApplyColor(shaft.GetComponent<Renderer>(), new Color(0.55f, 0.39f, 0.20f));
                var tip = CreatePart("Carried arrows • iron tip " + i, PrimitiveType.Cube, root,
                    new Vector3(x, 1.51f, -0.23f), new Vector3(0.018f, 0.045f, 0.018f));
                ApplyColor(tip.GetComponent<Renderer>(), new Color(0.38f, 0.40f, 0.41f));
            }
        }

        private void BuildBeltPouches(Transform root, bool bandages, bool potion)
        {
            if (bandages)
            {
                var pouch = CreatePart("Belt pouch • bandages", PrimitiveType.Cube, root,
                    new Vector3(0.24f, 0.70f, 0.12f), new Vector3(0.12f, 0.15f, 0.08f));
                ApplyColor(pouch.GetComponent<Renderer>(), new Color(0.56f, 0.48f, 0.34f));
                var flap = CreatePart("Belt pouch • bandage flap", PrimitiveType.Cube, root,
                    new Vector3(0.24f, 0.78f, 0.17f), new Vector3(0.13f, 0.045f, 0.025f));
                ApplyColor(flap.GetComponent<Renderer>(), new Color(0.70f, 0.63f, 0.48f));
            }
            if (potion)
            {
                var pouch = CreatePart("Belt pouch • potion", PrimitiveType.Cube, root,
                    new Vector3(-0.12f, 0.70f, 0.13f), new Vector3(0.10f, 0.13f, 0.075f));
                ApplyColor(pouch.GetComponent<Renderer>(), new Color(0.29f, 0.20f, 0.13f));
                var vial = CreatePart("Belt pouch • glass vial", PrimitiveType.Cylinder, root,
                    new Vector3(-0.12f, 0.74f, 0.19f), new Vector3(0.035f, 0.065f, 0.025f));
                ApplyColor(vial.GetComponent<Renderer>(), new Color(0.52f, 0.35f, 0.24f));
                var cork = CreatePart("Belt pouch • cork", PrimitiveType.Cylinder, root,
                    new Vector3(-0.12f, 0.82f, 0.19f), new Vector3(0.025f, 0.018f, 0.025f));
                ApplyColor(cork.GetComponent<Renderer>(), new Color(0.48f, 0.32f, 0.17f));
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

        private void BuildWornJerkin(Transform root)
        {
            var leather = new Color(0.25f, 0.16f, 0.095f);
            var edge = new Color(0.43f, 0.30f, 0.16f);
            var chest = CreatePart("Worn jerkin • leather cuirass", PrimitiveType.Capsule, root,
                new Vector3(0f, 1.02f, 0.025f), new Vector3(0.52f, 0.54f, 0.36f));
            ApplyColor(chest.GetComponent<Renderer>(), leather);
            var frontPanel = CreatePart("Worn jerkin • front panel", PrimitiveType.Cube, root,
                new Vector3(0f, 1.00f, 0.225f), new Vector3(0.30f, 0.39f, 0.035f));
            ApplyColor(frontPanel.GetComponent<Renderer>(), edge);
            for (var side = -1; side <= 1; side += 2)
            {
                var strap = CreatePart("Worn jerkin • shoulder strap", PrimitiveType.Cube, root,
                    new Vector3(side * 0.17f, 1.31f, 0.08f), new Vector3(0.07f, 0.20f, 0.035f));
                strap.transform.localRotation = Quaternion.Euler(0f, 0f, side * -12f);
                ApplyColor(strap.GetComponent<Renderer>(), leather);
                var rivet = CreatePart("Worn jerkin • brass rivet", PrimitiveType.Sphere, root,
                    new Vector3(side * 0.12f, 1.18f, 0.244f), new Vector3(0.025f, 0.025f, 0.012f));
                ApplyColor(rivet.GetComponent<Renderer>(), new Color(0.64f, 0.47f, 0.20f));
            }
        }

        private void BuildWornShield(Transform leftArm, bool equipped)
        {
            if (leftArm == null) return;
            var oldShield = leftArm.Find("Equipped shield");
            if (oldShield != null) Object.Destroy(oldShield.gameObject);
            if (!equipped) return;

            var shield = new GameObject("Equipped shield").transform;
            shield.SetParent(leftArm, false);
            shield.localPosition = new Vector3(0f, -0.48f, 0.16f);
            shield.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shield.localScale = Vector3.one;
            var wood = CreatePart("Shield • wooden body", PrimitiveType.Cylinder, shield,
                Vector3.zero, new Vector3(0.25f, 0.035f, 0.34f));
            ApplyColor(wood.GetComponent<Renderer>(), new Color(0.35f, 0.22f, 0.12f));
            var boss = CreatePart("Shield • iron boss", PrimitiveType.Sphere, shield,
                new Vector3(0f, 0.045f, 0f), new Vector3(0.10f, 0.045f, 0.10f));
            ApplyColor(boss.GetComponent<Renderer>(), new Color(0.42f, 0.43f, 0.40f));
            var rim = CreatePart("Shield • iron rim", PrimitiveType.Cylinder, shield,
                Vector3.zero, new Vector3(0.255f, 0.012f, 0.345f));
            ApplyColor(rim.GetComponent<Renderer>(), new Color(0.25f, 0.27f, 0.27f));
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

        private void BuildWornPurse(Transform root)
        {
            var leather = new Color(0.24f, 0.14f, 0.075f);
            var shadow = new Color(0.13f, 0.075f, 0.04f);
            var edge = new Color(0.42f, 0.28f, 0.14f);
            var pouch = CreatePart("Worn purse • soft leather pouch", PrimitiveType.Capsule, root,
                new Vector3(0.245f, 0.84f, 0.115f), new Vector3(0.14f, 0.17f, 0.085f));
            pouch.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            ApplyColor(pouch.GetComponent<Renderer>(), leather);
            var gusset = CreatePart("Worn purse • side gusset", PrimitiveType.Cube, root,
                new Vector3(0.245f, 0.84f, 0.175f), new Vector3(0.17f, 0.12f, 0.035f));
            ApplyColor(gusset.GetComponent<Renderer>(), shadow);
            var flap = CreatePart("Worn purse • folded flap", PrimitiveType.Cube, root,
                new Vector3(0.245f, 0.925f, 0.185f), new Vector3(0.15f, 0.075f, 0.035f));
            flap.transform.localRotation = Quaternion.Euler(0f, 0f, -7f);
            ApplyColor(flap.GetComponent<Renderer>(), edge);
            var seam = CreatePart("Worn purse • flap seam", PrimitiveType.Cube, root,
                new Vector3(0.245f, 0.895f, 0.207f), new Vector3(0.11f, 0.012f, 0.012f));
            ApplyColor(seam.GetComponent<Renderer>(), shadow);
            var strap = CreatePart("Worn purse • belt loop", PrimitiveType.Cube, root,
                new Vector3(0.245f, 0.99f, 0.085f), new Vector3(0.075f, 0.09f, 0.035f));
            ApplyColor(strap.GetComponent<Renderer>(), shadow);
            var clasp = CreatePart("Worn purse • brass clasp", PrimitiveType.Cube, root,
                new Vector3(0.245f, 0.925f, 0.211f), new Vector3(0.035f, 0.035f, 0.012f));
            ApplyColor(clasp.GetComponent<Renderer>(), new Color(0.62f, 0.46f, 0.19f));
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
                    AddHeldLight(flame.transform, new Color(1f, 0.58f, 0.28f), 2.1f, 8f);
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
                    AddHeldLight(glow.transform, new Color(1f, 0.72f, 0.42f), 1.6f, 10f);
                    break;
                }
            }
        }

        // Held light is a local presentation effect only. Server-side night safety,
        // visibility and encounter rules remain authoritative and are not inferred here.
        private static void AddHeldLight(Transform source, Color color, float intensity, float range)
        {
            if (source == null) return;
            var lightObject = new GameObject("Held light • local illumination");
            lightObject.transform.SetParent(source, false);
            lightObject.transform.localPosition = Vector3.zero;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
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
            // social_event is a retained "last event" field, not a transient packet.
            // Establish a baseline on the first snapshot (and when the controlled body changes)
            // so reconnecting/rebuilding the view registry cannot replay an old toast.
            var playerId = WorldFrameParser.ParseEntityId(frame.Me.Id, "me.id");
            var socialEvent = frame.Me.SocialEvent;
            var sequence = socialEvent != null ? socialEvent.Sequence : 0L;
            var isNewSocialEvent = false;
            if (!_hasSocialEventBaseline || _socialEventOwner != playerId)
            {
                _socialEventOwner = playerId;
                _lastSocialEventSequence = sequence;
                _hasSocialEventBaseline = true;
            }
            else if (sequence < _lastSocialEventSequence)
            {
                // A new world/session may restart its per-body sequence. Re-baseline silently.
                _lastSocialEventSequence = sequence;
            }
            else if (sequence > _lastSocialEventSequence)
            {
                _lastSocialEventSequence = sequence;
                isNewSocialEvent = true;
            }

            if (socialEvent != null && DrinkPresentationPolicy.IsCoDrinkEvent(isNewSocialEvent, socialEvent.Type))
            {
                var playerMotion = player.GetComponent<EntityMotion>();
                if (playerMotion != null) playerMotion.PlayClinkGesture();
                SetMug(player, true);
                DrinkPresented?.Invoke();
                if (TryGetView(socialEvent.With, out var drinkPartner))
                {
                    SetMug(drinkPartner, true);
                    var partnerNpc = drinkPartner.GetComponent<WorldNpc>();
                    if (partnerNpc != null) partnerNpc.PlayServerClink();
                    else
                    {
                        var partnerMotion = drinkPartner.GetComponent<EntityMotion>();
                        if (partnerMotion != null) partnerMotion.PlayClinkGesture();
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
                if (partnerNpc != null && npcDrink)
                {
                    SetMug(partner, true);
                    partnerNpc.PlayServerDrink();
                }
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
            _neighborLastSeenAt.Clear();
            _neighborEquipmentSignatures.Clear();
            _lastTalkEvent = string.Empty;
            _lastSocialEventSequence = 0L;
            _socialEventOwner = 0UL;
            _hasSocialEventBaseline = false;
            _hasPlayerMugState = false;
            _playerHadMug = false;
            _playerMugOwner = 0UL;
            _hasPlayerMugOwner = false;
            _lastPlayerEquipmentSignature = string.Empty;
            PlayerTransform = null;
        }

        private GameObject GetOrCreate(ulong id, string displayName, string role, Vector3 position,
            bool isPlayer, bool isLocalPlayer)
        {
            if (_views.TryGetValue(id, out var existing))
            {
                var existingIsPlayer = existing.GetComponent<WorldNpc>() == null;
                var existingIsLocalPlayer = PlayerTransform == existing.transform;
                if (existingIsPlayer == isPlayer && existingIsLocalPlayer == isLocalPlayer)
                    return existing;

                // Presence can change while the entity remains in this client's neighbor set.
                // Rebuild only when its semantic kind changes; never leave a live player with
                // an NPC component or vice versa.
                Object.Destroy(existing);
                _views.Remove(id);
                _neighborEquipmentSignatures.Remove(id);
                if (existingIsLocalPlayer) _playerVisualRoot = null;
            }

            var go = new GameObject($"EntityView_{id}_{displayName}");
            go.transform.SetParent(_root, false);
            go.transform.position = position;

            var visualRoot = go.transform;
            if (isPlayer)
            {
                var visualObject = new GameObject("PlayerVisualRoot");
                visualObject.transform.SetParent(go.transform, false);
                visualRoot = visualObject.transform;
                if (isLocalPlayer) _playerVisualRoot = visualRoot;
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
            // Legs rotate around the hips, not around their own midpoint. This makes the
            // procedural gait visibly lift/plant the feet instead of merely twisting capsules.
            var leftLegPivot = new GameObject("Left leg").transform;
            leftLegPivot.SetParent(visualRoot, false);
            leftLegPivot.localPosition = new Vector3(-0.14f, 0.64f, 0f);
            var leftLegMesh = CreatePart("Left lower leg", PrimitiveType.Capsule, leftLegPivot,
                new Vector3(0f, -0.28f, 0f), new Vector3(0.18f, 0.36f, 0.18f));
            var leftBoot = CreatePart("Left boot", PrimitiveType.Cube, leftLegPivot,
                new Vector3(0f, -0.60f, 0.055f), new Vector3(0.19f, 0.10f, 0.30f));
            ApplyColor(leftBoot.GetComponent<Renderer>(), new Color(0.19f, 0.15f, 0.11f));

            var rightLegPivot = new GameObject("Right leg").transform;
            rightLegPivot.SetParent(visualRoot, false);
            rightLegPivot.localPosition = new Vector3(0.14f, 0.64f, 0f);
            var rightLegMesh = CreatePart("Right lower leg", PrimitiveType.Capsule, rightLegPivot,
                new Vector3(0f, -0.28f, 0f), new Vector3(0.18f, 0.36f, 0.18f));
            var rightBoot = CreatePart("Right boot", PrimitiveType.Cube, rightLegPivot,
                new Vector3(0f, -0.60f, 0.055f), new Vector3(0.19f, 0.10f, 0.30f));
            ApplyColor(rightBoot.GetComponent<Renderer>(), new Color(0.19f, 0.15f, 0.11f));

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
            ApplyBodyVariation(id, body.transform, head.transform, leftArm.transform, rightArm.transform,
                leftLegPivot, rightLegPivot);
            if (isPlayer)
                visualRoot.localScale = new Vector3(1f, 0.97f + (float)(id % 5UL) * 0.015f, 1f);
            AddBaseClothingDetails(visualRoot, id);
            if (!isPlayer) AddRoleWorkwear(visualRoot, role, id);
            AddFaceDetails(visualRoot, id, isPlayer);
            AddRoleAccessories(visualRoot, role);

            var motion = go.AddComponent<EntityMotion>();
            motion.Initialize(body.transform, head.transform, leftArm.transform, rightArm.transform,
                leftLegPivot, rightLegPivot, isPlayer, id);

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

            if (!isLocalPlayer)
            {
                var lod = go.AddComponent<EntityViewLod>();
                lod.Initialize(labelObject, go.GetComponentsInChildren<Renderer>(true));
            }

            if (!isLocalPlayer)
            {
                var hitbox = go.AddComponent<CapsuleCollider>();
                hitbox.direction = 1;
                hitbox.center = new Vector3(0f, 0.92f, 0f);
                hitbox.height = 1.84f;
                hitbox.radius = 0.30f;
                if (!isPlayer)
                {
                    var npc = go.AddComponent<WorldNpc>();
                    npc.Initialize(id, displayName, body.GetComponent<Renderer>(), tunic);
                }
            }

            _views.Add(id, go);
            return go;
        }

        // Appearance is derived from the stable server entity ID, never from frame order
        // or Unity's global random state. The same resident therefore keeps their look
        // across reconnects and clients without adding protocol fields.
        // Five restrained body builds keep the settlement from looking like cloned mannequins.
        // Every proportion is keyed by the stable server entity ID, so reconnects and clients agree.
        private static void ApplyBodyVariation(ulong id, Transform body, Transform head,
            Transform leftArm, Transform rightArm, Transform leftLeg, Transform rightLeg)
        {
            var build = (int)(id % 5UL);
            var torsoWidth = new[] { 0.91f, 0.97f, 1.00f, 1.08f, 1.14f }[build];
            var torsoDepth = new[] { 0.94f, 1.02f, 0.96f, 1.08f, 1.12f }[build];
            var armWidth = new[] { 0.92f, 0.96f, 1.00f, 1.08f, 1.13f }[build];
            var legWidth = new[] { 0.94f, 1.02f, 0.98f, 1.08f, 1.12f }[build];
            var headWidth = new[] { 1.04f, 0.96f, 1.00f, 0.98f, 1.03f }[build];

            body.localScale = Vector3.Scale(body.localScale, new Vector3(torsoWidth, 1f, torsoDepth));
            head.localScale = Vector3.Scale(head.localScale, new Vector3(headWidth, 1f, headWidth));
            leftArm.localScale = Vector3.Scale(leftArm.localScale, new Vector3(armWidth, 1f, armWidth));
            rightArm.localScale = Vector3.Scale(rightArm.localScale, new Vector3(armWidth, 1f, armWidth));
            leftLeg.localScale = Vector3.Scale(leftLeg.localScale, new Vector3(legWidth, 1f, legWidth));
            rightLeg.localScale = Vector3.Scale(rightLeg.localScale, new Vector3(legWidth, 1f, legWidth));
        }

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

        private void AddFaceDetails(Transform root, ulong id, bool isPlayer)
        {
            // Small, deterministic face variations make residents recognizable at a glance
            // without adding random state or changing their server identity.
            var variation = (int)(id % 5UL);
            var eyeWidth = variation == 2 ? 0.029f : variation == 4 ? 0.039f : 0.035f;
            var eyeHeight = variation == 1 ? 0.052f : variation == 3 ? 0.038f : 0.045f;
            var leftEye = CreatePart("Left eye", PrimitiveType.Sphere, root,
                new Vector3(-0.075f, 1.66f, 0.165f), new Vector3(eyeWidth, eyeHeight, 0.02f));
            var rightEye = CreatePart("Right eye", PrimitiveType.Sphere, root,
                new Vector3(0.075f, 1.66f, 0.165f), new Vector3(eyeWidth, eyeHeight, 0.02f));

            var hairColor = (id % 3UL) == 0UL
                ? new Color(0.18f, 0.12f, 0.09f)
                : (id % 3UL) == 1UL
                    ? new Color(0.34f, 0.22f, 0.14f)
                    : new Color(0.48f, 0.40f, 0.28f);
            var browScale = variation == 3 ? 0.82f : 1f;
            var leftBrow = CreatePart("Left eyebrow", PrimitiveType.Cube, root,
                new Vector3(-0.075f, 1.715f, 0.169f), new Vector3(0.075f * browScale, 0.018f, 0.018f));
            var rightBrow = CreatePart("Right eyebrow", PrimitiveType.Cube, root,
                new Vector3(0.075f, 1.715f, 0.169f), new Vector3(0.075f * browScale, 0.018f, 0.018f));
            leftBrow.transform.localRotation = Quaternion.Euler(0f, 0f, variation == 1 ? 7f : -3f);
            rightBrow.transform.localRotation = Quaternion.Euler(0f, 0f, variation == 1 ? -7f : 3f);

            var skin = SkinColor(id, isPlayer);
            var nose = CreatePart("Nose", PrimitiveType.Sphere, root,
                new Vector3(0f, 1.605f, 0.183f),
                new Vector3(variation == 4 ? 0.052f : 0.044f, variation == 2 ? 0.048f : 0.058f, 0.042f));
            var mouth = CreatePart("Mouth", PrimitiveType.Sphere, root,
                new Vector3(0f, 1.54f, 0.175f), new Vector3(0.06f, 0.02f, 0.015f));

            var features = new Color(0.12f, 0.09f, 0.07f);
            ApplyColor(leftEye.GetComponent<Renderer>(), features);
            ApplyColor(rightEye.GetComponent<Renderer>(), features);
            ApplyColor(leftBrow.GetComponent<Renderer>(), hairColor);
            ApplyColor(rightBrow.GetComponent<Renderer>(), hairColor);
            ApplyColor(nose.GetComponent<Renderer>(), Color.Lerp(skin, new Color(0.42f, 0.25f, 0.18f), 0.12f));
            ApplyColor(mouth.GetComponent<Renderer>(), new Color(0.35f, 0.13f, 0.12f));

            // A restrained old scar is a rare identity cue, not a universal decoration.
            if (id % 11UL == 0UL)
            {
                var scar = CreatePart("Old cheek scar", PrimitiveType.Cube, root,
                    new Vector3(0.105f, 1.59f, 0.171f), new Vector3(0.009f, 0.065f, 0.012f));
                scar.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
                ApplyColor(scar.GetComponent<Renderer>(), Color.Lerp(skin, new Color(0.34f, 0.17f, 0.13f), 0.42f));
            }
        }

        // Workwear gives professions a readable silhouette before the player knows an NPC's name.
        // All variants are derived from stable entity IDs; no random state or wire fields are needed.
        private void AddRoleWorkwear(Transform root, string role, ulong id)
        {
            var normalized = (role ?? string.Empty).ToLowerInvariant();
            var wear = (float)((id / 5UL) % 4UL) * 0.035f;
            switch (normalized)
            {
                case "smith":
                {
                    var leather = Color.Lerp(new Color(0.16f, 0.105f, 0.065f), new Color(0.31f, 0.20f, 0.12f), wear * 5f);
                    var apron = CreatePart("Workwear • smith leather apron", PrimitiveType.Cube, root,
                        new Vector3(0f, 0.82f, 0.177f), new Vector3(0.31f, 0.48f, 0.045f));
                    ApplyColor(apron.GetComponent<Renderer>(), leather);
                    var bib = CreatePart("Workwear • smith apron bib", PrimitiveType.Cube, root,
                        new Vector3(0f, 1.12f, 0.17f), new Vector3(0.25f, 0.20f, 0.04f));
                    ApplyColor(bib.GetComponent<Renderer>(), leather);
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var strap = CreatePart("Workwear • smith shoulder strap", PrimitiveType.Cube, root,
                            new Vector3(side * 0.15f, 1.22f, 0.06f), new Vector3(0.045f, 0.25f, 0.035f));
                        strap.transform.localRotation = Quaternion.Euler(0f, 0f, side * -18f);
                        ApplyColor(strap.GetComponent<Renderer>(), new Color(0.12f, 0.08f, 0.05f));
                    }
                    break;
                }
                case "cook":
                {
                    var linen = Color.Lerp(new Color(0.66f, 0.59f, 0.46f), new Color(0.82f, 0.76f, 0.63f), wear * 5f);
                    var apron = CreatePart("Workwear • cook linen apron", PrimitiveType.Cube, root,
                        new Vector3(0f, 0.80f, 0.177f), new Vector3(0.30f, 0.43f, 0.035f));
                    ApplyColor(apron.GetComponent<Renderer>(), linen);
                    var pocket = CreatePart("Workwear • cook apron pocket", PrimitiveType.Cube, root,
                        new Vector3(0f, 0.78f, 0.199f), new Vector3(0.15f, 0.12f, 0.012f));
                    ApplyColor(pocket.GetComponent<Renderer>(), new Color(0.48f, 0.39f, 0.28f));
                    break;
                }
                case "farmer":
                {
                    var cloth = Color.Lerp(new Color(0.28f, 0.31f, 0.18f), new Color(0.48f, 0.39f, 0.23f), wear * 5f);
                    var apron = CreatePart("Workwear • farmer field apron", PrimitiveType.Cube, root,
                        new Vector3(0f, 0.79f, 0.176f), new Vector3(0.32f, 0.40f, 0.035f));
                    ApplyColor(apron.GetComponent<Renderer>(), cloth);
                    var shoulder = CreatePart("Workwear • farmer shoulder cloth", PrimitiveType.Capsule, root,
                        new Vector3(0f, 1.25f, -0.015f), new Vector3(0.38f, 0.12f, 0.24f));
                    ApplyColor(shoulder.GetComponent<Renderer>(), Color.Lerp(cloth, new Color(0.18f, 0.20f, 0.13f), 0.35f));
                    break;
                }
                case "fisher":
                {
                    var sash = CreatePart("Workwear • fisher crossbody sash", PrimitiveType.Cube, root,
                        new Vector3(0.02f, 1.00f, 0.18f), new Vector3(0.075f, 0.70f, 0.035f));
                    sash.transform.localRotation = Quaternion.Euler(0f, 0f, -24f);
                    ApplyColor(sash.GetComponent<Renderer>(), new Color(0.18f, 0.29f, 0.34f));
                    var cuff = CreatePart("Workwear • fisher cuff", PrimitiveType.Cylinder, root,
                        new Vector3(0.33f, 0.68f, 0f), new Vector3(0.17f, 0.055f, 0.17f));
                    cuff.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    ApplyColor(cuff.GetComponent<Renderer>(), new Color(0.22f, 0.31f, 0.34f));
                    break;
                }
                case "hunter":
                {
                    var leather = new Color(0.23f, 0.20f, 0.13f);
                    var mantle = CreatePart("Workwear • hunter shoulder mantle", PrimitiveType.Capsule, root,
                        new Vector3(0f, 1.23f, -0.04f), new Vector3(0.42f, 0.17f, 0.28f));
                    ApplyColor(mantle.GetComponent<Renderer>(), leather);
                    var strap = CreatePart("Workwear • hunter chest strap", PrimitiveType.Cube, root,
                        new Vector3(-0.03f, 1.00f, 0.18f), new Vector3(0.07f, 0.56f, 0.035f));
                    strap.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                    ApplyColor(strap.GetComponent<Renderer>(), new Color(0.14f, 0.11f, 0.075f));
                    break;
                }
            }
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

            var heldItem = hand.Find("Held equipment item");
            if (heldItem != null) heldItem.gameObject.SetActive(!held);

            var mug = hand.Find("Held tankard");
            // Avoid creating an inactive tankard for every nearby entity that is not drinking.
            if (mug == null && !held) return;
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
            var text = go.GetComponentInChildren<TextMesh>(true);
            if (text != null) text.text = value ?? string.Empty;
        }
    }
}