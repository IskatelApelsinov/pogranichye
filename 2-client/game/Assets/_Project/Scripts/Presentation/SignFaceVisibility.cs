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

            var towardViewer = _camera.transform.position - transform.position;
            towardViewer.y = 0f;
            if (towardViewer.sqrMagnitude < 0.0001f) return;

            var visible = Vector3.Dot(_outwardNormal, towardViewer.normalized) > 0.05f;
            if (_renderer.enabled != visible) _renderer.enabled = visible;
        }
    }
}
