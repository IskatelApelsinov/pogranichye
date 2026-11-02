using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    public sealed class PresentationCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0, 8, -10);
        [SerializeField] private float followSharpness = 5f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        private Transform _target;
        private bool _firstPerson;
        private float _yaw;
        private float _pitch;

        public bool IsFirstPerson => _firstPerson;

        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null) _yaw = target.eulerAngles.y;
        }

        public void SetFirstPerson(bool enabled)
        {
            _firstPerson = enabled;
            if (_target != null) _yaw = _target.eulerAngles.y;
            _pitch = 0f;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            if (_firstPerson)
            {
                if (Input.GetMouseButton(1))
                {
                    _yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                    _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -75f, 75f);
                    _target.rotation = Quaternion.Euler(0f, _yaw, 0f);
                }

                transform.position = _target.position + Vector3.up * 1.62f;
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                return;
            }

            var desired = _target.position + Quaternion.Euler(0f, _target.eulerAngles.y, 0f) * offset;
            transform.position = Vector3.Lerp(transform.position, desired,
                1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(_target.position + Vector3.up - transform.position,
                Vector3.up);
        }
    }
}