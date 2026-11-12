using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Upright world-space nameplate. Unlike GUI overlays, its renderer is hidden whenever
    /// solid scene geometry lies between the camera and the label.
    /// </summary>
    public sealed class BillboardLabel : MonoBehaviour
    {
        private const int HitCapacity = 32;
        private readonly RaycastHit[] _hits = new RaycastHit[HitCapacity];
        private Camera _camera;
        private Renderer _renderer;
        private Transform _entityRoot;
        private float _nextOcclusionCheck;
        private const float OcclusionCheckInterval = 0.20f;
        private const float MaxLabelDistance = 90f;

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
            if (_renderer == null) return;

            // Avoid physics queries for labels outside the view or beyond the useful
            // settlement reading distance. This matters because every nearby resident
            // owns a world-space label.
            var viewport = _camera.WorldToViewportPoint(transform.position);
            var toLabel = transform.position - _camera.transform.position;
            var distance = toLabel.magnitude;
            if (viewport.z <= 0f || viewport.x < -0.08f || viewport.x > 1.08f ||
                viewport.y < -0.08f || viewport.y > 1.08f || distance > MaxLabelDistance)
            {
                _renderer.enabled = false;
                return;
            }

            if (Time.unscaledTime < _nextOcclusionCheck) return;
            _nextOcclusionCheck = Time.unscaledTime + OcclusionCheckInterval;
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
                if (IsOwnCollider(hitTransform))
                    continue;
                blocked = true;
                break;
            }

            // RaycastNonAlloc does not guarantee which hits fit when the buffer is full.
            // In a dense street, the wall may be omitted behind many nearby colliders.
            // Only in that saturated case, walk through the owner's own colliders with
            // allocation-free single-hit casts until the first real occluder is found.
            if (!blocked && count >= _hits.Length)
            {
                var rayOrigin = _camera.transform.position;
                var remaining = distance - 0.04f;
                var resolved = false;
                for (var skip = 0; skip < 16 && remaining > 0.02f; skip++)
                {
                    RaycastHit nearest;
                    if (!Physics.Raycast(rayOrigin, direction, out nearest, remaining,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        resolved = true;
                        break;
                    }

                    if (!IsOwnCollider(nearest.collider != null ? nearest.collider.transform : null))
                    {
                        blocked = true;
                        resolved = true;
                        break;
                    }

                    var advance = nearest.distance + 0.025f;
                    rayOrigin += direction * advance;
                    remaining -= advance;
                }

                // Fail closed if an unusually complex owner could not be traversed safely.
                if (!resolved) blocked = true;
            }

            _renderer.enabled = !blocked;
        }

        private bool IsOwnCollider(Transform hitTransform)
        {
            return hitTransform != null && _entityRoot != null &&
                (hitTransform == _entityRoot || hitTransform.IsChildOf(_entityRoot));
        }
    }
}
