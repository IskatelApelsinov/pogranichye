using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Lightweight procedural locomotion and social gestures for the first scene.</summary>
    public sealed class EntityMotion : MonoBehaviour
    {
        private Transform _body;
        private Transform _head;
        private Transform _leftArm;
        private Transform _rightArm;
        private Transform _rightHand;
        private GameObject _activityTool;
        private bool _suppressActivityTool;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private float _phase;
        private float _idleRate = 1.7f;
        private float _walkRate = 1f;
        private bool _isPlayer;
        private string _activity = "";
        private float _locomotion;
        private float _locomotionTarget;
        private bool _hasNetworkTarget;
        private Vector3 _networkTargetPosition;
        private Vector3 _networkPositionVelocity;
        private Quaternion _networkTargetRotation;
        private float _lastNetworkTargetAt;
        private float _greetingRemaining;
        private float _drinkGestureRemaining;
        private Transform _heldTankard;
        private Quaternion _heldTankardRestRotation;
        private float _talkGestureRemaining;
        private float _clinkGestureRemaining;
        private float _actionGestureRemaining;
        private string _actionGesture = string.Empty;
        private Transform _attentionTarget;
        private Transform _focusTarget;
        private Quaternion _headTargetRotation;
        private const float NetworkSmoothTime = 0.42f;

        /// <summary>
        /// Applies a server-authoritative remote entity sample. Position is interpolated locally
        /// for presentation only; this never sends or invents world state.
        /// </summary>
        public void SetNetworkTarget(Vector3 position, Quaternion rotation)
        {
            var now = Time.unscaledTime;
            if (!_hasNetworkTarget)
            {
                transform.position = position;
                transform.rotation = rotation;
                _hasNetworkTarget = true;
                _lastNetworkTargetAt = now;
                _networkTargetPosition = position;
                _networkTargetRotation = rotation;
                return;
            }

            var elapsed = Mathf.Max(0.1f, now - _lastNetworkTargetAt);
            var distance = Vector3.Distance(_networkTargetPosition, position);
            var estimatedSpeed = distance / elapsed;
            _locomotionTarget = estimatedSpeed > 0.2f
                ? Mathf.Clamp01(estimatedSpeed / 3.2f)
                : 0f;

            _networkTargetPosition = position;
            _networkTargetRotation = rotation;
            _lastNetworkTargetAt = now;
        }

        public void Initialize(Transform body, Transform head, Transform leftArm, Transform rightArm,
            Transform leftLeg, Transform rightLeg, bool isPlayer, ulong variationSeed = 0UL)
        {
            _body = body;
            _head = head;
            _leftArm = leftArm;
            _rightArm = rightArm;
            _rightHand = rightArm != null ? rightArm.Find("Right hand") : null;
            _leftLeg = leftLeg;
            _rightLeg = rightLeg;
            _isPlayer = isPlayer;
            _phase = (float)(variationSeed % 1000UL) / 1000f * Mathf.PI * 2f;
            _idleRate = 1.55f + (float)(variationSeed % 7UL) * 0.055f;
            _walkRate = 0.94f + (float)((variationSeed / 7UL) % 5UL) * 0.03f;
            _headTargetRotation = Quaternion.identity;
        }

        /// <summary>Presentation-only locomotion intensity derived from replicated movement.</summary>
        public float LocomotionAmount => _locomotion;

        public void SetLocomotion(float normalizedSpeed)
        {
            _locomotionTarget = Mathf.Clamp01(normalizedSpeed);
        }

        /// <summary>
        /// Maps the server's stable activity registry code to a small presentation category.
        /// Unknown or missing codes intentionally fall back to neutral idle.
        /// Never infer animation from goal text or localized outcome prose.
        /// </summary>
        public void SetActivityCode(string activityCode)
        {
            var previousActivity = _activity;
            switch (activityCode)
            {
                case "meal":
                    _activity = "eat";
                    break;
                case "sleep":
                case "rest_idle":
                    _activity = "rest";
                    break;
                case "drink_tavern":
                    _activity = "drink";
                    break;
                case "visit_neighbors":
                    _activity = "social";
                    break;
                case "fish_river":
                    _activity = "fish";
                    break;
                case "fisher_mend":
                    // Net maintenance is not active fishing; use a distinct repair pose and prop.
                    _activity = "mend_net";
                    break;
                case "chop_wood":
                    _activity = "chop";
                    break;
                case "farm_sow":
                case "farm_reap":
                case "farm_tend":
                    _activity = "farm";
                    break;
                case "farm_mend":
                    // Winter maintenance is indoor gear repair, not field work.
                    _activity = "mend_gear";
                    break;
                case "farm_nosow":
                case "farm_noreap":
                    // The server reports these as blocked/no-op states, not active field work.
                    _activity = "wait";
                    break;
                case "smith_forge":
                    _activity = "forge";
                    break;
                case "mine_ore":
                    _activity = "mine";
                    break;
                case "mine_spent":
                case "hunter_empty":
                    // No resource/target remains; don't animate a productive action.
                    _activity = "wait";
                    break;
                case "hunt_wild":
                    _activity = "hunt";
                    break;
                case "cook_hearth":
                    _activity = "cook";
                    break;
                case "brew_remedy":
                    _activity = "brew";
                    break;
                case "forage_herbs":
                    _activity = "forage";
                    break;
                case "weave":
                    _activity = "weave";
                    break;
                case "weaver_wait":
                    _activity = "wait";
                    break;
                case "herd_barn":
                case "herd_graze":
                    _activity = "herd";
                    break;
                case "keep_tavern":
                    _activity = "tavern";
                    break;
                case "study_mana":
                    _activity = "study";
                    break;
                case "smith_cold":
                    _activity = "wait";
                    break;
                case "work":
                    _activity = "work";
                    break;
                default:
                    _activity = string.Empty;
                    break;
            }

            if (previousActivity == _activity) return;
            RefreshActivityTool();
        }

        /// <summary>
        /// Suppresses the generic activity prop while the authoritative inventory says
        /// the character is holding a real item or the server-confirmed tankard.
        /// </summary>
        public void SetEquippedHandItem(string item, bool tankardHeld)
        {
            var suppress = !string.IsNullOrEmpty(item) || tankardHeld;
            if (_suppressActivityTool == suppress) return;
            _suppressActivityTool = suppress;
            RefreshActivityTool();
        }

        private void RefreshActivityTool()
        {
            if (_activityTool != null) Destroy(_activityTool);
            _activityTool = _suppressActivityTool
                ? null
                : ProceduralVillageBuilder.CreateActivityToolView(_rightHand, _activity);
        }

        /// <summary>Plays a brief drink gesture after the server confirms the mug was consumed.</summary>
        public bool IsDrinkGesturePlaying => _drinkGestureRemaining > 0f;

        /// <summary>Registers the held tankard so the confirmed sip can tilt it visually.</summary>
        public void SetHeldTankard(Transform tankard)
        {
            if (_heldTankard == tankard) return;
            _heldTankard = tankard;
            if (_heldTankard != null) _heldTankardRestRotation = _heldTankard.localRotation;
        }

        public void PlayDrinkGesture()
        {
            _drinkGestureRemaining = 1.35f;
        }

        /// <summary>Short presentation-only hand/tool motion for a deliberate player action.</summary>
        public void PlayActionGesture(string action)
        {
            if (action != "chop" && action != "fish_cast" && action != "fish_reel" &&
                action != "attack" && action != "dodge" && action != "guard" && action != "apiary" &&
                action != "farmwork" && action != "forge" && action != "mine" && action != "gather") return;
            _actionGesture = action;
            _actionGestureRemaining = action == "chop" || action == "attack" ? 0.62f : 0.78f;
        }

        public void PlayTalkGesture(Transform observer)
        {
            _talkGestureRemaining = 1.25f;
            _attentionTarget = observer;
        }

        public void PlayClinkGesture()
        {
            _clinkGestureRemaining = 1.15f;
            _drinkGestureRemaining = 1.15f;
        }

        /// <summary>
        /// Sets the observer used for the NPC's attention and gaze presentation.
        /// </summary>
        public void SetFocusTarget(Transform observer)
        {
            _focusTarget = observer;
        }

        public void PlayGreeting(Transform observer)
        {
            _greetingRemaining = 1.65f;
            _attentionTarget = observer;
        }

        private void Update()
        {
            if (_hasNetworkTarget)
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position, _networkTargetPosition, ref _networkPositionVelocity,
                    NetworkSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, _networkTargetRotation, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            }

            if (_body == null) return;
            _locomotion = Mathf.MoveTowards(_locomotion, _locomotionTarget, Time.deltaTime * 5.5f);
            var walking = _locomotion > 0.02f;
            _phase += Time.deltaTime * (walking
                ? Mathf.Lerp(5.5f, 8.5f, _locomotion) * _walkRate
                : (_isPlayer ? 2.2f : _idleRate));

            if (_drinkGestureRemaining > 0f)
                _drinkGestureRemaining = Mathf.Max(0f, _drinkGestureRemaining - Time.deltaTime);
            if (_talkGestureRemaining > 0f)
                _talkGestureRemaining = Mathf.Max(0f, _talkGestureRemaining - Time.deltaTime);
            if (_clinkGestureRemaining > 0f)
                _clinkGestureRemaining = Mathf.Max(0f, _clinkGestureRemaining - Time.deltaTime);
            if (_actionGestureRemaining > 0f)
                _actionGestureRemaining = Mathf.Max(0f, _actionGestureRemaining - Time.deltaTime);

            if (_heldTankard != null)
            {
                var sipProgress = 1f - Mathf.Clamp01(_drinkGestureRemaining / 1.35f);
                var sipTilt = _drinkGestureRemaining > 0f ? Mathf.Sin(sipProgress * Mathf.PI) * 20f : 0f;
                var targetTankardRotation = _heldTankardRestRotation * Quaternion.Euler(0f, 0f, sipTilt);
                _heldTankard.localRotation = Quaternion.Slerp(
                    _heldTankard.localRotation, targetTankardRotation,
                    1f - Mathf.Exp(-14f * Time.deltaTime));
            }

            var resting = _activity == "rest";
            var chopping = _activity == "chop";
            var fishing = _activity == "fish";
            var mendingNet = _activity == "mend_net";
            var forging = _activity == "forge";
            var farming = _activity == "farm";
            var mendingGear = _activity == "mend_gear";
            var mining = _activity == "mine";
            var hunting = _activity == "hunt";
            var weaving = _activity == "weave";
            var studying = _activity == "study";
            var cooking = _activity == "cook" || _activity == "brew";
            var foraging = _activity == "forage";
            var herding = _activity == "herd";
            var tavernWork = _activity == "tavern";
            var working = _activity == "work" || chopping || fishing || forging || farming || mining ||
                hunting || weaving || studying || cooking || foraging || herding || tavernWork || mendingNet || mendingGear;
            var social = _activity == "social";
            var eating = _activity.Contains("eat") || _activity.Contains("ест") || _activity.Contains("ед");
            var drinking = _drinkGestureRemaining > 0f || _activity.Contains("drink") || _activity.Contains("пив") || _activity.Contains("пь");

            var greeting = _greetingRemaining > 0f && !walking && !_isPlayer;
            if (_greetingRemaining > 0f)
            {
                _greetingRemaining = Mathf.Max(0f, _greetingRemaining - Time.deltaTime);
                if (_greetingRemaining <= 0f) _attentionTarget = null;
            }

            var bob = walking ? Mathf.Lerp(0.035f, 0.075f, _locomotion) : resting ? 0.008f : 0.025f;
            _body.localPosition = new Vector3(0, 0.95f + Mathf.Sin(_phase) * bob, 0);
            var greetingLean = greeting ? -1.8f : 0f;
            _body.localRotation = Quaternion.Euler(walking ? Mathf.Lerp(1.5f, 7f, _locomotion) : greetingLean, 0f,
                walking ? Mathf.Sin(_phase * 0.5f) * 1.4f * _locomotion : Mathf.Sin(_phase * 0.65f) * 0.7f);

            var swing = Mathf.Sin(_phase * 1.4f);
            var idleSway = Mathf.Sin(_phase * 0.72f) * 2.2f;
            var leftArmAngle = walking ? swing * Mathf.Lerp(24f, 34f, _locomotion) : working ? swing * 32f : social ? Mathf.Sin(_phase * 2f) * 24f : idleSway;
            var rightArmAngle = walking ? -swing * Mathf.Lerp(24f, 34f, _locomotion) : working ? -swing * 38f : social ? Mathf.Cos(_phase * 2f) * 26f : -idleSway;

            // Stable server activity codes drive distinct silhouettes instead of one generic work loop.
            if (!walking)
            {
                if (chopping || mining)
                {
                    var strike = Mathf.Max(0f, Mathf.Sin(_phase * 2.8f));
                    leftArmAngle = -18f + Mathf.Sin(_phase * 1.4f) * 5f;
                    rightArmAngle = -78f + strike * 82f;
                }
                else if (fishing)
                {
                    leftArmAngle = -24f + Mathf.Sin(_phase * 0.65f) * 4f;
                    rightArmAngle = -48f + Mathf.Sin(_phase * 0.65f + 0.5f) * 7f;
                }
                else if (mendingNet)
                {
                    var stitch = Mathf.Sin(_phase * 2.6f);
                    leftArmAngle = -36f + stitch * 7f;
                    rightArmAngle = -42f - stitch * 11f;
                }
                else if (forging)
                {
                    var hammer = Mathf.Max(0f, Mathf.Sin(_phase * 3.1f));
                    leftArmAngle = -22f + Mathf.Sin(_phase * 1.1f) * 5f;
                    rightArmAngle = -88f + hammer * 78f;
                }
                else if (farming)
                {
                    leftArmAngle = -34f + Mathf.Sin(_phase * 1.5f) * 8f;
                    rightArmAngle = -38f + Mathf.Cos(_phase * 1.5f) * 9f;
                }
                else if (mendingGear)
                {
                    var repair = Mathf.Sin(_phase * 2.2f);
                    leftArmAngle = -28f + repair * 5f;
                    rightArmAngle = -46f - repair * 9f;
                }
                else if (hunting)
                {
                    leftArmAngle = -42f + Mathf.Sin(_phase * 0.8f) * 5f;
                    rightArmAngle = -52f + Mathf.Sin(_phase * 0.8f + 0.4f) * 6f;
                }
                else if (weaving)
                {
                    var shuttle = Mathf.Sin(_phase * 3.2f);
                    leftArmAngle = -34f + shuttle * 9f;
                    rightArmAngle = -38f - shuttle * 13f;
                }
                else if (studying)
                {
                    leftArmAngle = -24f + Mathf.Sin(_phase * 0.7f) * 3f;
                    rightArmAngle = -58f + Mathf.Sin(_phase * 1.1f) * 5f;
                }
                else if (cooking)
                {
                    leftArmAngle = -28f + Mathf.Sin(_phase * 0.8f) * 4f;
                    rightArmAngle = -52f + Mathf.Sin(_phase * 2.1f) * 15f;
                }
                else if (foraging)
                {
                    leftArmAngle = -18f + Mathf.Sin(_phase * 0.5f) * 3f;
                    rightArmAngle = -28f + Mathf.Max(0f, Mathf.Sin(_phase * 2.4f)) * 28f;
                }
                else if (herding)
                {
                    leftArmAngle = -12f + Mathf.Sin(_phase * 0.45f) * 3f;
                    rightArmAngle = -42f + Mathf.Sin(_phase * 0.65f) * 5f;
                }
                else if (tavernWork)
                {
                    leftArmAngle = -18f + Mathf.Sin(_phase * 1.2f) * 5f;
                    rightArmAngle = -32f + Mathf.Sin(_phase * 1.8f) * 10f;
                }
            }

            if (eating) rightArmAngle = Mathf.Sin(_phase * 1.8f) * 34f - 28f;
            if (drinking) rightArmAngle = Mathf.Sin(_phase * 1.15f) * 10f - 62f;
            if (_talkGestureRemaining > 0f && !walking)
            {
                var emphasis = Mathf.Sin((1.25f - _talkGestureRemaining) * 8f);
                leftArmAngle = 18f + emphasis * 10f;
                rightArmAngle = -24f - emphasis * 14f;
            }
            if (_clinkGestureRemaining > 0f && !walking)
            {
                var toast = Mathf.Sin((1.15f - _clinkGestureRemaining) * 9f);
                rightArmAngle = -104f + toast * 7f;
                leftArmAngle = -72f + toast * 5f;
            }
            if (resting) { leftArmAngle = 8f; rightArmAngle = -8f; }

            var rightArmYaw = 0f;
            if (_actionGestureRemaining > 0f && _actionGesture == "chop")
            {
                var progress = 1f - _actionGestureRemaining / 0.62f;
                var strike = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                rightArmAngle = Mathf.Lerp(-118f, 8f, strike);
                rightArmYaw = -10f + strike * 8f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "fish_cast")
            {
                var progress = 1f - _actionGestureRemaining / 0.78f;
                rightArmAngle = Mathf.Lerp(-112f, -18f, Mathf.SmoothStep(0f, 1f, progress));
                rightArmYaw = Mathf.Sin(progress * Mathf.PI) * 24f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "fish_reel")
            {
                var progress = 1f - _actionGestureRemaining / 0.78f;
                rightArmAngle = -68f + Mathf.Sin(progress * Mathf.PI * 4f) * 12f;
                rightArmYaw = Mathf.Sin(progress * Mathf.PI * 4f) * 9f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "attack")
            {
                var progress = 1f - _actionGestureRemaining / 0.62f;
                var strike = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                rightArmAngle = Mathf.Lerp(-102f, 24f, strike);
                rightArmYaw = -22f + strike * 38f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "guard")
            {
                rightArmAngle = -82f;
                leftArmAngle = -72f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "dodge")
            {
                var progress = 1f - _actionGestureRemaining / 0.78f;
                _body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(progress * Mathf.PI) * 18f);
                rightArmAngle = -35f;
                leftArmAngle = 42f;
            }
            else if (_actionGestureRemaining > 0f)
            {
                var progress = 1f - _actionGestureRemaining / 0.78f;
                var workBeat = Mathf.Sin(progress * Mathf.PI * 2f);
                rightArmAngle = -48f + workBeat * 30f;
                leftArmAngle = -24f - workBeat * 12f;
            }
            if (greeting)
            {
                var wave = Mathf.Sin((1.65f - _greetingRemaining) * 15f);
                rightArmAngle = -112f + wave * 9f;
                rightArmYaw = wave * 28f;
            }

            var legSwing = walking ? swing * Mathf.Lerp(24f, 38f, _locomotion) : swing * 2f;

            if (_leftArm != null) _leftArm.localRotation = Quaternion.Euler(leftArmAngle, 0, -7f);
            if (_rightArm != null) _rightArm.localRotation = Quaternion.Euler(rightArmAngle, rightArmYaw, 7f);
            if (_leftLeg != null) _leftLeg.localRotation = Quaternion.Euler(legSwing, 0, 0);
            if (_rightLeg != null) _rightLeg.localRotation = Quaternion.Euler(-legSwing, 0, 0);

            if (_head != null)
            {
                var lookTarget = _attentionTarget != null ? _attentionTarget : _focusTarget;
                if (lookTarget != null && (greeting || _focusTarget != null))
                {
                    var localDirection = transform.InverseTransformPoint(lookTarget.position) - _head.localPosition;
                    var yaw = Mathf.Clamp(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, -38f, 38f);
                    var pitch = Mathf.Clamp(-Mathf.Atan2(localDirection.y, new Vector2(localDirection.x, localDirection.z).magnitude) * Mathf.Rad2Deg, -12f, 12f);
                    _headTargetRotation = Quaternion.Euler(pitch, yaw, 0f);
                }
                else
                {
                    _headTargetRotation = Quaternion.Euler(0f, Mathf.Sin(_phase * 0.31f) * 4f, 0f);
                }

                _head.localRotation = Quaternion.Slerp(
                    _head.localRotation, _headTargetRotation, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
        }
    }
}