using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Presentation-only hybrid camera. It never changes authoritative world position.</summary>
    public sealed class PresentationCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0, 8, -10);
        [SerializeField] private float followSharpness = 5f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float thirdPersonMinPitch = 18f;
        [SerializeField] private float thirdPersonMaxPitch = 62f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float minZoomDistance = 5f;
        [SerializeField] private float maxZoomDistance = 17f;
        [SerializeField] private float collisionRadius = 0.22f;
        private Transform _target;
        private bool _firstPerson;
        private float _yaw;
        private float _pitch;
        private float _zoomDistance;

        public bool IsFirstPerson => _firstPerson;

        private void Awake()
        {
            _zoomDistance = offset.magnitude;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null) _yaw = target.eulerAngles.y;
            _pitch = 28f;
        }

        public void SetFirstPerson(bool enabled)
        {
            _firstPerson = enabled;
            if (_target != null) _yaw = _target.eulerAngles.y;
            _pitch = enabled ? 0f : 28f;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            if (_firstPerson)
            {
                // Mouse always controls view. RMB is reserved for future spell-weaving input;
                // while it is held, camera look pauses so cursor motion can trace the weave.
                if (!Input.GetMouseButton(1))
                {
                    _yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                    _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -75f, 75f);
                    _target.rotation = Quaternion.Euler(0f, _yaw, 0f);
                }

                transform.position = _target.position + Vector3.up * 1.62f;
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                return;
            }

            var scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
                _zoomDistance = Mathf.Clamp(_zoomDistance - scroll * zoomSpeed * 10f,
                    minZoomDistance, maxZoomDistance);

            // Mouse always orbits the third-person camera. RMB is reserved for spell weaving;
            // camera orbit pauses while held so the same mouse movement can trace a pattern.
            if (!Input.GetMouseButton(1))
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
                out var hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                desired = focus + ray.normalized * Mathf.Max(0.35f, hit.distance - 0.18f);
            }

            transform.position = Vector3.Lerp(transform.position, desired,
                1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
        }
    }
}