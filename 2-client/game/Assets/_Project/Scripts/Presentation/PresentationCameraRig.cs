using UnityEngine.Serialization;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Presentation-only hybrid camera. It never changes authoritative world position.</summary>
    public sealed class PresentationCameraRig : MonoBehaviour
    {
        [FormerlySerializedAs("offset")]
        [SerializeField] private Vector3 _offset = new Vector3(0, 22, -28);
        [FormerlySerializedAs("followSharpness")]
        [SerializeField] private float _followSharpness = 5f;
        // Raw mouse delta is measured in pixels per frame; a restrained default avoids
        // excessive rotation on high-DPI mice.
        [FormerlySerializedAs("mouseSensitivity")]
        [SerializeField] private float _mouseSensitivity = 0.035f;
        [FormerlySerializedAs("thirdPersonMinPitch")]
        [SerializeField] private float _thirdPersonMinPitch = 18f;
        [FormerlySerializedAs("thirdPersonMaxPitch")]
        [SerializeField] private float _thirdPersonMaxPitch = 62f;
        [FormerlySerializedAs("zoomSpeed")]
        [SerializeField] private float _zoomSpeed = 5f;
        [FormerlySerializedAs("minZoomDistance")]
        [SerializeField] private float _minZoomDistance = 8f;
        [FormerlySerializedAs("maxZoomDistance")]
        [SerializeField] private float _maxZoomDistance = 110f;
        [FormerlySerializedAs("collisionRadius")]
        [SerializeField] private float _collisionRadius = 0.22f;
        [Header("First-person feel")]
        [FormerlySerializedAs("headBobAmplitude")]
        [SerializeField] private float _headBobAmplitude = 0.035f;
        [FormerlySerializedAs("headBobSideAmplitude")]
        [SerializeField] private float _headBobSideAmplitude = 0.012f;
        [FormerlySerializedAs("headBobFrequency")]
        [SerializeField] private float _headBobFrequency = 9f;
        [FormerlySerializedAs("sprintFieldOfViewBoost")]
        [SerializeField] private float _sprintFieldOfViewBoost = 3f;
        [FormerlySerializedAs("fieldOfViewSharpness")]
        [SerializeField] private float _fieldOfViewSharpness = 7f;
        private Camera _camera;
        private float _baseFarClipPlane = 1000f;
        private Pogranichye.Client.ClientInput _input;
        private float _defaultFieldOfView = 48f;
        private float _headBobPhase;
        private Transform _target;
        private Transform _lockOnTarget;
        private bool _firstPerson;
        private bool _mounted;
        private float _yaw;
        private float _pitch;
        private float _zoomDistance;
        private float _jumpOffset;
        private bool _captureMouseForGesture;
        private bool _invertMouseY;

        public void BindInput(Pogranichye.Client.ClientInput input) { _input = input; }
        public bool IsFirstPerson => _firstPerson;
        public bool CaptureMouseForGesture => _captureMouseForGesture;
        public void SetMounted(bool mounted) { _mounted = mounted; }

        public void SetJumpOffset(float offset) { _jumpOffset = Mathf.Max(0f, offset); }

        public void SetDefaultFieldOfView(float fieldOfView)
        {
            _defaultFieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
            if (_camera != null) _camera.fieldOfView = _defaultFieldOfView;
        }

        public void SetMouseGestureCapture(bool active) { _captureMouseForGesture = active; }

        /// <summary>Extends visibility for the server-authored distant cauldron rim LOD.</summary>
        public void SetWorldHorizonDistance(float distance)
        {
            if (_camera == null) return;
            var target = distance > 0f ? Mathf.Max(_baseFarClipPlane, distance) : _baseFarClipPlane;
            _camera.farClipPlane = Mathf.Clamp(target, _baseFarClipPlane, 100000f);
        }

        public void SetLookSettings(float sensitivity, bool invertY)
        {
            _mouseSensitivity = Mathf.Clamp(sensitivity, 0.008f, 0.09f);
            _invertMouseY = invertY;
        }

        private void Awake()
        {
            _zoomDistance = _offset.magnitude;
            _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                _defaultFieldOfView = _camera.fieldOfView;
                _baseFarClipPlane = _camera.farClipPlane;
            }
        }

        public void SetTarget(Transform target)
        {
            if (_target == target) return;
            _target = target;
            if (target != null) _yaw = target.eulerAngles.y;
            _pitch = _firstPerson ? 0f : 28f;
        }

        public void SetLockOnTarget(Transform target)
        {
            _lockOnTarget = target;
        }

        public void SetFirstPerson(bool enabled)
        {
            if (_firstPerson == enabled) return;
            _firstPerson = enabled;
            if (_target != null) _yaw = _target.eulerAngles.y;
            _pitch = enabled ? 0f : 28f;
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            var motion = _target.GetComponent<EntityMotion>();
            var gait = motion != null ? motion.LocomotionAmount : 0f;
            var sprintBlend = _firstPerson ? Mathf.InverseLerp(0.58f, 1f, gait) : 0f;
            if (_camera != null)
            {
                var targetFov = _defaultFieldOfView + _sprintFieldOfViewBoost * sprintBlend;
                var blend = 1f - Mathf.Exp(-_fieldOfViewSharpness * Time.unscaledDeltaTime);
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, blend);
            }

            if (_lockOnTarget == null)
            {
                _lockOnTarget = null;
            }
            else if (!_captureMouseForGesture)
            {
                var aimPoint = _lockOnTarget.position + Vector3.up * 1.1f;
                var aimOrigin = _firstPerson ? transform.position : _target.position;
                var toTarget = aimPoint - aimOrigin;
                var horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
                if (horizontal > 0.05f)
                {
                    var playerToTarget = aimPoint - _target.position;
                    var desiredYaw = Mathf.Atan2(playerToTarget.x, playerToTarget.z) * Mathf.Rad2Deg;
                    var desiredPitch = _firstPerson
                        ? -Mathf.Atan2(toTarget.y, horizontal) * Mathf.Rad2Deg
                        : _pitch;
                    var lockBlend = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
                    _yaw = Mathf.LerpAngle(_yaw, desiredYaw, lockBlend);
                    if (_firstPerson)
                        _pitch = Mathf.Lerp(_pitch, desiredPitch, lockBlend);
                }
            }

            if (_firstPerson)
            {
                if (!_captureMouseForGesture)
                {
                    var look = _input != null ? _input.LookDelta : Vector2.zero;
                    _yaw += look.x * _mouseSensitivity;
                    _pitch = Mathf.Clamp(_pitch + look.y * _mouseSensitivity * (_invertMouseY ? 1f : -1f), -75f, 75f);
                }
                if (!_captureMouseForGesture)
                    _target.rotation = Quaternion.Euler(0f, _yaw, 0f);
                _headBobPhase += Time.unscaledDeltaTime * Mathf.Lerp(1.35f, _headBobFrequency, gait);
                var walkBob = Mathf.Sin(_headBobPhase * 2f) * _headBobAmplitude * gait;
                var sideBob = Mathf.Sin(_headBobPhase) * _headBobSideAmplitude * gait;
                var idleBreath = Mathf.Sin(Time.unscaledTime * 1.15f) * 0.006f;
                var eyeHeight = _mounted ? 2.78f : 1.62f;
                var mountForwardOffset = _mounted ? 0.12f : 0f;
                transform.position = _target.position + Vector3.up * (eyeHeight + walkBob + idleBreath + _jumpOffset)
                    + _target.right * sideBob + _target.forward * mountForwardOffset;
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                return;
            }

            var scroll = _input != null ? _input.Zoom : 0f;
            if (Mathf.Abs(scroll) > 0.001f)
                _zoomDistance = Mathf.Clamp(_zoomDistance - scroll * _zoomSpeed * 10f,
                    _minZoomDistance, _maxZoomDistance);
            if (!_captureMouseForGesture)
            {
                var look = _input != null ? _input.LookDelta : Vector2.zero;
                _yaw += look.x * _mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch + look.y * _mouseSensitivity * (_invertMouseY ? 1f : -1f),
                    _thirdPersonMinPitch, _thirdPersonMaxPitch);
            }

            var focusHeight = _mounted ? 2.0f : 1.25f;
            var focus = _target.position + Vector3.up * (focusHeight + _jumpOffset);
            var direction = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back;
            var desired = focus + direction * _zoomDistance;
            var ray = desired - focus;
            var distance = ray.magnitude;
            if (distance > 0.001f && Physics.SphereCast(focus, _collisionRadius, ray.normalized,
                out var hit, distance, 1 << 2, QueryTriggerInteraction.Ignore))
                desired = focus + ray.normalized * Mathf.Max(0.35f, hit.distance - 0.18f);
            transform.position = Vector3.Lerp(transform.position, desired,
                1f - Mathf.Exp(-_followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
        }
    }
}