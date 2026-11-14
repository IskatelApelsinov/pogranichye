using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Enables only the outward-facing inscription on a physical signboard.
    /// TextMesh planes can otherwise reveal the opposite inscription through the thin board.
    /// </summary>
    public sealed class SignFaceVisibility : MonoBehaviour
    {
        private Renderer _renderer;
        private Camera _camera;
        private Vector3 _outwardNormal;
        private const float MaxSignViewDistance = 32f;
        private const float OcclusionPadding = 0.12f;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            // TextMesh's visible face points along local +Z.
            _outwardNormal = transform.rotation * Vector3.forward;
        }

        private void LateUpdate()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) _camera = FindAnyObjectByType<Camera>();
            }
            if (_renderer == null || _camera == null) return;

            var toCamera = _camera.transform.position - transform.position;
            var distance = toCamera.magnitude;
            if (distance < 0.001f) return;

            // A settlement sign is a physical prop, not a world-space waypoint:
            // never let it dominate the horizon or remain readable through buildings.
            var towardViewer = toCamera;
            towardViewer.y = 0f;
            var visible = distance <= MaxSignViewDistance &&
                towardViewer.sqrMagnitude > 0.0001f &&
                Vector3.Dot(_outwardNormal, towardViewer.normalized) > 0.05f;

            if (visible)
            {
                var rayOrigin = _camera.transform.position;
                var rayDirection = toCamera / distance;
                if (Physics.Raycast(rayOrigin, rayDirection, out var hit,
                    Mathf.Max(0f, distance - OcclusionPadding),
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    visible = false;
            }

            if (_renderer.enabled != visible) _renderer.enabled = visible;
        }
    }
}
