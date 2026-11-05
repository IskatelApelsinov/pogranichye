using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Presentation-only hybrid camera. It never changes authoritative world position.</summary>
    public sealed class PresentationCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0, 18, -26);
        [SerializeField] private float followSharpness = 5f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float thirdPersonMinPitch = 18f;
        [SerializeField] private float thirdPersonMaxPitch = 62f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float minZoomDistance = 5f;
        [SerializeField] private float maxZoomDistance = 80f;
        [SerializeField] private float collisionRadius = 0.22f;
        [Header("First-person feel")]
        [SerializeField] private float headBobAmplitude = 0.035f;
        [SerializeField] private float headBobSideAmplitude = 0.012f;
        [SerializeField] private float headBobFrequency = 9f;
        private float _headBobPhase;
        private Transform _target;
        private bool _firstPerson;
        private float _yaw;
        private float _pitch;
        private float _zoomDistance;
        private bool _captureMouseForGesture;

        public bool IsFirstPerson => _firstPerson;
        public bool CaptureMouseForGesture => _captureMouseForGesture;

        /// <summary>Called by a future contextual tool/magic mode when raw mouse motion is a gesture, not camera look.</summary>
        public void SetMouseGestureCapture(bool active)
        {
            _captureMouseForGesture = active;
        }

        private void Awake()
        {
            _zoomDistance = offset.magnitude;
        }

        public void SetTarget(Transform target)
        {
            if (_target == target) return;
            _target = target;
            if (target != null) _yaw = target.eulerAngles.y;
            _pitch = _firstPerson ? 0f : 28f;
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

            if (_firstPerson)
            {
                // Mouse always controls view unless an active tool explicitly captures it for a gesture.
                if (!_captureMouseForGesture)
                {
                    _yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                    _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -75f, 75f);
                    _target.rotation = Quaternion.Euler(0f, _yaw, 0f);
                }

                var motion = _target.GetComponent<EntityMotion>();
                var gait = motion != null ? motion.LocomotionAmount : 0f;
                _headBobPhase += Time.unscaledDeltaTime * Mathf.Lerp(1.35f, headBobFrequency, gait);
                var walkBob = Mathf.Sin(_headBobPhase * 2f) * headBobAmplitude * gait;
                var sideBob = Mathf.Sin(_headBobPhase) * headBobSideAmplitude * gait;
                var idleBreath = Mathf.Sin(Time.unscaledTime * 1.15f) * 0.006f;
                transform.position = _target.position + Vector3.up * (1.62f + walkBob + idleBreath)
                    + _target.right * sideBob;
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                return;
            }

            var scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
                _zoomDistance = Mathf.Clamp(_zoomDistance - scroll * zoomSpeed * 10f,
                    minZoomDistance, maxZoomDistance);

            // Mouse always orbits the camera. RMB alone does not freeze look: a contextual mode
            // must explicitly request gesture capture (e.g. tracing a spell or casting a fishing line).
            if (!_captureMouseForGesture)
            {
                _yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity,
                    thirdPersonMinPitch, thirdPersonMaxPitch);
            }

            var focus = _target.position + Vector3.up * 1.25f;
            var direction = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back;
            var desired = focus + direction * _zoomDistance;

            // Keep the camera from clipping through tavern walls, houses and other solid geometry.
            var ray = desired - focus;
            var distance = ray.magnitude;
            if (distance > 0.001f && Physics.SphereCast(focus, collisionRadius, ray.normalized,
                out var hit, distance, 1 << 2, QueryTriggerInteraction.Ignore))
            {
                desired = focus + ray.normalized * Mathf.Max(0.35f, hit.distance - 0.18f);
            }

            transform.position = Vector3.Lerp(transform.position, desired,
                1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
        }
    }
}