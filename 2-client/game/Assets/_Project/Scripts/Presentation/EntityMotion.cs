using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Lightweight procedural locomotion and social gestures for the first scene.</summary>
    public sealed class EntityMotion : MonoBehaviour
    {
        public static event Action<Vector3, bool> NearbyFootstep;
        public static event Action<Vector3> NearbyMagicCast;
        public static event Action<Vector3, NpcActivitySoundKind> NearbyWorkAction;
        private Transform _body;
        private Transform _head;
        private Transform _leftArm;
        private Transform _rightArm;
        private Transform _rightHand;
        private GameObject _activityTool;
        private Vector3 _activityToolRestPosition;
        private Quaternion _activityToolRestRotation;
        private bool _suppressActivityTool;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private float _phase;
        private float _idleRate = 1.7f;
        private float _walkRate = 1f;
        private bool _isPlayer;
        private string _activity = "";
        private bool _hasActivityBaseline;
        private float _locomotion;
        private float _locomotionTarget;
        private bool _hasNetworkTarget;
        private Vector3 _networkTargetPosition;
        private Vector3 _networkPositionVelocity;
        private Quaternion _networkTargetRotation;
        private float _lastNetworkTargetAt;
        private float _presentationUpdateInterval;
        private float _nextPresentationUpdateAt;
        private float _presentationDeltaTime;
        private float _nextFootstepSoundAt;
        private float _greetingRemaining;
        private float _drinkGestureRemaining;
        private Transform _heldTankard;
        private Quaternion _heldTankardRestRotation;
        private float _talkGestureRemaining;
        private float _clinkGestureRemaining;
        private float _actionGestureRemaining;
        private string _actionGesture = string.Empty;
        private bool _workGestureLoop;
        private bool _workGestureStopRequested;
        private float _workGesturePhase;
        private float _workGestureMinimumRemaining;
        private bool _guarding;
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

        /// <summary>
        /// Sets the presentation pose cadence for distance LOD. Network target interpolation
        /// remains render-rate; only procedural animation work is throttled.
        /// </summary>
        public void SetPresentationUpdateInterval(float seconds)
        {
            _presentationUpdateInterval = Mathf.Max(0f, seconds);
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

            var hadActivityBaseline = _hasActivityBaseline;
            _hasActivityBaseline = true;
            if (previousActivity == _activity) return;

            RefreshActivityTool();
            // The local player already plays action audio from confirmed local input/ACK paths.
            // Remote work sounds are emitted only after the first snapshot established a baseline,
            // so joining a scene does not produce a burst of stale "action started" sounds.
            if (!hadActivityBaseline || _isPlayer) return;

            var soundKind = NpcActivitySoundPolicy.FromServerCode(activityCode);
            if (soundKind != NpcActivitySoundKind.None)
                NearbyWorkAction?.Invoke(transform.position, soundKind);
            if (_activity == "weave")
                NearbyMagicCast?.Invoke(transform.position);
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
            if (_activityTool != null)
            {
                _activityToolRestPosition = _activityTool.transform.localPosition;
                _activityToolRestRotation = _activityTool.transform.localRotation;
            }
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
                action != "attack" && action != "heavy_attack" && action != "thrust" &&
                action != "bow_shot" && action != "dodge" && action != "guard" &&
                action != "apiary" && action != "farmwork" && action != "forge" &&
                action != "mine" && action != "gather" && action != "meditate" && action != "study" &&
                action != "hunt" && action != "cook" && action != "herd" &&
                action != "build" && action != "craft" && action != "trade" &&
                action != "fish") return;
            _actionGesture = action;
            _actionGestureRemaining = action == "chop" || action == "attack" ||
                action == "heavy_attack" || action == "thrust" ? 0.62f : 0.78f;
        }

        /// <summary>Starts a sustained, presentation-only work cycle while a result-bearing action is pending.</summary>
        public void SetWorkGesture(string action)
        {
            if (action != "chop" && action != "forge" && action != "mine" &&
                action != "farmwork" && action != "gather" && action != "apiary" &&
                action != "meditate" && action != "study" && action != "hunt" && action != "cook" &&
                action != "herd" && action != "build" && action != "craft" &&
                action != "trade") return;
            PlayActionGesture(action);
            _workGestureLoop = true;
            _workGestureStopRequested = false;
            _workGesturePhase = 0f;
            _workGestureMinimumRemaining = 0.85f;
            _actionGestureRemaining = 1f;
        }

        public void StopWorkGesture()
        {
            if (!_workGestureLoop) return;
            _workGestureStopRequested = true;
            if (_workGestureMinimumRemaining <= 0f) EndWorkGesture();
        }

        /// <summary>Immediately cancels the work pose, e.g. when movement or scene teardown begins.</summary>
        public void CancelWorkGesture()
        {
            if (_workGestureLoop) EndWorkGesture();
        }

        private void EndWorkGesture()
        {
            _workGestureLoop = false;
            _workGestureStopRequested = false;
            _workGestureMinimumRemaining = 0f;
            _actionGestureRemaining = 0f;
            _actionGesture = string.Empty;
        }

        private float ActionProgress(float duration)
        {
            return _workGestureLoop
                ? Mathf.Repeat(_workGesturePhase / (Mathf.PI * 2f), 1f)
                : 1f - _actionGestureRemaining / duration;
        }

        public void SetGuarding(bool guarding) { _guarding = guarding; }

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

            // Keep network interpolation above at render frequency, but run procedural
            // pose work at a distance-dependent cadence. Accumulate scaled time so gestures
            // and locomotion do not slow down when a distant entity is updated less often.
            _presentationDeltaTime += animationDeltaTime;
            var presentationNow = Time.unscaledTime;
            if (_presentationUpdateInterval > 0f && presentationNow < _nextPresentationUpdateAt)
                return;
            var animationDeltaTime = _presentationDeltaTime;
            _presentationDeltaTime = 0f;
            _nextPresentationUpdateAt = presentationNow + _presentationUpdateInterval;
            _locomotion = Mathf.MoveTowards(_locomotion, _locomotionTarget, animationDeltaTime * 5.5f);
            var walking = _locomotion > 0.02f;
            if (!_isPlayer && walking && Time.unscaledTime >= _nextFootstepSoundAt)
            {
                var running = _locomotion >= 0.72f;
                _nextFootstepSoundAt = Time.unscaledTime + (running ? 0.34f : 0.52f);
                NearbyFootstep?.Invoke(transform.position, running);
            }
            if (_workGestureLoop && walking) CancelWorkGesture();
            _phase += animationDeltaTime * (walking
                ? Mathf.Lerp(5.5f, 8.5f, _locomotion) * _walkRate
                : (_isPlayer ? 2.2f : _idleRate));

            if (_drinkGestureRemaining > 0f)
                _drinkGestureRemaining = Mathf.Max(0f, _drinkGestureRemaining - animationDeltaTime);
            if (_talkGestureRemaining > 0f)
                _talkGestureRemaining = Mathf.Max(0f, _talkGestureRemaining - animationDeltaTime);
            if (_clinkGestureRemaining > 0f)
                _clinkGestureRemaining = Mathf.Max(0f, _clinkGestureRemaining - animationDeltaTime);
            if (_workGestureLoop)
            {
                _workGesturePhase += animationDeltaTime * 5.2f;
                _workGestureMinimumRemaining = Mathf.Max(0f, _workGestureMinimumRemaining - animationDeltaTime);
                if (_workGestureStopRequested && _workGestureMinimumRemaining <= 0f)
                    EndWorkGesture();
            }
            if (!_workGestureLoop && _actionGestureRemaining > 0f)
                _actionGestureRemaining = Mathf.Max(0f, _actionGestureRemaining - animationDeltaTime);

            if (_heldTankard != null)
            {
                var sipProgress = 1f - Mathf.Clamp01(_drinkGestureRemaining / 1.35f);
                var sipTilt = _drinkGestureRemaining > 0f ? Mathf.Sin(sipProgress * Mathf.PI) * 20f : 0f;
                var targetTankardRotation = _heldTankardRestRotation * Quaternion.Euler(0f, 0f, sipTilt);
                _heldTankard.localRotation = Quaternion.Slerp(
                    _heldTankard.localRotation, targetTankardRotation,
                    1f - Mathf.Exp(-14f * animationDeltaTime));
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

            // Animate the held prop independently from the arm pose: this makes a hammer,
            // axe, hoe, rod or ladle visibly move through its work cycle without spawning objects.
            if (_activityTool != null)
            {
                var toolBeat = Mathf.Sin(_phase * 2.8f);
                var toolAngle = 0f;
                var toolOffset = Vector3.zero;
                if (!walking && (chopping || mining || forging))
                {
                    toolAngle = Mathf.Max(0f, toolBeat) * (forging ? 20f : 27f);
                    toolOffset.y = Mathf.Max(0f, toolBeat) * 0.018f;
                }
                else if (!walking && farming)
                {
                    toolAngle = Mathf.Sin(_phase * 1.5f) * 10f;
                    toolOffset.y = Mathf.Sin(_phase * 1.5f) * 0.012f;
                }
                else if (!walking && (fishing || mendingNet))
                {
                    toolAngle = Mathf.Sin(_phase * 0.65f) * (fishing ? 4f : 9f);
                    toolOffset.x = Mathf.Sin(_phase * 0.65f) * 0.008f;
                }
                else if (!walking && cooking)
                {
                    toolAngle = Mathf.Sin(_phase * 2.1f) * 14f;
                    toolOffset.x = Mathf.Cos(_phase * 2.1f) * 0.01f;
                }
                else if (!walking && weaving)
                {
                    toolAngle = Mathf.Sin(_phase * 3.2f) * 12f;
                    toolOffset.x = Mathf.Sin(_phase * 3.2f) * 0.012f;
                }
                else if (!walking && (foraging || tavernWork))
                {
                    toolAngle = Mathf.Sin(_phase * 1.8f) * 7f;
                }

                var toolTransform = _activityTool.transform;
                toolTransform.localPosition = _activityToolRestPosition + toolOffset;
                toolTransform.localRotation = _activityToolRestRotation * Quaternion.Euler(0f, 0f, toolAngle);
            }
            // A restrained torso/hip response makes long-running NPC jobs readable at a distance;
            // it is derived only from the replicated activity code, never from outcome text.
            var workBodyBob = 0f;
            var social = _activity == "social";
            var eating = _activity.Contains("eat") || _activity.Contains("ест") || _activity.Contains("ед");
            var drinking = _drinkGestureRemaining > 0f || _activity.Contains("drink") || _activity.Contains("пив") || _activity.Contains("пь");

            var greeting = _greetingRemaining > 0f && !walking && !_isPlayer;
            if (_greetingRemaining > 0f)
            {
                _greetingRemaining = Mathf.Max(0f, _greetingRemaining - animationDeltaTime);
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
                    workBodyBob = strike * 0.045f;
                }
                else if (fishing)
                {
                    var lineTension = Mathf.Sin(_phase * 0.65f);
                    leftArmAngle = -24f + lineTension * 4f;
                    rightArmAngle = -48f + Mathf.Sin(_phase * 0.65f + 0.5f) * 7f;
                    workBodyBob = Mathf.Max(0f, lineTension) * 0.012f;
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
                    workBodyBob = hammer * 0.035f;
                }
                else if (farming)
                {
                    var hoeStroke = Mathf.Max(0f, Mathf.Sin(_phase * 1.5f));
                    leftArmAngle = -34f + Mathf.Sin(_phase * 1.5f) * 8f;
                    rightArmAngle = -38f + Mathf.Cos(_phase * 1.5f) * 9f;
                    workBodyBob = hoeStroke * 0.025f;
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

            if (workBodyBob > 0f && !walking)
                _body.localPosition += new Vector3(0f, workBodyBob, 0f);

            if (eating) rightArmAngle = Mathf.Sin(_phase * 1.8f) * 34f - 28f;
            if (drinking) rightArmAngle = Mathf.Sin(_phase * 1.15f) * 10f - 62f;
            if (_guarding)
            {
                rightArmAngle = -82f;
                leftArmAngle = -72f;
            }
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
                var progress = ActionProgress(0.62f);
                var strike = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                rightArmAngle = Mathf.Lerp(-118f, 8f, strike);
                rightArmYaw = -10f + strike * 8f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "fish_cast")
            {
                var progress = ActionProgress(0.78f);
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
            else if (_actionGestureRemaining > 0f && _actionGesture == "heavy_attack")
            {
                var progress = 1f - _actionGestureRemaining / 0.62f;
                var strike = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                rightArmAngle = Mathf.Lerp(-138f, 32f, strike);
                rightArmYaw = -34f + strike * 48f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "thrust")
            {
                var progress = 1f - _actionGestureRemaining / 0.62f;
                var extension = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                rightArmAngle = -74f - extension * 42f;
                rightArmYaw = -8f;
                _body.localPosition += new Vector3(0f, 0f, extension * 0.10f);
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "bow_shot")
            {
                var progress = 1f - _actionGestureRemaining / 0.78f;
                var draw = progress < 0.58f ? progress / 0.58f : 1f - (progress - 0.58f) / 0.42f;
                leftArmAngle = -62f;
                rightArmAngle = -72f - Mathf.Clamp01(draw) * 38f;
                rightArmYaw = -24f;
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
            else if (_actionGestureRemaining > 0f &&
                (_actionGesture == "forge" || _actionGesture == "mine" || _actionGesture == "craft" || _actionGesture == "build"))
            {
                var progress = ActionProgress(0.78f);
                var strike = Mathf.Max(0f, Mathf.Sin(progress * Mathf.PI * 2f));
                rightArmAngle = -116f + strike * 108f;
                rightArmYaw = -12f + strike * 8f;
                leftArmAngle = -32f + Mathf.Sin(progress * Mathf.PI * 2f + 0.5f) * 5f;
                _body.localRotation = Quaternion.Euler(-3f - strike * 5f, 0f, 0f);
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "farmwork")
            {
                var progress = ActionProgress(0.78f);
                var hoe = Mathf.Max(0f, Mathf.Sin(progress * Mathf.PI * 2f));
                rightArmAngle = -74f + hoe * 62f;
                leftArmAngle = -54f + hoe * 28f;
                _body.localRotation = Quaternion.Euler(8f + hoe * 5f, 0f, 0f);
            }
            else if (_actionGestureRemaining > 0f && (_actionGesture == "gather" || _actionGesture == "apiary"))
            {
                var progress = ActionProgress(0.78f);
                var reach = Mathf.Sin(progress * Mathf.PI);
                rightArmAngle = -28f - reach * 42f;
                leftArmAngle = -18f - reach * 22f;
                _body.localRotation = Quaternion.Euler(12f + reach * 5f, 0f, 0f);
            }
            else if (_actionGestureRemaining > 0f && (_actionGesture == "meditate" || _actionGesture == "study"))
            {
                var progress = ActionProgress(0.78f);
                var breathe = Mathf.Sin(progress * Mathf.PI * 2f);
                rightArmAngle = -54f + breathe * 4f;
                leftArmAngle = -54f - breathe * 4f;
                rightArmYaw = 18f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "hunt")
            {
                rightArmAngle = -72f;
                leftArmAngle = -62f;
                rightArmYaw = -20f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "cook")
            {
                var progress = ActionProgress(0.78f);
                rightArmAngle = -52f + Mathf.Sin(progress * Mathf.PI * 4f) * 18f;
                leftArmAngle = -42f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "herd")
            {
                var progress = ActionProgress(0.78f);
                rightArmAngle = -38f + Mathf.Sin(progress * Mathf.PI * 2f) * 24f;
                leftArmAngle = -12f;
            }
            else if (_actionGestureRemaining > 0f && _actionGesture == "trade")
            {
                var progress = ActionProgress(0.78f);
                rightArmAngle = -32f - Mathf.Sin(progress * Mathf.PI) * 26f;
                leftArmAngle = -18f;
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
                    _head.localRotation, _headTargetRotation, 1f - Mathf.Exp(-8f * animationDeltaTime));
            }
        }
    }
}