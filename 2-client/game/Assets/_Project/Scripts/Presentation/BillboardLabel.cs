using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Upright world-space nameplate. Unlike GUI overlays, its renderer is hidden whenever
    /// solid scene geometry lies between the camera and the label.
    /// </summary>
    public sealed class BillboardLabel : MonoBehaviour
    {
        private const int HitCapacity = 24;
        private readonly RaycastHit[] _hits = new RaycastHit[HitCapacity];
        private Camera _camera;
        private Renderer _renderer;
        private Transform _entityRoot;
        private float _nextOcclusionCheck;

        private void OnEnable()
        {
            _camera = Camera.main;
            _renderer = GetComponent<Renderer>();
            _entityRoot = transform.parent;
        }

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            var towardCamera = Vector3.ProjectOnPlane(
                _camera.transform.position - transform.position, Vector3.up);
            if (towardCamera.sqrMagnitude < 0.0001f)
                towardCamera = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
            if (towardCamera.sqrMagnitude < 0.0001f)
                towardCamera = Vector3.forward;

            // TextMesh's front normal is local -Z: point +Z away from the viewer.
            transform.rotation = Quaternion.LookRotation(-towardCamera.normalized, Vector3.up);

            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer == null || Time.unscaledTime < _nextOcclusionCheck) return;
            _nextOcclusionCheck = Time.unscaledTime + 0.08f;

            var toLabel = transform.position - _camera.transform.position;
            var distance = toLabel.magnitude;
            if (distance <= 0.05f)
            {
                _renderer.enabled = true;
                return;
            }

            var direction = toLabel / distance;
            var count = Physics.RaycastNonAlloc(_camera.transform.position, direction, _hits,
                distance - 0.04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var blocked = false;
            for (var i = 0; i < count; i++)
            {
                var collider = _hits[i].collider;
                if (collider == null) continue;
                var hitTransform = collider.transform;
                // Ignore the character's own capsule/body; only external world geometry occludes it.
                if (_entityRoot != null &&
                    (hitTransform == _entityRoot || hitTransform.IsChildOf(_entityRoot)))
                    continue;
                blocked = true;
                break;
            }
            _renderer.enabled = !blocked;
        }
    }
}
