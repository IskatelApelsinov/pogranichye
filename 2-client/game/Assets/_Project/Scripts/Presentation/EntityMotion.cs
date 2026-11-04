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
        private Transform _leftLeg;
        private Transform _rightLeg;
        private float _phase;
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
            Transform leftLeg, Transform rightLeg, bool isPlayer)
        {
            _body = body;
            _head = head;
            _leftArm = leftArm;
            _rightArm = rightArm;
            _leftLeg = leftLeg;
            _rightLeg = rightLeg;
            _isPlayer = isPlayer;
            _phase = Random.value * Mathf.PI * 2f;
            _headTargetRotation = Quaternion.identity;
        }

        /// <summary>Presentation-only locomotion intensity derived from replicated movement.</summary>
        public float LocomotionAmount => _locomotion;

        public void SetLocomotion(float normalizedSpeed)
        {
            _locomotionTarget = Mathf.Clamp01(normalizedSpeed);
        }

        public void SetActivity(string activity)
        {
            _activity = (activity ?? string.Empty).ToLowerInvariant();
        }

        /// <summary>
        /// Plays a brief local greeting gesture after the player addresses this NPC.
        /// This is presentation feedback only; the server still owns the interaction result.
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
            _phase += Time.deltaTime * (walking ? Mathf.Lerp(5.5f, 8.5f, _locomotion) : (_isPlayer ? 2.2f : 1.7f));

            var resting = _activity.Contains("rest") || _activity.Contains("отдых");
            var working = _activity.Contains("work") || _activity.Contains("работ");
            var social = _activity.Contains("social") || _activity.Contains("общ") || _activity.Contains("говор");
            var eating = _activity.Contains("eat") || _activity.Contains("ест") || _activity.Contains("ед");
            var drinking = _activity.Contains("drink") || _activity.Contains("пив") || _activity.Contains("пь");

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
            if (eating) rightArmAngle = Mathf.Sin(_phase * 1.8f) * 34f - 28f;
            if (drinking) rightArmAngle = Mathf.Sin(_phase * 1.15f) * 10f - 62f;
            if (resting) { leftArmAngle = 8f; rightArmAngle = -8f; }

            var rightArmYaw = 0f;
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