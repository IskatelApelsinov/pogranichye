using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    public sealed class PresentationCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0, 8, -10);
        [SerializeField] private float followSharpness = 5f;
        private Transform _target;

        public void SetTarget(Transform target) { _target = target; }

        private void LateUpdate()
        {
            if (_target == null) return;
            var desired = _target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired,
                1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(_target.position + Vector3.up - transform.position,
                Vector3.up);
        }
    }
}